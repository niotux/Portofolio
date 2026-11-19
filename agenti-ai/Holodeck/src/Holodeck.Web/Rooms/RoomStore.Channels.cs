using Npgsql;

namespace Holodeck.Web.Rooms;

// Channels: named conversations with an explicit member set. A packet posted to a channel reaches every other member.
public sealed partial class RoomStore
{
  private const string ChannelSelect = "SELECT c.id, c.project_id, c.name, c.legacy, c.paused, coalesce(array_agg(m.member_id ORDER BY m.member_id) FILTER (WHERE m.member_id IS NOT NULL), '{}') FROM room_channels c LEFT JOIN room_channel_members m ON m.channel_id = c.id";
  private const string PairSeparator = " & ";

  public async Task<Channel> CreateChannelAsync(string projectId, string name, List<string> memberIds)
  {
    string validName = AgentNames.Validate(name);
    string id;
    await using (NpgsqlConnection connection = await database.OpenConnectionAsync())
    {
      await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
      await LockMembershipAsync(connection, transaction, projectId);
      await RequireActiveMembersAsync(connection, transaction, projectId, memberIds);
      id = await InsertChannelAsync(connection, transaction, projectId, validName, memberIds);
      await transaction.CommitAsync();
    }

    return await GetChannelAsync(projectId, id);
  }

  public async Task<Channel> RenameChannelAsync(string projectId, string channelId, string name)
  {
    string validName = AgentNames.Validate(name);
    await using NpgsqlCommand command = database.CreateCommand("UPDATE room_channels SET name = $3 WHERE project_id = $1 AND id = $2");
    command.Parameters.AddWithValue(projectId);
    command.Parameters.AddWithValue(channelId);
    command.Parameters.AddWithValue(validName);
    await ExecuteChannelChangeAsync(command, validName);
    return await GetChannelAsync(projectId, channelId);
  }

  public async Task<Channel> SetChannelPausedAsync(string projectId, string channelId, bool isPaused)
  {
    await using NpgsqlCommand command = database.CreateCommand("UPDATE room_channels SET paused = $3 WHERE project_id = $1 AND id = $2");
    command.Parameters.AddWithValue(projectId);
    command.Parameters.AddWithValue(channelId);
    command.Parameters.AddWithValue(isPaused);
    await ExecuteChannelChangeAsync(command, "");
    return await GetChannelAsync(projectId, channelId);
  }

  public async Task DeleteChannelAsync(string projectId, string channelId)
  {
    await using NpgsqlCommand command = database.CreateCommand("DELETE FROM room_channels WHERE project_id = $1 AND id = $2");
    command.Parameters.AddWithValue(projectId);
    command.Parameters.AddWithValue(channelId);
    await ExecuteChannelChangeAsync(command, "");
  }

  // Membership changes hold the channel row lock that sends take, so a send sees the membership before or after.
  public async Task<Channel> AddChannelMemberAsync(string projectId, string channelId, string memberId)
  {
    await using (NpgsqlConnection connection = await database.OpenConnectionAsync())
    {
      await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
      await LockMembershipAsync(connection, transaction, projectId);
      await RequireActiveMembersAsync(connection, transaction, projectId, [memberId]);
      if (await LockChannelRowAsync(connection, transaction, projectId, channelId))
      {
        await using NpgsqlCommand command = new("INSERT INTO room_channel_members(channel_id, member_id) VALUES ($1, $2) ON CONFLICT DO NOTHING", connection, transaction);
        command.Parameters.AddWithValue(channelId);
        command.Parameters.AddWithValue(memberId);
        await command.ExecuteNonQueryAsync();
      }

      await transaction.CommitAsync();
    }

    return await GetChannelAsync(projectId, channelId);
  }

  public async Task<Channel> RemoveChannelMemberAsync(string projectId, string channelId, string memberId)
  {
    await using (NpgsqlConnection connection = await database.OpenConnectionAsync())
    {
      await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
      if (await LockChannelRowAsync(connection, transaction, projectId, channelId))
      {
        await using NpgsqlCommand command = new("DELETE FROM room_channel_members WHERE channel_id = $1 AND member_id = $2", connection, transaction);
        command.Parameters.AddWithValue(channelId);
        command.Parameters.AddWithValue(memberId);
        await command.ExecuteNonQueryAsync();
      }

      await transaction.CommitAsync();
    }

    return await GetChannelAsync(projectId, channelId);
  }

