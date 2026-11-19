using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Holodeck.Web.Projects;
using Holodeck.Web.Rooms;
using Holodeck.Web.Settings;
using Npgsql;
using NpgsqlTypes;

namespace Holodeck.Web.Management;

// Durable private requests. Holodeck validates data and fences attempts; only the target agent performs the work.
public sealed partial class ManagementStore(NpgsqlDataSource database, AgentLibrary library, ProjectLibraryStore projectLibraries, AgentSettingsStore settings)
{
  private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
  private const string Columns = "id, action_id, request_id, definition_revision, settings_revision, status, input::text, result::text, progress, message, error, created_at, updated_at, action::text, settings_content, session_hash, attempt_token, lease_expires_at";

  public async Task InitializeAsync()
  {
    const string Sql = """
      CREATE TABLE IF NOT EXISTS management_jobs (
        id text PRIMARY KEY,
        project_id text NOT NULL REFERENCES memory_projects(id) ON DELETE CASCADE,
        member_id text NOT NULL REFERENCES room_members(id) ON DELETE CASCADE,
        action_id text NOT NULL,
        request_id text NOT NULL,
        definition_revision text NOT NULL,
        settings_revision text NOT NULL,
        settings_content text NOT NULL,
        action jsonb NOT NULL,
        status text NOT NULL CHECK(status IN ('queued','running','succeeded','failed','canceled')),
        input jsonb NOT NULL,
        result jsonb,
        progress integer CHECK(progress BETWEEN 0 AND 100),
        message text,
        error text,
        session_hash text,
        attempt_token text,
        lease_expires_at timestamptz,
        created_at timestamptz NOT NULL DEFAULT now(),
        updated_at timestamptz NOT NULL DEFAULT now(),
        UNIQUE(member_id, request_id));
      CREATE INDEX IF NOT EXISTS management_jobs_member ON management_jobs(member_id, created_at DESC);
      CREATE TABLE IF NOT EXISTS management_claims (
        member_id text NOT NULL REFERENCES room_members(id) ON DELETE CASCADE,
        session_hash text NOT NULL,
        claim_id text NOT NULL,
        job_id text NOT NULL REFERENCES management_jobs(id) ON DELETE CASCADE,
        attempt_token text NOT NULL,
        PRIMARY KEY(member_id, session_hash, claim_id));
      """;
    await using NpgsqlCommand command = database.CreateCommand(Sql);
    await command.ExecuteNonQueryAsync();
  }

  public async Task<ManagementDefinition> ActionsAsync(RoomMember member)
  {
    if (member.IsYou || member.Library is null)
    {
      throw new InvalidOperationException("Select an agent with an available library definition.");
    }

    string? error = await projectLibraries.ErrorForAsync(member.ProjectId, member.Library);
    if (error is not null)
    {
      throw new InvalidOperationException(error);
    }

    return library.Get(member.Library).Actions ?? new ManagementDefinition("", new List<ManagementAction>());
  }

  public async Task<List<ManagementJob>> ListAsync(RoomMember member)
  {
    await using NpgsqlCommand command = database.CreateCommand($"SELECT {Columns} FROM management_jobs WHERE project_id = $1 AND member_id = $2 ORDER BY created_at DESC, id DESC LIMIT $3");
    command.Parameters.AddWithValue(member.ProjectId);
    command.Parameters.AddWithValue(member.Id);
    command.Parameters.AddWithValue(ManagementLimits.RecentJobs);
    List<ManagementJob> jobs = new();
    await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
      jobs.Add(Read(reader).Job);
    }

