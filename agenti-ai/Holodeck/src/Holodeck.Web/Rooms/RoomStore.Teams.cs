using Npgsql;

namespace Holodeck.Web.Rooms;

public sealed partial class RoomStore
{
  public async Task<List<RoomMember>> PlaceTeamAsync(string projectId, LibraryTeam team)
  {
    List<PreparedTeamMember> prepared = PrepareTeam(team);
    List<RoomMember> members = new();
    List<string> createdDirectories = new();
    bool committed = false;
    try
    {
      await using NpgsqlConnection connection = await database.OpenConnectionAsync();
      await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
      await LockPlacementAsync(connection, transaction, projectId);
      await LockMembershipAsync(connection, transaction, projectId);
      string youId = await EnsureTeamYouAsync(connection, transaction, projectId);
      Dictionary<string, string> ids = new(StringComparer.Ordinal) { [YouName] = youId };
      Dictionary<string, string> names = new(StringComparer.Ordinal) { [YouName] = YouName };
      List<string> taken = await ListActiveNamesAsync(connection, transaction, projectId);
      foreach (PreparedTeamMember source in prepared)
      {
        string name = FreeName(source.Name, taken);
        // A suffix must still fit the same name limit as an individually placed agent.
        AgentNames.Validate(name);
        taken.Add(name);
        RoomMember member = new(NewId(), projectId, name, false, source.Agent.Folder, false);
        await InsertTeamMemberAsync(connection, transaction, member);
        members.Add(member);
        ids.Add(source.Id, member.Id);
        names.Add(source.Id, name);
        string directory = paths.AgentDirectory(projectId, member.Id);
        if (Directory.Exists(directory) || File.Exists(directory))
        {
          throw new InvalidOperationException("The new agent directory already exists.");
        }

        createdDirectories.Add(directory);
        Directory.CreateDirectory(paths.Table(projectId, member.Id));
        await File.WriteAllTextAsync(paths.SettingsFile(projectId, member.Id), source.Settings);
      }

      // Wires are two-member channels named after the pair; declared channels keep their own names.
      foreach (List<string> endpoints in team.Wires)
      {
        string name = await FreeChannelNameAsync(connection, transaction, projectId, PairName(names[endpoints[0]], endpoints[0] == YouName, names[endpoints[1]], endpoints[1] == YouName));
        await InsertChannelAsync(connection, transaction, projectId, name, [ids[endpoints[0]], ids[endpoints[1]]]);
      }

      if (team.Channels is not null)
      {
        foreach (LibraryTeamChannel channel in team.Channels)
        {
          List<string> memberIds = new();
          foreach (string memberId in channel.Members)
          {
            memberIds.Add(ids[memberId]);
          }

          string name = await FreeChannelNameAsync(connection, transaction, projectId, channel.Name);
          await InsertChannelAsync(connection, transaction, projectId, name, memberIds);
        }
      }

      await transaction.CommitAsync();
      committed = true;
      return members;
    }
    catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.ForeignKeyViolation)
    {
      throw new KeyNotFoundException("Project was not found.", exception);
    }
    finally
    {
      if (!committed)
      {
        foreach (string directory in createdDirectories)
        {
          if (Directory.Exists(directory))
          {
            Directory.Delete(directory, true);
          }
        }
      }
    }
  }

  private static List<PreparedTeamMember> PrepareTeam(LibraryTeam team)
  {
    HashSet<string> ids = new(StringComparer.Ordinal) { YouName };
    List<PreparedTeamMember> members = new();
    foreach (LibraryTeamMember source in team.Members)
    {
      if (string.IsNullOrWhiteSpace(source.Id) || string.Equals(source.Id, YouName, StringComparison.OrdinalIgnoreCase) || !ids.Add(source.Id))
      {
        throw new ArgumentException("Team member IDs must be unique and cannot be You.");
      }

      string name = AgentNames.Validate(source.Name);
      string settings = ReadTeamSettings(source.Agent);
      members.Add(new PreparedTeamMember(source.Id, name, source.Agent, settings));
    }

    HashSet<string> wires = new(StringComparer.Ordinal);
    foreach (List<string> endpoints in team.Wires)
    {
      if (endpoints is null)
      {
        throw new ArgumentException("Each team wire needs two member IDs.");
      }

      if (endpoints.Count != 2)
      {
        throw new ArgumentException("Each team wire needs two member IDs.");
      }

      string first = endpoints[0];
      string second = endpoints[1];
      if (!ids.Contains(first) || !ids.Contains(second) || first == second)
      {
        throw new ArgumentException("Team wires must connect two different team members or You.");
      }

      bool inOrder = string.CompareOrdinal(first, second) < 0;
      string key = inOrder ? first + "\0" + second : second + "\0" + first;
      if (!wires.Add(key))
      {
        throw new ArgumentException("A team cannot contain the same wire twice.");
      }
    }

    return members;
  }

  private static string ReadTeamSettings(LibraryAgent agent)
  {
    string? source = agent.SettingsPath;
    if (source is null)
    {
      return EmptySettings;
    }

    if (!File.Exists(source))
    {
      throw new InvalidOperationException($"Settings for {agent.Name} were not found.");
    }

    FileInfo info = new(source);
    if (info.Length > RoomLimits.MaxSettingsBytes)
    {
      throw new InvalidOperationException($"Settings for {agent.Name} exceed the settings size limit.");
    }

    return File.ReadAllText(source);
  }

  private static async Task LockPlacementAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string projectId)
  {
    await using NpgsqlCommand command = new("SELECT pg_advisory_xact_lock(hashtext($1))", connection, transaction);
    command.Parameters.AddWithValue(projectId);
    await command.ExecuteNonQueryAsync();
  }

  private static async Task<string> EnsureTeamYouAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string projectId)
  {
    const string Sql = """
      INSERT INTO room_members(id, project_id, name, is_you, created_at)
      VALUES ($1, $2, $3, true, now())
      ON CONFLICT (project_id) WHERE is_you DO UPDATE SET name = room_members.name
      RETURNING id
      """;
    await using NpgsqlCommand command = new(Sql, connection, transaction);
    command.Parameters.AddWithValue(NewId());
    command.Parameters.AddWithValue(projectId);
    command.Parameters.AddWithValue(YouName);
    return (string)(await command.ExecuteScalarAsync())!;
  }

  private static async Task InsertTeamMemberAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, RoomMember member)
  {
    await using NpgsqlCommand command = new("INSERT INTO room_members(id, project_id, name, is_you, library, created_at) VALUES ($1, $2, $3, false, $4, now())", connection, transaction);
    command.Parameters.AddWithValue(member.Id);
    command.Parameters.AddWithValue(member.ProjectId);
    command.Parameters.AddWithValue(member.Name);
    command.Parameters.AddWithValue(member.Library!);
    await command.ExecuteNonQueryAsync();
  }
}