  private static async Task<bool> LockChannelRowAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string projectId, string channelId)
  {
    await using NpgsqlCommand command = new("SELECT id FROM room_channels WHERE project_id = $1 AND id = $2 FOR UPDATE", connection, transaction);
    command.Parameters.AddWithValue(projectId);
    command.Parameters.AddWithValue(channelId);
    return await command.ExecuteScalarAsync() is not null;
  }

  // By id, or by current name ignoring case.
  public async Task<Channel> GetChannelAsync(string projectId, string idOrName)
  {
    await using NpgsqlCommand command = database.CreateCommand($"{ChannelSelect} WHERE c.project_id = $1 AND (c.id = $2 OR lower(c.name) = lower($2)) GROUP BY c.id ORDER BY c.id = $2 DESC LIMIT 1");
    command.Parameters.AddWithValue(projectId);
    command.Parameters.AddWithValue(idOrName);
    List<Channel> channels = await ReadChannelsAsync(command);
    if (channels.Count == 0)
    {
      throw new KeyNotFoundException($"There is no channel called '{idOrName}' in this room.");
    }

    return channels[0];
  }

  public async Task<List<ChannelSummary>> ListChannelsAsync(string projectId)
  {
    const string Sql = """
      SELECT c.id, c.name, c.legacy, c.paused,
        coalesce((SELECT array_agg(member_id ORDER BY member_id) FROM room_channel_members WHERE channel_id = c.id), '{}'),
        (SELECT count(*) FROM room_packets p WHERE p.channel_id = c.id), last.note, last.sender_id, last.sent_at
      FROM room_channels c
      LEFT JOIN LATERAL (SELECT note, sender_id, sent_at FROM room_packets WHERE channel_id = c.id ORDER BY id DESC LIMIT 1) last ON true
      WHERE c.project_id = $1
      ORDER BY coalesce(last.sent_at, c.created_at) DESC, c.id
      """;
    List<ChannelSummary> channels = new();
    await using NpgsqlCommand command = database.CreateCommand(Sql);
    command.Parameters.AddWithValue(projectId);
    await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
      DateTimeOffset? lastAt = null;
      if (!reader.IsDBNull(8))
      {
        lastAt = reader.GetFieldValue<DateTimeOffset>(8);
      }

      List<string> memberIds = new(reader.GetFieldValue<string[]>(4));
      channels.Add(new ChannelSummary(reader.GetString(0), reader.GetString(1), reader.GetBoolean(2), reader.GetBoolean(3), memberIds, (int)reader.GetInt64(5), ReadOptional(reader, 6), ReadOptional(reader, 7), lastAt));
    }

    return channels;
  }

  // The channel for two members: their migrated wire if both are still in it, otherwise the oldest channel of exactly the two.
  public async Task<Channel?> FindPairChannelAsync(string projectId, string firstId, string secondId)
  {
    string? id;
    await using (NpgsqlConnection connection = await database.OpenConnectionAsync())
    {
      id = await FindPairChannelIdAsync(connection, null, projectId, firstId, secondId);
    }

    if (id is null)
    {
      return null;
    }

    return await GetChannelAsync(projectId, id);
  }

  private static async Task<string?> FindPairChannelIdAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, string projectId, string firstId, string secondId)
  {
    const string Sql = """
      SELECT c.id FROM room_channels c
      WHERE c.project_id = $1
        AND EXISTS (SELECT 1 FROM room_channel_members WHERE channel_id = c.id AND member_id = $2)
        AND EXISTS (SELECT 1 FROM room_channel_members WHERE channel_id = c.id AND member_id = $3)
        AND ((c.legacy AND EXISTS (SELECT 1 FROM room_wires w WHERE w.id = c.id AND ((w.first_id = $2 AND w.second_id = $3) OR (w.first_id = $3 AND w.second_id = $2))))
          OR (SELECT count(*) FROM room_channel_members WHERE channel_id = c.id) = 2)
      ORDER BY c.legacy DESC, c.created_at, c.id
      LIMIT 1
      """;
    await using NpgsqlCommand command = new(Sql, connection, transaction);
    command.Parameters.AddWithValue(projectId);
    command.Parameters.AddWithValue(firstId);
    command.Parameters.AddWithValue(secondId);
    return (string?)await command.ExecuteScalarAsync();
  }

  // A two-member channel named after the pair, created when the pair has none.
  public async Task<Channel> EnsurePairChannelAsync(string projectId, string firstId, string secondId)
  {
    if (firstId == secondId)
    {
      throw new ArgumentException("A channel for a pair needs two different members.");
    }

    RoomMember first = await GetMemberAsync(projectId, firstId);
    RoomMember second = await GetMemberAsync(projectId, secondId);
    string id;
    await using (NpgsqlConnection connection = await database.OpenConnectionAsync())
    {
      await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
      await LockMembershipAsync(connection, transaction, projectId);
      await RequireActiveMembersAsync(connection, transaction, projectId, [firstId, secondId]);
      string? existingId = await FindPairChannelIdAsync(connection, transaction, projectId, firstId, secondId);
      if (existingId is not null)
      {
        id = existingId;
      }
      else
      {
        string name = await FreeChannelNameAsync(connection, transaction, projectId, PairName(first.Name, first.IsYou, second.Name, second.IsYou));
        id = await InsertChannelAsync(connection, transaction, projectId, name, [firstId, secondId]);
      }

      await transaction.CommitAsync();
    }

    return await GetChannelAsync(projectId, id);
  }

  // A channel with you is named after the other member, as personal chats always were; others are "A & B".
  private static string PairName(string first, bool firstIsYou, string second, bool secondIsYou)
  {
    if (firstIsYou)
    {
      return second;
    }

    if (secondIsYou)
    {
      return first;
    }

    return first + PairSeparator + second;
  }

  private static async Task<string> InsertChannelAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string projectId, string name, List<string> memberIds)
  {
    string id = NewId();
    await using (NpgsqlCommand insert = new("INSERT INTO room_channels(id, project_id, name, created_at) VALUES ($1, $2, $3, now())", connection, transaction))
    {
      insert.Parameters.AddWithValue(id);
      insert.Parameters.AddWithValue(projectId);
      insert.Parameters.AddWithValue(name);
      try
      {
        await insert.ExecuteNonQueryAsync();
      }
      catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
      {
        throw new InvalidOperationException($"A channel called '{name}' already exists.");
      }
    }

    foreach (string memberId in memberIds)
    {
      await using NpgsqlCommand member = new("INSERT INTO room_channel_members(channel_id, member_id) VALUES ($1, $2) ON CONFLICT DO NOTHING", connection, transaction);
      member.Parameters.AddWithValue(id);
      member.Parameters.AddWithValue(memberId);
      await member.ExecuteNonQueryAsync();
    }

    return id;
  }

  private static async Task<string> FreeChannelNameAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string projectId, string baseName)
  {
    List<string> taken = new();
    await using NpgsqlCommand command = new("SELECT name FROM room_channels WHERE project_id = $1", connection, transaction);
    command.Parameters.AddWithValue(projectId);
    await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
      taken.Add(reader.GetString(0));
    }

    return FreeName(baseName, taken);
  }

  // Adding memberships and retiring an agent serialize on the project before taking channel locks. The lock
  // excludes new channels from appearing midway through retirement, while packet foreign keys can still be read.
  private static async Task LockMembershipAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string projectId)
  {
    await using NpgsqlCommand command = new("SELECT id FROM memory_projects WHERE id = $1 FOR NO KEY UPDATE", connection, transaction);
    command.Parameters.AddWithValue(projectId);
    if (await command.ExecuteScalarAsync() is null)
    {
      throw new KeyNotFoundException("Project was not found.");
    }
  }

  private static async Task RequireActiveMembersAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string projectId, List<string> memberIds)
  {
    foreach (string memberId in memberIds)
    {
      await using NpgsqlCommand command = new("SELECT removed FROM room_members WHERE project_id = $1 AND id = $2", connection, transaction);
      command.Parameters.AddWithValue(projectId);
      command.Parameters.AddWithValue(memberId);
      object? removed = await command.ExecuteScalarAsync();
      if (removed is null)
      {
        throw new KeyNotFoundException("That member is not in this room.");
      }

      if ((bool)removed)
      {
        throw new InvalidOperationException("That member was removed from the room.");
      }
    }
  }

  private static async Task ExecuteChannelChangeAsync(NpgsqlCommand command, string name)
  {
    int changed;
    try
    {
      changed = await command.ExecuteNonQueryAsync();
    }
    catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
    {
      throw new InvalidOperationException($"A channel called '{name}' already exists.");
    }

    if (changed == 0)
    {
      throw new KeyNotFoundException("That channel is not in this room.");
    }
  }

  private static async Task<List<Channel>> ReadChannelsAsync(NpgsqlCommand command)
  {
    List<Channel> channels = new();
    await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
      channels.Add(new Channel(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetBoolean(3), reader.GetBoolean(4), new List<string>(reader.GetFieldValue<string[]>(5))));
    }

    return channels;
  }
}
