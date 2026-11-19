using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace Holodeck.Web.Computers;

// Each agent instance's grants, as one JSON document. No row means nothing granted.
public sealed class GrantStore(NpgsqlDataSource database)
{
  private static readonly AgentGrants Nothing = new(new List<PortGrant>(), new List<FolderGrant>());

  public async Task InitializeAsync(CancellationToken token)
  {
    const string Schema = """
      CREATE TABLE IF NOT EXISTS room_member_grants (
        member_id text PRIMARY KEY REFERENCES room_members(id) ON DELETE CASCADE,
        grants jsonb NOT NULL);
      """;
    await using NpgsqlCommand command = database.CreateCommand(Schema);
    await command.ExecuteNonQueryAsync(token);
  }

  public async Task<AgentGrants> ReadAsync(string memberId)
  {
    await using NpgsqlCommand command = database.CreateCommand("SELECT grants::text FROM room_member_grants WHERE member_id = $1");
    command.Parameters.AddWithValue(memberId);
    object? value = await command.ExecuteScalarAsync();
    if (value is not string json)
    {
      return Nothing;
    }

    return JsonSerializer.Deserialize<AgentGrants>(json) ?? Nothing;
  }

  public async Task WriteAsync(string memberId, AgentGrants grants)
  {
    await using NpgsqlCommand command = database.CreateCommand("INSERT INTO room_member_grants(member_id, grants) VALUES ($1, $2) ON CONFLICT (member_id) DO UPDATE SET grants = excluded.grants");
    command.Parameters.AddWithValue(memberId);
    command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Jsonb, Value = JsonSerializer.Serialize(grants) });
    await command.ExecuteNonQueryAsync();
  }

  // Host ports granted to any agent, so a new grant never reuses one.
  public async Task<List<int>> ListHostPortsAsync()
  {
    List<int> ports = new();
    await using NpgsqlCommand command = database.CreateCommand("SELECT (port->>'HostPort')::int FROM room_member_grants, jsonb_array_elements(grants->'Ports') port");
    await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
      ports.Add(reader.GetInt32(0));
    }

    return ports;
  }
}
