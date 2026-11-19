using Npgsql;

namespace Holodeck.Web.Rooms;

// Who is in each project's room, and which channels they share. Removing a member keeps its history readable.
public sealed partial class RoomStore(NpgsqlDataSource database, RoomPaths paths)
{
  private const string YouName = "You";
  private const string MemberColumns = "id, project_id, name, is_you, library, removed";
  private const string EmptySettings = "{}";

  public async Task InitializeAsync(CancellationToken token)
  {
    const string Schema = """
      CREATE TABLE IF NOT EXISTS room_members (
        id text PRIMARY KEY,
        project_id text NOT NULL REFERENCES memory_projects(id) ON DELETE CASCADE,
        name text NOT NULL,
        is_you bool NOT NULL,
        library text,
        removed bool NOT NULL DEFAULT false,
        should_run bool NOT NULL DEFAULT false,
        session_hash text UNIQUE,
        next_sequence bigint NOT NULL DEFAULT 0,
        done_sequence bigint NOT NULL DEFAULT 0,
        in_flight_sequence bigint,
        in_flight_session text,
        created_at timestamptz NOT NULL);
      CREATE UNIQUE INDEX IF NOT EXISTS room_members_name ON room_members(project_id, lower(name)) WHERE NOT removed;
      CREATE UNIQUE INDEX IF NOT EXISTS room_members_you ON room_members(project_id) WHERE is_you;
      CREATE TABLE IF NOT EXISTS room_wires (
        id text PRIMARY KEY,
        project_id text NOT NULL REFERENCES memory_projects(id) ON DELETE CASCADE,
        first_id text NOT NULL REFERENCES room_members(id) ON DELETE CASCADE,
        second_id text NOT NULL REFERENCES room_members(id) ON DELETE CASCADE,
        connected bool NOT NULL,
        created_at timestamptz NOT NULL,
        UNIQUE(first_id, second_id),
        CHECK(first_id < second_id));
      CREATE TABLE IF NOT EXISTS room_packets (
        id bigserial PRIMARY KEY,
        project_id text NOT NULL REFERENCES memory_projects(id) ON DELETE CASCADE,
        wire_id text NOT NULL REFERENCES room_wires(id) ON DELETE CASCADE,
        sender_id text NOT NULL,
        recipient_id text NOT NULL,
        recipient_sequence bigint NOT NULL,
        note text NOT NULL,
        sent_at timestamptz NOT NULL,
        UNIQUE(recipient_id, recipient_sequence));
      CREATE INDEX IF NOT EXISTS room_packets_wire ON room_packets(wire_id, id);
      CREATE TABLE IF NOT EXISTS room_packet_files (
        packet_id bigint NOT NULL REFERENCES room_packets(id) ON DELETE CASCADE,
        ordinal int NOT NULL,
        name text NOT NULL,
        media_type text NOT NULL,
        size bigint NOT NULL,
        delivered_path text,
        PRIMARY KEY(packet_id, ordinal),
        UNIQUE(packet_id, name));
      CREATE TABLE IF NOT EXISTS room_channels (
        id text PRIMARY KEY,
        project_id text NOT NULL REFERENCES memory_projects(id) ON DELETE CASCADE,
        name text NOT NULL,
        legacy bool NOT NULL DEFAULT false,
        paused bool NOT NULL DEFAULT false,
        created_at timestamptz NOT NULL);
      CREATE UNIQUE INDEX IF NOT EXISTS room_channels_name ON room_channels(project_id, lower(name));
      CREATE TABLE IF NOT EXISTS room_channel_members (
        channel_id text NOT NULL REFERENCES room_channels(id) ON DELETE CASCADE,
        member_id text NOT NULL REFERENCES room_members(id) ON DELETE CASCADE,
        PRIMARY KEY(channel_id, member_id));
      ALTER TABLE room_packets ADD COLUMN IF NOT EXISTS channel_id text REFERENCES room_channels(id) ON DELETE CASCADE;
      ALTER TABLE room_packets ALTER COLUMN wire_id DROP NOT NULL;
      ALTER TABLE room_packets ALTER COLUMN recipient_id DROP NOT NULL;
      ALTER TABLE room_packets ALTER COLUMN recipient_sequence DROP NOT NULL;
      CREATE INDEX IF NOT EXISTS room_packets_channel ON room_packets(channel_id, id);
      CREATE TABLE IF NOT EXISTS room_deliveries (
        packet_id bigint NOT NULL REFERENCES room_packets(id) ON DELETE CASCADE,
        recipient_id text NOT NULL REFERENCES room_members(id) ON DELETE CASCADE,
        recipient_sequence bigint NOT NULL,
        PRIMARY KEY(packet_id, recipient_id),
        UNIQUE(recipient_id, recipient_sequence));
      CREATE TABLE IF NOT EXISTS room_delivery_files (
        packet_id bigint NOT NULL,
        recipient_id text NOT NULL,
        ordinal int NOT NULL,
        delivered_path text NOT NULL,
        PRIMARY KEY(packet_id, recipient_id, ordinal),
        FOREIGN KEY(packet_id, recipient_id) REFERENCES room_deliveries(packet_id, recipient_id) ON DELETE CASCADE);
      CREATE TABLE IF NOT EXISTS room_migrations (name text PRIMARY KEY);
      ALTER TABLE room_members ADD COLUMN IF NOT EXISTS computer_started_at text;
      ALTER TABLE room_packets ADD COLUMN IF NOT EXISTS send_key text;
      ALTER TABLE room_packets ADD COLUMN IF NOT EXISTS send_fingerprint text;
      CREATE UNIQUE INDEX IF NOT EXISTS room_packets_send_key ON room_packets(project_id, sender_id, send_key) WHERE send_key IS NOT NULL;
      """;
    await using (NpgsqlCommand command = database.CreateCommand(Schema))
    {
      await command.ExecuteNonQueryAsync(token);
    }

    await MigrateWiresAsync(token);
  }

