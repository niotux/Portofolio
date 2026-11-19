using System.Security.Cryptography;
using System.Text.Json;
using Holodeck.Web.Configuration;
using Holodeck.Web.Rooms;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;
using Pgvector;
using Pgvector.Npgsql;

namespace Holodeck.Web.AgentData;

// Each agent instance gets its own schema and login role in Holodeck's PostgreSQL (pgvector included), created on first
// use. Its SQL runs logged in as that role, so it sees only its own schema: not Holodeck's tables, not another agent's.
// A schema costs next to nothing, unlike a database per instance. Holodeck never looks at what an agent keeps there.
public sealed partial class AgentDatabases(NpgsqlDataSource database, IOptions<DatabaseOptions> options) : IAsyncDisposable
{
  public const int MaxStatements = 64;
  public const int MaxRows = 5000;
  public const int CommandSeconds = 30;
  private const int ConnectionLimit = 4;
  private const int PasswordBytes = 24;
  private const int MemberIdLength = 32;
  private const string RolePrefix = "agent_";
  private const string DuplicateObject = "42710";
  private readonly SemaphoreSlim provisioning = new(1, 1);
  private readonly Lock gate = new();
  private readonly Dictionary<string, NpgsqlDataSource> sources = new();

  public async Task InitializeAsync()
  {
    try
    {
      await using NpgsqlCommand extension = database.CreateCommand("CREATE EXTENSION IF NOT EXISTS vector SCHEMA public");
      await extension.ExecuteNonQueryAsync();
    }
    catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation || exception.SqlState == DuplicateObject)
    {
      // Another instance created it at the same moment.
    }

