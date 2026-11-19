using Microsoft.AspNetCore.StaticFiles;
using Npgsql;

namespace Holodeck.Web.Rooms;

// Packets in channels. Each recipient gets its packets one at a time, in the order they were sent to it, across all
// its channels. Asking for the next packet means the previous one is done; a new session (a restart) gets the
// unfinished packet again.
public sealed class PacketStore(NpgsqlDataSource database, RoomStore room, RoomPaths paths, RoomSignals signals)
{
  private const string ReceivedFolder = "received";
  private const string DefaultMediaType = "application/octet-stream";
  private static readonly FileExtensionContentTypeProvider MediaTypes = new();

  // The legacy two-party form: post to the channel this pair shares.
  public async Task<Packet> SendByNameAsync(RoomMember sender, string recipientName, string note, List<OutgoingFile> files, CancellationToken token)
  {
    Channel channel = await FindPairChannelAsync(sender, recipientName);
    return await SendAsync(sender, channel.Id, note, files, token);
  }

  public async Task<Channel> FindPairChannelAsync(RoomMember sender, string recipientName)
  {
    List<RoomMember> members = await room.ListMembersAsync(sender.ProjectId);
    foreach (RoomMember member in members)
    {
      bool isNamed = string.Equals(member.Name, recipientName, StringComparison.OrdinalIgnoreCase);
      if (!isNamed || member.IsRemoved || member.Id == sender.Id)
      {
        continue;
      }

      Channel? channel = await room.FindPairChannelAsync(sender.ProjectId, sender.Id, member.Id);
      if (channel is null)
      {
        throw new KeyNotFoundException($"You do not share a channel with just {member.Name}. Send to a channel instead.");
      }

      return channel;
    }

    throw new KeyNotFoundException($"There is nobody called '{recipientName}' in this room.");
  }

  public async Task<Packet> SendAsync(RoomMember sender, string channelIdOrName, string note, List<OutgoingFile> files, CancellationToken token)
  {
    SendResult result = await SendAsync(sender, channelIdOrName, note, files, null, token);
    return result.Packet;
  }

  // With a key, one sender commits at most one packet per key. A retry with the same channel, note, and attachment
  // bytes reports that packet, even if the sender could no longer send it now; anything else under that key is a conflict.
  public async Task<SendResult> SendAsync(RoomMember sender, string channelIdOrName, string note, List<OutgoingFile> files, string? idempotencyKey, CancellationToken token)
  {
    ValidateContent(note, files);
    if (idempotencyKey is not null)
    {
      ValidateKey(idempotencyKey);
    }

    if (sender.IsRemoved)
    {
      throw new InvalidOperationException("Removed members cannot send.");
    }

    Channel channel = await room.GetChannelAsync(sender.ProjectId, channelIdOrName);
    StoredPacket stored = await StoreAsync(sender, channel, note, files, idempotencyKey, token);
    if (!stored.IsReplay)
    {
      foreach (string recipientId in stored.RecipientIds)
      {
        signals.Pulse(sender.ProjectId, recipientId);
      }

      signals.Pulse(sender.ProjectId, sender.Id);
    }

    return new SendResult(stored.Packet, stored.IsReplay);
  }

  public async Task<Delivery?> NextAsync(RoomMember agent, string sessionHash, CancellationToken token)
  {
    await using NpgsqlConnection connection = await database.OpenConnectionAsync(token);
    await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token);
    long done;
    long? inFlight = null;
    string? inFlightSession = null;
    await using (NpgsqlCommand position = new("SELECT done_sequence, in_flight_sequence, in_flight_session FROM room_members WHERE id = $1 FOR UPDATE", connection, transaction))
    {
      position.Parameters.AddWithValue(agent.Id);
      await using NpgsqlDataReader reader = await position.ExecuteReaderAsync(token);
      if (!await reader.ReadAsync(token))
      {
        throw new KeyNotFoundException("That member is not in this room.");
      }

      done = reader.GetInt64(0);
      if (!reader.IsDBNull(1))
      {
        inFlight = reader.GetInt64(1);
        inFlightSession = reader.IsDBNull(2) ? null : reader.GetString(2);
      }
    }