  public async Task<RoomMember> EnsureYouAsync(string projectId)
  {
    await using (NpgsqlCommand insert = database.CreateCommand("INSERT INTO room_members(id, project_id, name, is_you, created_at) VALUES ($1, $2, $3, true, now()) ON CONFLICT (project_id) WHERE is_you DO NOTHING"))
    {
      insert.Parameters.AddWithValue(NewId());
      insert.Parameters.AddWithValue(projectId);
      insert.Parameters.AddWithValue(YouName);
      try
      {
        await insert.ExecuteNonQueryAsync();
      }
      catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.ForeignKeyViolation)
      {
        throw new KeyNotFoundException("Project was not found.");
      }
    }

    await using NpgsqlCommand select = database.CreateCommand($"SELECT {MemberColumns} FROM room_members WHERE project_id = $1 AND is_you");
    select.Parameters.AddWithValue(projectId);
    return (await ReadMembersAsync(select, CancellationToken.None))[0];
  }

  public async Task<List<RoomMember>> ListMembersAsync(string projectId)
  {
    await using NpgsqlCommand command = database.CreateCommand($"SELECT {MemberColumns} FROM room_members WHERE project_id = $1 ORDER BY is_you DESC, created_at, id");
    command.Parameters.AddWithValue(projectId);
    return await ReadMembersAsync(command, CancellationToken.None);
  }

  public async Task<List<RoomMember>> ListAgentsAsync()
  {
    await using NpgsqlCommand command = database.CreateCommand($"SELECT {MemberColumns} FROM room_members WHERE NOT is_you AND NOT removed ORDER BY created_at, id");
    return await ReadMembersAsync(command, CancellationToken.None);
  }

  public async Task<RoomMember> GetMemberAsync(string projectId, string memberId)
  {
    await using NpgsqlCommand command = database.CreateCommand($"SELECT {MemberColumns} FROM room_members WHERE project_id = $1 AND id = $2");
    command.Parameters.AddWithValue(projectId);
    command.Parameters.AddWithValue(memberId);
    List<RoomMember> members = await ReadMembersAsync(command, CancellationToken.None);
    if (members.Count == 0)
    {
      throw new KeyNotFoundException("That member is not in this room.");
    }

    return members[0];
  }

  public async Task<RoomMember> PlaceAsync(string projectId, LibraryAgent agent)
  {
    string baseName = AgentNames.Validate(agent.Name);
    string settings = ReadTeamSettings(agent);
    RoomMember member;
    await using (NpgsqlConnection connection = await database.OpenConnectionAsync())
    {
      await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
      // One placement at a time per project, so two placements never pick the same free name.
      await using (NpgsqlCommand lockProject = new("SELECT pg_advisory_xact_lock(hashtext($1))", connection, transaction))
      {
        lockProject.Parameters.AddWithValue(projectId);
        await lockProject.ExecuteNonQueryAsync();
      }

      List<string> taken = await ListActiveNamesAsync(connection, transaction, projectId);
      member = new RoomMember(NewId(), projectId, FreeName(baseName, taken), false, agent.Folder, false);
      await using NpgsqlCommand insert = new("INSERT INTO room_members(id, project_id, name, is_you, library, created_at) VALUES ($1, $2, $3, false, $4, now())", connection, transaction);
      insert.Parameters.AddWithValue(member.Id);
      insert.Parameters.AddWithValue(projectId);
      insert.Parameters.AddWithValue(member.Name);
      insert.Parameters.AddWithValue(agent.Folder);
      try
      {
        await insert.ExecuteNonQueryAsync();
      }
      catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.ForeignKeyViolation)
      {
        throw new KeyNotFoundException("Project was not found.");
      }

      await transaction.CommitAsync();
    }

    PrepareAgentDirectory(member, settings);
    return member;
  }

  public async Task<RoomMember> RenameAsync(string projectId, string memberId, string name)
  {
    RoomMember member = await GetMemberAsync(projectId, memberId);
    RequireAgent(member);
    string validName = AgentNames.Validate(name);
    await using NpgsqlCommand command = database.CreateCommand("UPDATE room_members SET name = $2 WHERE id = $1");
    command.Parameters.AddWithValue(memberId);
    command.Parameters.AddWithValue(validName);
    try
    {
      await command.ExecuteNonQueryAsync();
    }
    catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
    {
      throw new InvalidOperationException("Another member of this room already has that name.");
    }

    return member with { Name = validName };
  }

  public async Task RemoveAsync(string projectId, string memberId)
  {
    RoomMember member = await GetMemberAsync(projectId, memberId);
    if (member.IsYou)
    {
      throw new InvalidOperationException("You cannot be removed from your own room.");
    }

    await using NpgsqlConnection connection = await database.OpenConnectionAsync();
    await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
    await LockMembershipAsync(connection, transaction, projectId);
    // Lock its channels in id order, then leave them, then update the member row: sends lock one channel before
    // member rows, so the two never deadlock, and a send sees the membership before or after this removal.
    await using (NpgsqlCommand lockChannels = new("SELECT c.id FROM room_channels c JOIN room_channel_members m ON m.channel_id = c.id WHERE m.member_id = $1 ORDER BY c.id FOR UPDATE OF c", connection, transaction))
    {
      lockChannels.Parameters.AddWithValue(memberId);
      await lockChannels.ExecuteNonQueryAsync();
    }

    await using (NpgsqlCommand leave = new("DELETE FROM room_channel_members WHERE member_id = $1", connection, transaction))
    {
      leave.Parameters.AddWithValue(memberId);
      await leave.ExecuteNonQueryAsync();
    }

    await using (NpgsqlCommand remove = new("UPDATE room_members SET removed = true, should_run = false, session_hash = NULL WHERE id = $1", connection, transaction))
    {
      remove.Parameters.AddWithValue(memberId);
      await remove.ExecuteNonQueryAsync();
    }

    await transaction.CommitAsync();
  }

  public async Task SetShouldRunAsync(string memberId, bool shouldRun)
  {
    await using NpgsqlCommand command = database.CreateCommand("UPDATE room_members SET should_run = $2 WHERE id = $1");
    command.Parameters.AddWithValue(memberId);
    command.Parameters.AddWithValue(shouldRun);
    await command.ExecuteNonQueryAsync();
  }

  public async Task<List<RoomMember>> ListShouldRunAsync()
  {
    await using NpgsqlCommand command = database.CreateCommand($"SELECT {MemberColumns} FROM room_members WHERE should_run AND NOT removed ORDER BY created_at, id");
    return await ReadMembersAsync(command, CancellationToken.None);
  }

  public async Task BeginSessionAsync(string memberId, string keyHash)
  {
    await using NpgsqlCommand command = database.CreateCommand("UPDATE room_members SET session_hash = $2 WHERE id = $1");
    command.Parameters.AddWithValue(memberId);
    command.Parameters.AddWithValue(keyHash);
    await command.ExecuteNonQueryAsync();
  }

  // A retained container keeps its key, but a new process must replay its unfinished delivery.
  // First observation adopts an existing process without disturbing that process's delivery position.
  public async Task ObserveComputerStartAsync(string memberId, string startedAt)
  {
    if (string.IsNullOrEmpty(startedAt))
    {
      return;
    }

    const string Update = """
      UPDATE room_members
      SET in_flight_session = CASE
        WHEN computer_started_at IS NOT NULL AND computer_started_at <> $2 THEN NULL
        ELSE in_flight_session
      END,
      computer_started_at = $2
      WHERE id = $1
      """;
    await using NpgsqlCommand command = database.CreateCommand(Update);
    command.Parameters.AddWithValue(memberId);
    command.Parameters.AddWithValue(startedAt);
    await command.ExecuteNonQueryAsync();
  }

  public async Task EndSessionAsync(string memberId)
  {
    await using NpgsqlCommand command = database.CreateCommand("UPDATE room_members SET session_hash = NULL, in_flight_session = NULL WHERE id = $1");
    command.Parameters.AddWithValue(memberId);
    await command.ExecuteNonQueryAsync();
  }

  public async Task<RoomMember?> FindBySessionAsync(string keyHash)
  {
    await using NpgsqlCommand command = database.CreateCommand($"SELECT {MemberColumns} FROM room_members WHERE session_hash = $1 AND NOT removed");
    command.Parameters.AddWithValue(keyHash);
    List<RoomMember> members = await ReadMembersAsync(command, CancellationToken.None);
    if (members.Count == 0)
    {
      return null;
    }

    return members[0];
  }

  private void PrepareAgentDirectory(RoomMember member, string settings)
  {
    Directory.CreateDirectory(paths.Table(member.ProjectId, member.Id));
    File.WriteAllText(paths.SettingsFile(member.ProjectId, member.Id), settings);
  }

  private static void RequireAgent(RoomMember member)
  {
    if (member.IsYou)
    {
      throw new InvalidOperationException("You keep your own name.");
    }

    if (member.IsRemoved)
    {
      throw new InvalidOperationException("That member was removed from the room.");
    }
  }

  private static async Task<List<string>> ListActiveNamesAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string projectId)
  {
    List<string> names = new();
    await using NpgsqlCommand command = new("SELECT name FROM room_members WHERE project_id = $1 AND NOT removed", connection, transaction);
    command.Parameters.AddWithValue(projectId);
    await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
      names.Add(reader.GetString(0));
    }

    return names;
  }

  private static string FreeName(string baseName, List<string> taken)
  {
    string candidate = baseName;
    int number = 1;
    while (IsTaken(candidate, taken))
    {
      number++;
      candidate = $"{baseName} {number}";
    }

    return candidate;
  }

  private static bool IsTaken(string name, List<string> taken)
  {
    foreach (string existing in taken)
    {
      if (string.Equals(existing, name, StringComparison.OrdinalIgnoreCase))
      {
        return true;
      }
    }

    return false;
  }

  private static async Task<List<RoomMember>> ReadMembersAsync(NpgsqlCommand command, CancellationToken token)
  {
    List<RoomMember> members = new();
    await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
    while (await reader.ReadAsync(token))
    {
      members.Add(new RoomMember(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetBoolean(3), ReadOptional(reader, 4), reader.GetBoolean(5)));
    }

    return members;
  }

  private static string? ReadOptional(NpgsqlDataReader reader, int ordinal)
  {
    if (reader.IsDBNull(ordinal))
    {
      return null;
    }

    return reader.GetString(ordinal);
  }

  private static string NewId()
  {
    return Guid.NewGuid().ToString("N");
  }
}
