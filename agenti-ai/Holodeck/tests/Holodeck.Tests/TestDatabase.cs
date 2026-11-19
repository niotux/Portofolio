using Npgsql;

namespace Holodeck.Tests;

// Each test gets its own PostgreSQL schema and data directory, both removed on dispose.
internal sealed class TestDatabase : IAsyncDisposable
{
  private readonly string connectionString;
  private readonly string schema;

  private TestDatabase(string connectionString, string schema, string dataPath)
  {
    this.connectionString = connectionString;
    this.schema = schema;
    DataPath = dataPath;
    NpgsqlConnectionStringBuilder builder = new(connectionString) { SearchPath = schema };
    ScopedConnectionString = builder.ConnectionString;
    Database = NpgsqlDataSource.Create(ScopedConnectionString);
  }

  public NpgsqlDataSource Database { get; private set; }
  public string DataPath { get; }
  public string ScopedConnectionString { get; }

  public static async Task<TestDatabase> CreateAsync()
  {
    string connectionString = Environment.GetEnvironmentVariable(DatabaseFactAttribute.ConnectionVariable)!;
    string schema = "t_" + Guid.NewGuid().ToString("N");
    await using (NpgsqlDataSource shared = NpgsqlDataSource.Create(connectionString))
    {
      await using NpgsqlCommand command = shared.CreateCommand($"CREATE SCHEMA {schema}");
      await command.ExecuteNonQueryAsync();
    }

    string dataPath = Path.Combine(Path.GetTempPath(), "holodeck-test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dataPath);
    return new TestDatabase(connectionString, schema, dataPath);
  }

  // Simulates an application restart: a fresh data source over the same schema and files.
  public async Task RestartAsync()
  {
    await Database.DisposeAsync();
    Database = NpgsqlDataSource.Create(ScopedConnectionString);
  }

  public async Task ExecuteAsync(string sql)
  {
    await using NpgsqlCommand command = Database.CreateCommand(sql);
    await command.ExecuteNonQueryAsync();
  }

  public async Task<long> CountAsync(string sql)
  {
    await using NpgsqlCommand command = Database.CreateCommand(sql);
    return (long)(await command.ExecuteScalarAsync())!;
  }

  public async ValueTask DisposeAsync()
  {
    await Database.DisposeAsync();
    await using NpgsqlDataSource shared = NpgsqlDataSource.Create(connectionString);
    await using NpgsqlCommand command = shared.CreateCommand($"DROP SCHEMA IF EXISTS {schema} CASCADE");
    await command.ExecuteNonQueryAsync();
    if (Directory.Exists(DataPath))
    {
      Directory.Delete(DataPath, true);
    }
  }
}