    const string Sql = """
      CREATE TABLE IF NOT EXISTS agent_databases (
        member_id text PRIMARY KEY REFERENCES room_members(id) ON DELETE CASCADE,
        role_name text NOT NULL UNIQUE,
        password text NOT NULL,
        created_at timestamptz NOT NULL DEFAULT now());
      CREATE TABLE IF NOT EXISTS library_databases (
        library_key text PRIMARY KEY,
        role_name text NOT NULL UNIQUE,
        password text NOT NULL,
        created_at timestamptz NOT NULL DEFAULT now())
      """;
    await using NpgsqlCommand command = database.CreateCommand(Sql);
    await command.ExecuteNonQueryAsync();
  }

  public async Task<SqlResponse> RunAsync(RoomMember member, SqlRequest request, bool readOnly, CancellationToken token)
  {
    Validate(request);
    NpgsqlDataSource source = await SourceAsync(member.Id);
    return await RunOnSourceAsync(source, request, readOnly, token);
  }

  private static async Task<SqlResponse> RunOnSourceAsync(NpgsqlDataSource source, SqlRequest request, bool readOnly, CancellationToken token)
  {
    await using NpgsqlConnection connection = await source.OpenConnectionAsync(token);
    await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token);
    if (readOnly)
    {
      await using NpgsqlCommand guard = new("SET TRANSACTION READ ONLY", connection, transaction);
      await guard.ExecuteNonQueryAsync(token);
    }

    List<SqlResult> results = new();
    for (int index = 0; index < request.Statements.Count; index++)
    {
      try
      {
        results.Add(await ExecuteAsync(connection, transaction, request.Statements[index], token));
      }
      catch (PostgresException exception)
      {
        throw new ArgumentException($"Statement {index + 1}: {exception.MessageText}");
      }
      catch (NpgsqlException exception) when (exception.InnerException is TimeoutException)
      {
        throw new ArgumentException($"Statement {index + 1} took longer than {CommandSeconds} seconds.");
      }
    }

    await transaction.CommitAsync(token);
    return new SqlResponse(results);
  }

  // Removes the agent's schema and role with everything in them. The next use starts an empty one.
  public async Task DropAsync(string memberId)
  {
    NpgsqlDataSource? source;
    lock (gate)
    {
      sources.Remove(memberId, out source);
    }

    if (source is not null)
    {
      await source.DisposeAsync();
    }

    await provisioning.WaitAsync();
    try
    {
      AgentDatabaseLogin? login = await FindLoginAsync(memberId);
      if (login is null)
      {
        return;
      }

      string role = login.Value.Role;
      await using NpgsqlConnection connection = await database.OpenConnectionAsync();
      await using (NpgsqlCommand terminate = new("SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE usename = $1", connection))
      {
        terminate.Parameters.AddWithValue(role);
        await terminate.ExecuteNonQueryAsync();
      }

      await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
      await using (NpgsqlCommand drop = new($"DROP SCHEMA IF EXISTS {role} CASCADE; DROP ROLE IF EXISTS {role}", connection, transaction))
      {
        await drop.ExecuteNonQueryAsync();
      }

      await using (NpgsqlCommand forget = new("DELETE FROM agent_databases WHERE member_id = $1", connection, transaction))
      {
        forget.Parameters.AddWithValue(memberId);
        await forget.ExecuteNonQueryAsync();
      }

      await transaction.CommitAsync();
    }
    finally
    {
      provisioning.Release();
    }
  }

  public async Task DropProjectAsync(string projectId)
  {
    List<string> members = new();
    await using (NpgsqlCommand command = database.CreateCommand("SELECT d.member_id FROM agent_databases d JOIN room_members m ON m.id = d.member_id WHERE m.project_id = $1"))
    {
      command.Parameters.AddWithValue(projectId);
      await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
      while (await reader.ReadAsync())
      {
        members.Add(reader.GetString(0));
      }
    }

    foreach (string member in members)
    {
      await DropAsync(member);
    }
  }

  public async ValueTask DisposeAsync()
  {
    List<NpgsqlDataSource> open = new();
    lock (gate)
    {
      foreach (NpgsqlDataSource source in sources.Values)
      {
        open.Add(source);
      }

      sources.Clear();
    }

    foreach (NpgsqlDataSource source in open)
    {
      await source.DisposeAsync();
    }

    provisioning.Dispose();
  }

  private static void Validate(SqlRequest request)
  {
    bool hasStatements = request.Statements is not null && request.Statements.Count > 0;
    if (!hasStatements || request.Statements!.Count > MaxStatements)
    {
      throw new ArgumentException($"Send between 1 and {MaxStatements} statements.");
    }

    foreach (SqlStatement statement in request.Statements)
    {
      if (string.IsNullOrWhiteSpace(statement.Sql))
      {
        throw new ArgumentException("Every statement needs SQL.");
      }
    }
  }

  private static async Task<SqlResult> ExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, SqlStatement statement, CancellationToken token)
  {
    await using NpgsqlCommand command = new(statement.Sql, connection, transaction) { CommandTimeout = CommandSeconds };
    if (statement.Parameters is not null)
    {
      foreach (JsonElement value in statement.Parameters)
      {
        command.Parameters.Add(Parameter(value));
      }
    }

    List<string> columns = new();
    List<List<object?>> rows = new();
    int affected;
    await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(token))
    {
      for (int column = 0; column < reader.FieldCount; column++)
      {
        columns.Add(reader.GetName(column));
      }

      while (await reader.ReadAsync(token))
      {
        if (rows.Count == MaxRows)
        {
          throw new ArgumentException($"A statement returned more than {MaxRows} rows; add a LIMIT.");
        }

        rows.Add(ReadRow(reader, columns));
      }

      await reader.CloseAsync();
      affected = Math.Max(reader.RecordsAffected, 0);
    }

    return new SqlResult(columns, rows, affected);
  }

  private static List<object?> ReadRow(NpgsqlDataReader reader, List<string> columns)
  {
    List<object?> row = new(columns.Count);
    for (int column = 0; column < columns.Count; column++)
    {
      if (reader.IsDBNull(column))
      {
        row.Add(null);
        continue;
      }

      object value;
      try
      {
        value = reader.GetValue(column);
      }
      catch (Exception exception) when (exception is InvalidCastException || exception is NotSupportedException)
      {
        throw new ArgumentException($"Column {columns[column]} has a type the database API cannot return; cast it to text.");
      }

      row.Add(value is Vector vector ? vector.ToArray() : value);
    }

    return row;
  }

  // Strings go untyped so PostgreSQL infers them (text, timestamptz, ...). Number arrays arrive as double precision[]:
  // cast them with $n::vector for embeddings or $n::bigint[] for ids.
  private static NpgsqlParameter Parameter(JsonElement value)
  {
    switch (value.ValueKind)
    {
      case JsonValueKind.String:
        return new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Unknown, Value = value.GetString()! };
      case JsonValueKind.Number:
        return value.TryGetInt64(out long whole) ? new NpgsqlParameter { Value = whole } : new NpgsqlParameter { Value = value.GetDouble() };
      case JsonValueKind.True:
      case JsonValueKind.False:
        return new NpgsqlParameter { Value = value.GetBoolean() };
      case JsonValueKind.Null:
        return new NpgsqlParameter { Value = DBNull.Value };
      case JsonValueKind.Array:
        return ArrayParameter(value);
      default:
        throw new ArgumentException("Parameters are strings, numbers, booleans, null, or arrays of numbers or of strings.");
    }
  }

  private static NpgsqlParameter ArrayParameter(JsonElement value)
  {
    int length = value.GetArrayLength();
    bool strings = length > 0 && value[0].ValueKind == JsonValueKind.String;
    if (strings)
    {
      string[] texts = new string[length];
      for (int index = 0; index < length; index++)
      {
        JsonElement item = value[index];
        if (item.ValueKind != JsonValueKind.String)
        {
          throw new ArgumentException("An array parameter holds only numbers or only strings.");
        }

        texts[index] = item.GetString()!;
      }

      return new NpgsqlParameter { Value = texts };
    }

    double[] numbers = new double[length];
    for (int index = 0; index < length; index++)
    {
      JsonElement item = value[index];
      if (item.ValueKind != JsonValueKind.Number)
      {
        throw new ArgumentException("An array parameter holds only numbers or only strings.");
      }

      numbers[index] = item.GetDouble();
    }

    return new NpgsqlParameter { Value = numbers };
  }

  private async Task<NpgsqlDataSource> SourceAsync(string memberId)
  {
    lock (gate)
    {
      if (sources.TryGetValue(memberId, out NpgsqlDataSource? existing))
      {
        return existing;
      }
    }

    AgentDatabaseLogin login = await EnsureLoginAsync(memberId);
    return CacheSource(memberId, login);
  }

  private NpgsqlDataSource CacheSource(string key, AgentDatabaseLogin login)
  {
    NpgsqlConnectionStringBuilder connection = new(options.Value.ConnectionString)
    {
      Username = login.Role,
      Password = login.Password,
      SearchPath = login.Role + ",public",
      MaxPoolSize = ConnectionLimit
    };
    NpgsqlDataSourceBuilder builder = new(connection.ConnectionString);
    builder.UseVector();
    NpgsqlDataSource created = builder.Build();
    lock (gate)
    {
      if (sources.TryGetValue(key, out NpgsqlDataSource? raced))
      {
        created.Dispose();
        return raced;
      }

      sources.Add(key, created);
      return created;
    }
  }

  private async Task<AgentDatabaseLogin> EnsureLoginAsync(string memberId)
  {
    await provisioning.WaitAsync();
    try
    {
      AgentDatabaseLogin? existing = await FindLoginAsync(memberId);
      if (existing is not null)
      {
        return existing.Value;
      }

      // Role names cannot be parameters; the name is built only from the member's hex id.
      string role = RolePrefix + HexId(memberId);
      string password = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(PasswordBytes));
      await using NpgsqlConnection connection = await database.OpenConnectionAsync();
      await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
      string create = $"""
        CREATE ROLE {role} LOGIN PASSWORD '{password}' NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION CONNECTION LIMIT {ConnectionLimit};
        CREATE SCHEMA {role} AUTHORIZATION {role};
        ALTER ROLE {role} SET search_path = {role}, public;
        ALTER ROLE {role} SET statement_timeout = '{CommandSeconds}s';
        """;
      await using (NpgsqlCommand command = new(create, connection, transaction))
      {
        await command.ExecuteNonQueryAsync();
      }

      await using (NpgsqlCommand remember = new("INSERT INTO agent_databases(member_id, role_name, password) VALUES($1, $2, $3)", connection, transaction))
      {
        remember.Parameters.AddWithValue(memberId);
        remember.Parameters.AddWithValue(role);
        remember.Parameters.AddWithValue(password);
        await remember.ExecuteNonQueryAsync();
      }

      await transaction.CommitAsync();
      return new AgentDatabaseLogin(role, password);
    }
    finally
    {
      provisioning.Release();
    }
  }

  private async Task<AgentDatabaseLogin?> FindLoginAsync(string memberId)
  {
    await using NpgsqlCommand command = database.CreateCommand("SELECT role_name, password FROM agent_databases WHERE member_id = $1");
    command.Parameters.AddWithValue(memberId);
    await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
    {
      return null;
    }

    return new AgentDatabaseLogin(reader.GetString(0), reader.GetString(1));
  }

  private static string HexId(string memberId)
  {
    ReadOnlySpan<char> id = memberId.AsSpan();
    bool valid = id.Length == MemberIdLength;
    for (int index = 0; valid && index < id.Length; index++)
    {
      valid = char.IsAsciiHexDigitLower(id[index]) || char.IsAsciiDigit(id[index]);
    }

    if (!valid)
    {
      throw new InvalidOperationException("This member has no id a database role can be named after.");
    }

    return memberId;
  }
}
