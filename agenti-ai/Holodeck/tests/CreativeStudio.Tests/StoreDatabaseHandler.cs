using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace CreativeStudio.Tests;

// Runs the real store SQL in a disposable schema while preserving the production HTTP transport boundary.
internal sealed class StoreDatabaseHandler : HttpMessageHandler, IAsyncDisposable
{
  private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
  private readonly string connectionString;
  private readonly string schema;
  private readonly NpgsqlDataSource database;

  private StoreDatabaseHandler(string connectionString, string schema)
  {
    this.connectionString = connectionString;
    this.schema = schema;
    NpgsqlConnectionStringBuilder builder = new(connectionString) { SearchPath = schema + ",public" };
    database = NpgsqlDataSource.Create(builder.ConnectionString);
  }

  public bool FailNextRequest { get; set; }

  public static async Task<StoreDatabaseHandler> CreateAsync()
  {
    string connection = Environment.GetEnvironmentVariable(StoreDatabaseFactAttribute.ConnectionVariable)!;
    string schema = "creative_t_" + Guid.NewGuid().ToString("N");
    await using NpgsqlDataSource shared = NpgsqlDataSource.Create(connection);
    await using NpgsqlCommand command = shared.CreateCommand($"CREATE EXTENSION IF NOT EXISTS vector SCHEMA public; CREATE SCHEMA {schema}");
    await command.ExecuteNonQueryAsync();
    return new StoreDatabaseHandler(connection, schema);
  }

  protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
  {
    if (FailNextRequest)
    {
      FailNextRequest = false;
      throw new HttpRequestException("The host is temporarily unavailable.");
    }

    SqlRequest input = await request.Content!.ReadFromJsonAsync<SqlRequest>(Json, token) ?? throw new IOException("Missing SQL request.");
    await using NpgsqlConnection connection = await database.OpenConnectionAsync(token);
    await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token);
    List<SqlResult> results = new();
    foreach (SqlStatement statement in input.Statements)
    {
      results.Add(await ExecuteAsync(connection, transaction, statement, token));
    }

    await transaction.CommitAsync(token);
    return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new SqlResponse(results), options: Json) };
  }

  private static async Task<SqlResult> ExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, SqlStatement statement, CancellationToken token)
  {
    await using NpgsqlCommand command = new(statement.Sql, connection, transaction);
    foreach (object? parameter in statement.Parameters)
    {
      command.Parameters.Add(Parameter(parameter));
    }

    List<string> columns = new();
    List<List<JsonElement>> rows = new();
    int affected;
    await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(token))
    {
      for (int index = 0; index < reader.FieldCount; index++)
      {
        columns.Add(reader.GetName(index));
      }
      while (await reader.ReadAsync(token))
      {
        List<JsonElement> row = new();
        for (int index = 0; index < reader.FieldCount; index++)
        {
          object? value = reader.IsDBNull(index) ? null : reader.GetValue(index);
          row.Add(JsonSerializer.SerializeToElement(value, Json));
        }
        rows.Add(row);
      }
      affected = Math.Max(reader.RecordsAffected, 0);
    }

    return new SqlResult(columns, rows, affected);
  }

  private static NpgsqlParameter Parameter(object? input)
  {
    if (input is null)
    {
      return new NpgsqlParameter { Value = DBNull.Value };
    }

    JsonElement value = (JsonElement)input;
    switch (value.ValueKind)
    {
      case JsonValueKind.Null:
        return new NpgsqlParameter { Value = DBNull.Value };
      case JsonValueKind.String:
        return new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Unknown, Value = value.GetString()! };
      case JsonValueKind.Number:
        return value.TryGetInt64(out long integer) ? new NpgsqlParameter { Value = integer } : new NpgsqlParameter { Value = value.GetDouble() };
      case JsonValueKind.True:
      case JsonValueKind.False:
        return new NpgsqlParameter { Value = value.GetBoolean() };
      case JsonValueKind.Array:
        double[] numbers = new double[value.GetArrayLength()];
        for (int index = 0; index < numbers.Length; index++)
        {
          numbers[index] = value[index].GetDouble();
        }
        return new NpgsqlParameter { Value = numbers };
      default:
        throw new IOException("Unsupported SQL parameter.");
    }
  }

  public async ValueTask DisposeAsync()
  {
    await database.DisposeAsync();
    await using NpgsqlDataSource shared = NpgsqlDataSource.Create(connectionString);
    await using NpgsqlCommand command = shared.CreateCommand($"DROP SCHEMA {schema} CASCADE");
    await command.ExecuteNonQueryAsync();
    Dispose();
  }
}
