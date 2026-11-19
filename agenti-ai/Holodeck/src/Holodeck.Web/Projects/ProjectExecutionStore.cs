using Npgsql;

namespace Holodeck.Web.Projects;

// Browser connections do not own project execution. Persist intent so the platform can reconnect after restart.
public sealed class ProjectExecutionStore(NpgsqlDataSource database)
{
  public async Task InitializeAsync()
  {
    const string Schema = """
      CREATE TABLE IF NOT EXISTS project_executions (
        project_id text PRIMARY KEY REFERENCES memory_projects(id) ON DELETE CASCADE,
        running boolean NOT NULL);
      """;
    await using NpgsqlCommand command = database.CreateCommand(Schema);
    await command.ExecuteNonQueryAsync();
  }

  public async Task SetRunningAsync(string projectId, bool running, CancellationToken token)
  {
    const string Sql = "INSERT INTO project_executions(project_id, running) VALUES ($1, $2) ON CONFLICT(project_id) DO UPDATE SET running = EXCLUDED.running";
    await using NpgsqlCommand command = database.CreateCommand(Sql);
    command.Parameters.AddWithValue(projectId);
    command.Parameters.AddWithValue(running);
    await command.ExecuteNonQueryAsync(token);
  }

  public async Task<List<string>> ListRunningAsync(CancellationToken token)
  {
    List<string> projects = new();
    await using NpgsqlCommand command = database.CreateCommand("SELECT project_id FROM project_executions WHERE running ORDER BY project_id");
    await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
    while (await reader.ReadAsync(token))
    {
      projects.Add(reader.GetString(0));
    }

    return projects;
  }

  public async Task<bool> IsRunningAsync(string projectId, CancellationToken token)
  {
    await using NpgsqlCommand command = database.CreateCommand("SELECT running FROM project_executions WHERE project_id = $1");
    command.Parameters.AddWithValue(projectId);
    object? value = await command.ExecuteScalarAsync(token);
    return value is bool running && running;
  }
}