    long sequence = done + 1;
    if (inFlight is not null)
    {
      bool isSameSession = inFlightSession == sessionHash;
      sequence = isSameSession ? inFlight.Value + 1 : inFlight.Value;
      if (isSameSession)
      {
        done = inFlight.Value;
      }
    }

    sequence = await FirstWaitingAsync(connection, transaction, agent, sequence, token);
    Delivery? delivery = await DeliverAsync(connection, transaction, agent, sequence, token);
    await using (NpgsqlCommand update = new("UPDATE room_members SET done_sequence = $2, in_flight_sequence = $3, in_flight_session = $4 WHERE id = $1", connection, transaction))
    {
      update.Parameters.AddWithValue(agent.Id);
      update.Parameters.AddWithValue(done);
      update.Parameters.AddWithValue(delivery is null ? DBNull.Value : sequence);
      update.Parameters.AddWithValue(delivery is null ? DBNull.Value : sessionHash);
      await update.ExecuteNonQueryAsync(token);
    }

    await transaction.CommitAsync(token);
    return delivery;
  }

  // Deleting a channel removes its queued deliveries, leaving gaps in a recipient's sequence; delivery resumes at the
  // first one still waiting instead of stalling on a number that no longer exists.
  private static async Task<long> FirstWaitingAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, RoomMember agent, long sequence, CancellationToken token)
  {
    await using NpgsqlCommand command = new("SELECT MIN(recipient_sequence) FROM room_deliveries WHERE recipient_id = $1 AND recipient_sequence >= $2", connection, transaction);
    command.Parameters.AddWithValue(agent.Id);
    command.Parameters.AddWithValue(sequence);
    object? first = await command.ExecuteScalarAsync(token);
    return first is long waiting ? waiting : sequence;
  }

  public async Task<List<Packet>> HistoryAsync(string projectId, string channelId, long after, CancellationToken token)
  {
    List<Packet> packets = new();
    await using (NpgsqlCommand command = database.CreateCommand("SELECT id, channel_id, sender_id, note, sent_at FROM room_packets WHERE project_id = $1 AND channel_id = $2 AND id > $3 ORDER BY id LIMIT $4"))
    {
      command.Parameters.AddWithValue(projectId);
      command.Parameters.AddWithValue(channelId);
      command.Parameters.AddWithValue(after);
      command.Parameters.AddWithValue(RoomLimits.HistoryPageSize);
      await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
      while (await reader.ReadAsync(token))
      {
        packets.Add(new Packet(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), new List<PacketFile>(), reader.GetFieldValue<DateTimeOffset>(4)));
      }
    }

    if (packets.Count == 0)
    {
      return packets;
    }

    long[] ids = new long[packets.Count];
    for (int index = 0; index < packets.Count; index++)
    {
      ids[index] = packets[index].Id;
    }

    await using NpgsqlCommand files = database.CreateCommand("SELECT packet_id, name, media_type, size FROM room_packet_files WHERE packet_id = ANY($1) ORDER BY packet_id, ordinal");
    files.Parameters.AddWithValue(ids);
    await using NpgsqlDataReader fileReader = await files.ExecuteReaderAsync(token);
    int current = 0;
    while (await fileReader.ReadAsync(token))
    {
      long packetId = fileReader.GetInt64(0);
      while (packets[current].Id != packetId)
      {
        current++;
      }

      packets[current].Files.Add(new PacketFile(fileReader.GetString(1), fileReader.GetString(2), fileReader.GetInt64(3)));
    }

    return packets;
  }

  public string FilePath(string projectId, long packetId, string name)
  {
    bool isPlainName = name.Length > 0 && name.AsSpan().IndexOfAny('/', '\\') < 0 && name != "." && name != "..";
    string path = Path.Combine(paths.PacketDirectory(projectId, packetId), name);
    if (!isPlainName || !File.Exists(path))
    {
      throw new KeyNotFoundException("That file is not part of this packet.");
    }

    return path;
  }

  private async Task<StoredPacket> StoreAsync(RoomMember sender, Channel channel, string note, List<OutgoingFile> files, string? key, CancellationToken token)
  {
    await using NpgsqlConnection connection = await database.OpenConnectionAsync(token);
    await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token);
    if (key is not null)
    {
      // Requests with one sender's key run one at a time, so concurrent retries see the first one's commit.
      await using (NpgsqlCommand lockKey = new("SELECT pg_advisory_xact_lock(hashtext($1), hashtext($2))", connection, transaction))
      {
        lockKey.Parameters.AddWithValue(sender.Id);
        lockKey.Parameters.AddWithValue(key);
        await lockKey.ExecuteNonQueryAsync(token);
      }

      SentKey? sent = await FindKeyAsync(connection, transaction, sender, key, token);
      if (sent is not null)
      {
        StoredPacket replay = await ReplayAsync(connection, transaction, sent, channel, note, files, token);
        await transaction.CommitAsync(token);
        return replay;
      }
    }

    List<string> recipientIds = await LockChannelAsync(connection, transaction, sender, channel, token);
    long packetId;
    DateTimeOffset sentAt;
    await using (NpgsqlCommand insert = new("INSERT INTO room_packets(project_id, channel_id, sender_id, note, sent_at) VALUES ($1, $2, $3, $4, now()) RETURNING id, sent_at", connection, transaction))
    {
      insert.Parameters.AddWithValue(sender.ProjectId);
      insert.Parameters.AddWithValue(channel.Id);
      insert.Parameters.AddWithValue(sender.Id);
      insert.Parameters.AddWithValue(note);
      await using NpgsqlDataReader reader = await insert.ExecuteReaderAsync(token);
      await reader.ReadAsync(token);
      packetId = reader.GetInt64(0);
      sentAt = reader.GetFieldValue<DateTimeOffset>(1);
    }

    foreach (string recipientId in recipientIds)
    {
      await using NpgsqlCommand enqueue = new("WITH allocated AS (UPDATE room_members SET next_sequence = next_sequence + 1 WHERE id = $2 RETURNING next_sequence) INSERT INTO room_deliveries(packet_id, recipient_id, recipient_sequence) SELECT $1, $2, next_sequence FROM allocated", connection, transaction);
      enqueue.Parameters.AddWithValue(packetId);
      enqueue.Parameters.AddWithValue(recipientId);
      await enqueue.ExecuteNonQueryAsync(token);
    }

    string directory = paths.PacketDirectory(sender.ProjectId, packetId);
    List<PacketFile> stored = new();
    try
    {
      List<AttachmentDigest> digests = await StoreFilesAsync(connection, transaction, packetId, directory, files, stored, token);
      if (key is not null)
      {
        await RememberKeyAsync(connection, transaction, packetId, key, SendFingerprint.Compute(channel.Id, note, digests), token);
      }
    }
    catch
    {
      // Commit was never attempted, so disposing the transaction rolls it back and these copies belong to no packet.
      DeleteStaged(directory);
      throw;
    }

    // A failed or interrupted commit may still have committed, so its files stay. An uncommitted packet id is never
    // reused, so leftover copies are only unreferenced disk use, never another packet's files.
    await transaction.CommitAsync(token);
    return new StoredPacket(new Packet(packetId, channel.Id, sender.Id, note, stored, sentAt), recipientIds, false);
  }

  private static void DeleteStaged(string directory)
  {
    if (Directory.Exists(directory))
    {
      Directory.Delete(directory, true);
    }
  }

  // Sends in one channel commit one at a time, in id order, so a reader paging by id never skips one. Membership
  // changes take the same channel row lock, and membership is read by a fresh statement after the lock is held, so a
  // removal or pause either happens before this packet or after it.
  private static async Task<List<string>> LockChannelAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, RoomMember sender, Channel channel, CancellationToken token)
  {
    bool isPaused;
    await using (NpgsqlCommand lockChannel = new("SELECT paused FROM room_channels WHERE id = $1 FOR UPDATE", connection, transaction))
    {
      lockChannel.Parameters.AddWithValue(channel.Id);
      object? paused = await lockChannel.ExecuteScalarAsync(token);
      if (paused is null)
      {
        throw new KeyNotFoundException("That channel does not exist.");
      }

      isPaused = (bool)paused;
    }

    List<string> recipientIds = new();
    bool isMember = false;
    await using (NpgsqlCommand members = new("SELECT member_id FROM room_channel_members WHERE channel_id = $1 ORDER BY member_id", connection, transaction))
    {
      members.Parameters.AddWithValue(channel.Id);
      await using NpgsqlDataReader reader = await members.ExecuteReaderAsync(token);
      while (await reader.ReadAsync(token))
      {
        string memberId = reader.GetString(0);
        if (memberId == sender.Id)
        {
          isMember = true;
        }
        else
        {
          recipientIds.Add(memberId);
        }
      }
    }

    if (!isMember)
    {
      throw new InvalidOperationException($"You are not a member of '{channel.Name}'.");
    }

    if (isPaused && !sender.IsYou)
    {
      throw new InvalidOperationException($"'{channel.Name}' is paused.");
    }

    return recipientIds;
  }

  private static async Task<SentKey?> FindKeyAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, RoomMember sender, string key, CancellationToken token)
  {
    await using NpgsqlCommand select = new("SELECT id, send_fingerprint FROM room_packets WHERE project_id = $1 AND sender_id = $2 AND send_key = $3", connection, transaction);
    select.Parameters.AddWithValue(sender.ProjectId);
    select.Parameters.AddWithValue(sender.Id);
    select.Parameters.AddWithValue(key);
    await using NpgsqlDataReader reader = await select.ExecuteReaderAsync(token);
    if (!await reader.ReadAsync(token))
    {
      return null;
    }

    return new SentKey(reader.GetInt64(0), reader.GetString(1));
  }

  private static async Task RememberKeyAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, long packetId, string key, string fingerprint, CancellationToken token)
  {
    await using NpgsqlCommand update = new("UPDATE room_packets SET send_key = $2, send_fingerprint = $3 WHERE id = $1", connection, transaction);
    update.Parameters.AddWithValue(packetId);
    update.Parameters.AddWithValue(key);
    update.Parameters.AddWithValue(fingerprint);
    await update.ExecuteNonQueryAsync(token);
  }

  // The retry's attachments are read now and compared with the bytes the first request actually stored.
  private static async Task<StoredPacket> ReplayAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, SentKey sent, Channel channel, string note, List<OutgoingFile> files, CancellationToken token)
  {
    List<AttachmentDigest> digests = new();
    foreach (OutgoingFile file in files)
    {
      digests.Add(await Attachments.DigestAsync(file, token));
    }

    if (SendFingerprint.Compute(channel.Id, note, digests) != sent.Fingerprint)
    {
      throw new InvalidOperationException("That idempotency key was already used for a different message.");
    }

    Packet packet = await ReadPacketAsync(connection, transaction, sent.PacketId, token);
    return new StoredPacket(packet, new List<string>(), true);
  }

  private static async Task<Packet> ReadPacketAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, long packetId, CancellationToken token)
  {
    Packet packet;
    await using (NpgsqlCommand select = new("SELECT id, channel_id, sender_id, note, sent_at FROM room_packets WHERE id = $1", connection, transaction))
    {
      select.Parameters.AddWithValue(packetId);
      await using NpgsqlDataReader reader = await select.ExecuteReaderAsync(token);
      await reader.ReadAsync(token);
      packet = new Packet(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), new List<PacketFile>(), reader.GetFieldValue<DateTimeOffset>(4));
    }

    await using NpgsqlCommand files = new("SELECT name, media_type, size FROM room_packet_files WHERE packet_id = $1 ORDER BY ordinal", connection, transaction);
    files.Parameters.AddWithValue(packetId);
    await using NpgsqlDataReader fileReader = await files.ExecuteReaderAsync(token);
    while (await fileReader.ReadAsync(token))
    {
      packet.Files.Add(new PacketFile(fileReader.GetString(0), fileReader.GetString(1), fileReader.GetInt64(2)));
    }

    return packet;
  }

  private static async Task<List<AttachmentDigest>> StoreFilesAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, long packetId, string directory, List<OutgoingFile> files, List<PacketFile> stored, CancellationToken token)
  {
    List<AttachmentDigest> digests = new();
    if (files.Count == 0)
    {
      return digests;
    }

    Directory.CreateDirectory(directory);
    for (int ordinal = 0; ordinal < files.Count; ordinal++)
    {
      string name = FreeName(directory, files[ordinal].Name);
      AttachmentDigest digest = await Attachments.CopyAsync(files[ordinal], Path.Combine(directory, name), token);
      PacketFile file = new(name, MediaTypeOf(name), digest.Size);
      await using NpgsqlCommand insert = new("INSERT INTO room_packet_files(packet_id, ordinal, name, media_type, size) VALUES ($1, $2, $3, $4, $5)", connection, transaction);
      insert.Parameters.AddWithValue(packetId);
      insert.Parameters.AddWithValue(ordinal);
      insert.Parameters.AddWithValue(file.Name);
      insert.Parameters.AddWithValue(file.MediaType);
      insert.Parameters.AddWithValue(file.Size);
      await insert.ExecuteNonQueryAsync(token);
      stored.Add(file);
      digests.Add(digest);
    }

    return digests;
  }

  private async Task<Delivery?> DeliverAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, RoomMember agent, long sequence, CancellationToken token)
  {
    long packetId;
    string from;
    string note;
    string channelId;
    string channelName;
    bool isLegacy;
    string senderId;
    DateTimeOffset sentAt;
    const string Sql = "SELECT p.id, s.name, p.note, c.id, c.name, c.legacy, p.sender_id, p.sent_at FROM room_deliveries d JOIN room_packets p ON p.id = d.packet_id JOIN room_members s ON s.id = p.sender_id JOIN room_channels c ON c.id = p.channel_id WHERE d.recipient_id = $1 AND d.recipient_sequence = $2";
    await using (NpgsqlCommand select = new(Sql, connection, transaction))
    {
      select.Parameters.AddWithValue(agent.Id);
      select.Parameters.AddWithValue(sequence);
      await using NpgsqlDataReader reader = await select.ExecuteReaderAsync(token);
      if (!await reader.ReadAsync(token))
      {
        return null;
      }

      packetId = reader.GetInt64(0);
      from = reader.GetString(1);
      note = reader.GetString(2);
      channelId = reader.GetString(3);
      channelName = reader.GetString(4);
      isLegacy = reader.GetBoolean(5);
      senderId = reader.GetString(6);
      sentAt = reader.GetFieldValue<DateTimeOffset>(7);
    }

    // Legacy channels keep the paths their agents always saw; other channels keep each channel's files apart.
    string folder = isLegacy ? $"{ReceivedFolder}/{from}" : $"{ReceivedFolder}/{channelId}/{from}";
    List<string> delivered = await PlaceFilesAsync(connection, transaction, agent, packetId, folder, token);
    return new Delivery(channelId, channelName, from, note, delivered, packetId, senderId, sentAt);
  }

  // Copies a packet's files onto this recipient's table once, and remembers where they went for redelivery.
  private async Task<List<string>> PlaceFilesAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, RoomMember agent, long packetId, string folder, CancellationToken token)
  {
    List<int> ordinals = new();
    List<string> names = new();
    List<string?> previous = new();
    const string Sql = "SELECT f.ordinal, f.name, d.delivered_path FROM room_packet_files f LEFT JOIN room_delivery_files d ON d.packet_id = f.packet_id AND d.ordinal = f.ordinal AND d.recipient_id = $2 WHERE f.packet_id = $1 ORDER BY f.ordinal";
    await using (NpgsqlCommand select = new(Sql, connection, transaction))
    {
      select.Parameters.AddWithValue(packetId);
      select.Parameters.AddWithValue(agent.Id);
      await using NpgsqlDataReader reader = await select.ExecuteReaderAsync(token);
      while (await reader.ReadAsync(token))
      {
        ordinals.Add(reader.GetInt32(0));
        names.Add(reader.GetString(1));
        previous.Add(reader.IsDBNull(2) ? null : reader.GetString(2));
      }
    }

    List<string> delivered = new();
    string table = paths.Table(agent.ProjectId, agent.Id);
    string source = paths.PacketDirectory(agent.ProjectId, packetId);
    for (int index = 0; index < names.Count; index++)
    {
      string? relative = previous[index];
      if (relative is null)
      {
        string target = Path.Combine(table, folder);
        Directory.CreateDirectory(target);
        relative = $"{folder}/{FreeName(target, names[index])}";
        await using NpgsqlCommand remember = new("INSERT INTO room_delivery_files(packet_id, recipient_id, ordinal, delivered_path) VALUES ($1, $2, $3, $4)", connection, transaction);
        remember.Parameters.AddWithValue(packetId);
        remember.Parameters.AddWithValue(agent.Id);
        remember.Parameters.AddWithValue(ordinals[index]);
        remember.Parameters.AddWithValue(relative);
        await remember.ExecuteNonQueryAsync(token);
      }

      string placed = Path.Combine(table, relative);
      if (!File.Exists(placed))
      {
        Directory.CreateDirectory(Path.GetDirectoryName(placed)!);
        File.Copy(Path.Combine(source, names[index]), placed);
      }

      delivered.Add(relative);
    }

    return delivered;
  }

  private static void ValidateContent(string note, List<OutgoingFile> files)
  {
    if (note.Length > RoomLimits.MaxNoteCharacters)
    {
      throw new ArgumentException($"Keep a note under {RoomLimits.MaxNoteCharacters} characters.");
    }

    if (files.Count > RoomLimits.MaxFilesPerPacket)
    {
      throw new ArgumentException($"Send at most {RoomLimits.MaxFilesPerPacket} files at once.");
    }

    if (note.Length == 0 && files.Count == 0)
    {
      throw new ArgumentException("Say something or attach a file.");
    }
  }

  private static void ValidateKey(string key)
  {
    bool isBounded = key.Length > 0 && key.Length <= RoomLimits.MaxIdempotencyKeyLength;
    if (!isBounded)
    {
      throw new ArgumentException($"Use an idempotency key of 1 to {RoomLimits.MaxIdempotencyKeyLength} characters.");
    }

    foreach (char character in key)
    {
      bool isAllowed = char.IsAsciiLetterOrDigit(character) || character == '-' || character == '_' || character == '.' || character == ':';
      if (!isAllowed)
      {
        throw new ArgumentException("An idempotency key may contain only ASCII letters, digits, hyphens, underscores, dots, and colons.");
      }
    }
  }

  // "draft.md" stays as is unless taken, then becomes "draft (2).md", "draft (3).md", ...
  private static string FreeName(string directory, string name)
  {
    string candidate = name;
    int number = 1;
    while (File.Exists(Path.Combine(directory, candidate)))
    {
      number++;
      candidate = $"{Path.GetFileNameWithoutExtension(name)} ({number}){Path.GetExtension(name)}";
    }

    return candidate;
  }

  private static string MediaTypeOf(string name)
  {
    if (MediaTypes.TryGetContentType(name, out string? mediaType))
    {
      return mediaType;
    }

    return DefaultMediaType;
  }
}