    return jobs;
  }

  public async Task<ManagementEnqueued> EnqueueAsync(RoomMember member, ManagementEnqueue input)
  {
    RequireIdentifier(input.RequestId, "Request Id");
    RequireIdentifier(input.ActionId, "Action Id");
    RequireIdentifier(input.DefinitionRevision, "Definition revision");
    string payload = Payload(input.Input);
    // Existing requests survive changes to the definition and stopping the agent. They cannot enqueue new work.
    ManagementJob? existing = await ExistingAsync(member, input, payload);
    if (existing is not null)
    {
      return new ManagementEnqueued(existing, true);
    }

    ManagementDefinition definition = await ActionsAsync(member);
    if (input.DefinitionRevision != definition.DefinitionRevision)
    {
      throw new InvalidOperationException("The actions definition changed. Review the action before running it.");
    }

    ManagementAction action = FindAction(definition, input.ActionId!);
    Validate(action.InputSchema, input.Input);
    SettingsView snapshot = await settings.ReadAsync(member);
    await using NpgsqlConnection connection = await database.OpenConnectionAsync();
    await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
    string? session = await LockMemberAsync(connection, transaction, member, null);
    existing = await ExistingAsync(connection, transaction, member, input, payload);
    if (existing is not null)
    {
      return new ManagementEnqueued(existing, true);
    }

    if (session is null || member.IsYou || member.IsRemoved)
    {
      throw new InvalidOperationException("Start this resolved agent before running an action.");
    }

    string id = NewToken();
    await using NpgsqlCommand insert = new("INSERT INTO management_jobs(id,project_id,member_id,action_id,request_id,definition_revision,settings_revision,settings_content,action,status,input) VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,'queued',$10)", connection, transaction);
    insert.Parameters.AddWithValue(id);
    insert.Parameters.AddWithValue(member.ProjectId);
    insert.Parameters.AddWithValue(member.Id);
    insert.Parameters.AddWithValue(action.Id);
    insert.Parameters.AddWithValue(input.RequestId!);
    insert.Parameters.AddWithValue(definition.DefinitionRevision);
    insert.Parameters.AddWithValue(snapshot.Revision);
    insert.Parameters.AddWithValue(snapshot.Content);
    AddJson(insert, JsonSerializer.Serialize(action, Json));
    AddJson(insert, payload);
    await insert.ExecuteNonQueryAsync();
    ManagementJob result = (await ReadAsync(connection, transaction, member, id)).Job;
    await transaction.CommitAsync();
    return new ManagementEnqueued(result, false);
  }

  private async Task<ManagementJob?> ExistingAsync(RoomMember member, ManagementEnqueue input, string payload)
  {
    await using NpgsqlConnection connection = await database.OpenConnectionAsync();
    return await ExistingAsync(connection, null, member, input, payload);
  }

  private static async Task<ManagementJob?> ExistingAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, RoomMember member, ManagementEnqueue input, string payload)
  {
    await using NpgsqlCommand command = new($"SELECT {Columns}, action_id = $4 AND definition_revision = $5 AND input = $6 FROM management_jobs WHERE project_id = $1 AND member_id = $2 AND request_id = $3", connection, transaction);
    command.Parameters.AddWithValue(member.ProjectId);
    command.Parameters.AddWithValue(member.Id);
    command.Parameters.AddWithValue(input.RequestId!);
    command.Parameters.AddWithValue(input.ActionId!);
    command.Parameters.AddWithValue(input.DefinitionRevision!);
    AddJson(command, payload);
    await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
    {
      return null;
    }

    if (!reader.GetBoolean(18))
    {
      throw new InvalidOperationException("That request Id was already used for different action input.");
    }

    return Read(reader).Job;
  }

  private static ManagementAction FindAction(ManagementDefinition definition, string id)
  {
    foreach (ManagementAction action in definition.Actions)
    {
      if (action.Id == id)
      {
        return action;
      }
    }

    throw new ArgumentException("That action is not declared by this agent.");
  }

  private static void Validate(SettingsSchemaNode schema, JsonElement value)
  {
    List<SettingsFieldError> errors = SettingsValidator.Validate(schema, SettingsDocument.Parse(Payload(value)));
    if (errors.Count > 0)
    {
      throw new ManagementValidationException(errors);
    }
  }

  private static string Payload(JsonElement value)
  {
    if (value.ValueKind == JsonValueKind.Undefined)
    {
      throw new ArgumentException("Provide an action payload.");
    }

    string text = value.GetRawText();
    if (Encoding.UTF8.GetByteCount(text) > ManagementLimits.MaxPayloadBytes)
    {
      throw new ArgumentException("The action payload is too large.");
    }

    return text;
  }

  private static void RequireIdentifier(string? value, string label)
  {
    if (value is null || value.Length > ManagementLimits.MaxIdentifierLength)
    {
      throw new ArgumentException($"{label} requires a bounded identifier.");
    }

    LibraryFiles.RequireId(value, label);
  }

  private static string NewToken() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

  private static void AddJson(NpgsqlCommand command, string text)
  {
    command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Jsonb, Value = text });
  }

  private static ManagementJobState Read(NpgsqlDataReader reader)
  {
    JsonElement? result = reader.IsDBNull(7) ? null : JsonSerializer.Deserialize<JsonElement>(reader.GetString(7), Json);
    ManagementJob job = new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), JsonSerializer.Deserialize<JsonElement>(reader.GetString(6), Json), result, reader.IsDBNull(8) ? null : reader.GetInt32(8), Optional(reader, 9), Optional(reader, 10), reader.GetFieldValue<DateTimeOffset>(11), reader.GetFieldValue<DateTimeOffset>(12), JsonSerializer.Deserialize<ManagementAction>(reader.GetString(13), Json)!);
    return new ManagementJobState(job, reader.GetString(14), Optional(reader, 15), Optional(reader, 16), reader.IsDBNull(17) ? null : reader.GetFieldValue<DateTimeOffset>(17));
  }

  private static string? Optional(NpgsqlDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetString(index);

  private static async Task<ManagementJobState> ReadAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, RoomMember member, string id)
  {
    await using NpgsqlCommand command = new($"SELECT {Columns} FROM management_jobs WHERE project_id = $1 AND member_id = $2 AND id = $3 FOR UPDATE", connection, transaction);
    command.Parameters.AddWithValue(member.ProjectId);
    command.Parameters.AddWithValue(member.Id);
    command.Parameters.AddWithValue(id);
    await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
    {
      throw new KeyNotFoundException("That management job is not assigned to this agent.");
    }

    return Read(reader);
  }

  // Every agent operation locks and verifies the current database session before touching a job. A revoke/stop
  // therefore either wins before the report or follows its commit; a stale in-memory caller cannot renew a lease.
  private static async Task<string?> LockMemberAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, RoomMember member, string? expectedSession)
  {
    await using NpgsqlCommand command = new("SELECT session_hash,removed,library FROM room_members WHERE project_id = $1 AND id = $2 FOR UPDATE", connection, transaction);
    command.Parameters.AddWithValue(member.ProjectId);
    command.Parameters.AddWithValue(member.Id);
    await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
    {
      throw new KeyNotFoundException("That member is not in this room.");
    }

    string? session = Optional(reader, 0);
    bool unavailable = reader.GetBoolean(1) || Optional(reader, 2) != member.Library;
    if (expectedSession is not null && (session != expectedSession || unavailable))
    {
      throw new UnauthorizedAccessException("That agent session is no longer active.");
    }

    if (unavailable)
    {
      return null;
    }

    return session;
  }
}
