using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace Humans.Tests;

// Runs the same named SQL request/response transport against an isolated PostgreSQL schema.
public sealed class PromptDatabaseHandler : HttpMessageHandler
{
  private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
  private readonly NpgsqlDataSource database;
  private readonly string connectionString;
  private readonly string schema;

  private PromptDatabaseHandler(string connectionString, string schema)
  {
    this.connectionString = connectionString;
    this.schema = schema;
    NpgsqlConnectionStringBuilder scoped = new(connectionString) { SearchPath = schema };
    database = NpgsqlDataSource.Create(scoped.ConnectionString);
  }

  public static async Task<PromptDatabaseHandler> CreateAsync()
  {
    string connection = Environment.GetEnvironmentVariable("HOLODECK_TEST_DATABASE")!;
    string schema = "hh_prompt_test_" + Guid.NewGuid().ToString("N");
    await using NpgsqlDataSource database = NpgsqlDataSource.Create(connection);
    await using NpgsqlCommand command = database.CreateCommand("CREATE SCHEMA " + schema);
    await command.ExecuteNonQueryAsync();
    return new PromptDatabaseHandler(connection, schema);
  }

  public PromptStore Store()
  {
    return new PromptStore(new LibraryDatabase(new HttpClient(this, false), new Uri("http://holodeck.invalid/library-database"), null));
  }

  public async Task<long> CountAsync(string query)
  {
    await using NpgsqlCommand command = database.CreateCommand(query);
    return Convert.ToInt64(await command.ExecuteScalarAsync());
  }

  public async Task ExecuteAsync(string query)
  {
    await using NpgsqlCommand command = database.CreateCommand(query);
    await command.ExecuteNonQueryAsync();
  }

  protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
  {
    SqlRequest input = await request.Content!.ReadFromJsonAsync<SqlRequest>(Json, token) ?? throw new IOException("No SQL request.");
    await using NpgsqlConnection connection = await database.OpenConnectionAsync(token);
    await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token);
    List<SqlResult> results = new();
    try
    {
      foreach (SqlStatement statement in input.Statements)
      {
        results.Add(await ExecuteAsync(connection, transaction, statement, token));
      }
      await transaction.CommitAsync(token);
      return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new SqlResponse(results), options: Json) };
    }
    catch (PostgresException exception)
    {
      return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = JsonContent.Create(new DatabaseError(exception.MessageText), options: Json) };
    }
  }

  private static async Task<SqlResult> ExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, SqlStatement statement, CancellationToken token)
  {
    await using NpgsqlCommand command = new(statement.Sql, connection, transaction);
    foreach (object? parameter in statement.Parameters)
    {
      if (parameter is null)
      {
        command.Parameters.Add(new NpgsqlParameter { Value = DBNull.Value });
      }
      else
      {
        command.Parameters.Add(Parameter((JsonElement)parameter));
      }
    }
    List<string> columns = new();
    List<List<JsonElement>> rows = new();
    await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
    for (int column = 0; column < reader.FieldCount; column++)
    {
      columns.Add(reader.GetName(column));
    }
    while (await reader.ReadAsync(token))
    {
      List<JsonElement> row = new();
      for (int column = 0; column < reader.FieldCount; column++)
      {
        row.Add(JsonSerializer.SerializeToElement(reader.IsDBNull(column) ? null : reader.GetValue(column)));
      }
      rows.Add(row);
    }
    await reader.CloseAsync();
    return new SqlResult(columns, rows, Math.Max(reader.RecordsAffected, 0));
  }

  private static NpgsqlParameter Parameter(JsonElement value)
  {
    if (value.ValueKind == JsonValueKind.String)
    {
      return new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Unknown, Value = value.GetString()! };
    }
    if (value.ValueKind == JsonValueKind.Number)
    {
      return new NpgsqlParameter { Value = value.GetInt64() };
    }
    return new NpgsqlParameter { Value = DBNull.Value };
  }

  public async Task DeleteAsync()
  {
    await database.DisposeAsync();
    await using NpgsqlDataSource shared = NpgsqlDataSource.Create(connectionString);
    await using NpgsqlCommand command = shared.CreateCommand("DROP SCHEMA " + schema + " CASCADE");
    await command.ExecuteNonQueryAsync();
    Dispose();
  }
}
