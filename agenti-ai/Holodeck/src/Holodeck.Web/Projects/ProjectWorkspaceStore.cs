using System.Text;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace Holodeck.Web.Projects;

// Stores each project's window layout as given. Deleting a project deletes its workspace.
public sealed class ProjectWorkspaceStore(NpgsqlDataSource database)
{
  public const int MaxDocumentBytes = 256 * 1024;

  public async Task InitializeAsync(CancellationToken token)
  {
    const string Schema = """
      CREATE TABLE IF NOT EXISTS project_workspaces (
        project_id text PRIMARY KEY REFERENCES memory_projects(id) ON DELETE CASCADE,
        layout jsonb,
        updated_at timestamptz NOT NULL);
      ALTER TABLE project_workspaces DROP COLUMN IF EXISTS appearance;
      """;
    await using NpgsqlCommand command = database.CreateCommand(Schema);
    await command.ExecuteNonQueryAsync(token);
  }

  public async Task<ProjectWorkspace> ReadAsync(string projectId, CancellationToken token)
  {
    const string Sql = "SELECT w.layout::text FROM memory_projects p LEFT JOIN project_workspaces w ON w.project_id = p.id WHERE p.id = $1";
    await using NpgsqlCommand command = database.CreateCommand(Sql);
    command.Parameters.AddWithValue(projectId);
    await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
    if (!await reader.ReadAsync(token))
    {
      throw new KeyNotFoundException("Project was not found.");
    }

    JsonElement? layout = reader.IsDBNull(0) ? null : Parse(reader.GetString(0));
    return new ProjectWorkspace(layout);
  }

  public async Task WriteAsync(string projectId, ProjectWorkspace workspace, CancellationToken token)
  {
    string? layout = Validate(workspace.Layout, "Layout");
    int bytes = layout is null ? 0 : Encoding.UTF8.GetByteCount(layout);
    if (bytes > MaxDocumentBytes)
    {
      throw new ArgumentException($"The workspace is larger than {MaxDocumentBytes} bytes.");
    }

    const string Sql = """
      INSERT INTO project_workspaces(project_id, layout, updated_at)
      SELECT id, $2, now() FROM memory_projects WHERE id = $1
      ON CONFLICT (project_id) DO UPDATE SET layout = excluded.layout, updated_at = excluded.updated_at
      RETURNING project_id
      """;
    await using NpgsqlCommand command = database.CreateCommand(Sql);
    command.Parameters.AddWithValue(projectId);
    command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Jsonb, Value = (object?)layout ?? DBNull.Value });
    if (await command.ExecuteScalarAsync(token) is null)
    {
      throw new KeyNotFoundException("Project was not found.");
    }
  }

  private static string? Validate(JsonElement? document, string name)
  {
    if (document is null || document.Value.ValueKind == JsonValueKind.Null)
    {
      return null;
    }

    if (document.Value.ValueKind != JsonValueKind.Object)
    {
      throw new ArgumentException($"{name} must be a JSON object.");
    }

    return document.Value.GetRawText();
  }

  private static JsonElement Parse(string json)
  {
    using JsonDocument document = JsonDocument.Parse(json);
    return document.RootElement.Clone();
  }
}
