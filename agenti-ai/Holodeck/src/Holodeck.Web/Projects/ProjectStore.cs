using Npgsql;
using NpgsqlTypes;

namespace Holodeck.Web.Projects;

// Projects and folders. The tables keep their original names so projects created before rooms stay visible.
public sealed class ProjectStore(NpgsqlDataSource database)
{
  public const string DefaultProjectId = "00000000000000000000000000000001";
  private const int MaxNameLength = 240;
  private const int MaxDescriptionLength = 4000;
  // The folder $1 and every folder inside it, as the CTE "tree".
  private const string FolderTree = "WITH RECURSIVE tree AS (SELECT id FROM workspace_folders WHERE id = $1 UNION ALL SELECT f.id FROM workspace_folders f JOIN tree t ON f.parent_id = t.id) ";

  public async Task InitializeAsync(CancellationToken token)
  {
    const string Schema = """
      CREATE TABLE IF NOT EXISTS memory_projects (id text PRIMARY KEY, name text NOT NULL, description text NOT NULL, created_at timestamptz NOT NULL);
      CREATE TABLE IF NOT EXISTS workspace_folders (id text PRIMARY KEY, name text NOT NULL, parent_id text REFERENCES workspace_folders(id));
      ALTER TABLE memory_projects ADD COLUMN IF NOT EXISTS folder_id text REFERENCES workspace_folders(id);
      """;
    await ExecuteAsync(Schema, token);
    await ExecuteAsync("INSERT INTO memory_projects(id, name, description, created_at) VALUES ($1, 'Default', '', now()) ON CONFLICT (id) DO NOTHING", token, DefaultProjectId);
  }

  public async Task<ProjectListing> ListAsync(CancellationToken token)
  {
    List<Folder> folders = new();
    List<Project> projects = new();
    await using NpgsqlConnection connection = await database.OpenConnectionAsync(token);
    await using NpgsqlCommand command = new("SELECT id, name, parent_id FROM workspace_folders ORDER BY lower(name); SELECT id, name, description, folder_id FROM memory_projects ORDER BY lower(name)", connection);
    await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
    while (await reader.ReadAsync(token))
    {
      folders.Add(new Folder(reader.GetString(0), reader.GetString(1), ReadOptional(reader, 2)));
    }

    await reader.NextResultAsync(token);
    while (await reader.ReadAsync(token))
    {
      projects.Add(ReadProject(reader));
    }

    return new ProjectListing(folders, projects);
  }

  public async Task<Project?> GetAsync(string id, CancellationToken token)
  {
    await using NpgsqlCommand command = database.CreateCommand("SELECT id, name, description, folder_id FROM memory_projects WHERE id = $1");
    command.Parameters.AddWithValue(id);
    await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
    if (!await reader.ReadAsync(token))
    {
      return null;
    }

    return ReadProject(reader);
  }

  public async Task<Project> CreateAsync(ProjectInput input, CancellationToken token)
  {
    Project project = new(Guid.NewGuid().ToString("N"), ValidateName(input.Name), ValidateDescription(input.Description), input.FolderId);
    await ExecuteAsync("INSERT INTO memory_projects(id, name, description, created_at, folder_id) VALUES ($1, $2, $3, now(), $4)", token, project.Id, project.Name, project.Description, project.FolderId);
    return project;
  }

  public async Task UpdateAsync(string id, ProjectInput input, CancellationToken token)
  {
    string name = ValidateName(input.Name);
    string description = ValidateDescription(input.Description);
    await RequireChangeAsync("UPDATE memory_projects SET name = $2, description = $3, folder_id = $4 WHERE id = $1", token, id, name, description, input.FolderId);
  }

  public async Task DeleteAsync(string id, CancellationToken token)
  {
    if (id == DefaultProjectId)
    {
      throw new InvalidOperationException("The Default project is the fallback workspace and cannot be deleted.");
    }

    await using NpgsqlConnection connection = await database.OpenConnectionAsync(token);
    await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token);
    // Chats from before rooms reference projects without cascading; remove them with their project.
    await using (NpgsqlCommand probe = new("SELECT to_regclass('conversations') IS NOT NULL", connection, transaction))
    {
      bool hasOldChats = (bool)(await probe.ExecuteScalarAsync(token))!;
      if (hasOldChats)
      {
        await using NpgsqlCommand chats = new("DELETE FROM conversations WHERE project_id = $1", connection, transaction);
        chats.Parameters.AddWithValue(id);
        await chats.ExecuteNonQueryAsync(token);
      }
    }

    await using NpgsqlCommand delete = new("DELETE FROM memory_projects WHERE id = $1", connection, transaction);
    delete.Parameters.AddWithValue(id);
    if (await delete.ExecuteNonQueryAsync(token) == 0)
    {
      throw new KeyNotFoundException("Project was not found.");
    }

    await transaction.CommitAsync(token);
  }

  public async Task<Folder> CreateFolderAsync(FolderInput input, CancellationToken token)
  {
    Folder folder = new(Guid.NewGuid().ToString("N"), ValidateName(input.Name), input.ParentId);
    await ExecuteAsync("INSERT INTO workspace_folders(id, name, parent_id) VALUES ($1, $2, $3)", token, folder.Id, folder.Name, folder.ParentId);
    return folder;
  }

  // Renames a folder and moves it under ParentId, which must not be the folder itself or one inside it.
  public async Task UpdateFolderAsync(string id, FolderInput input, CancellationToken token)
  {
    string name = ValidateName(input.Name);
    if (input.ParentId is not null && await IsInFolderAsync(input.ParentId, id, token))
    {
      throw new ArgumentException("A folder cannot move into itself or a folder inside it.");
    }

    await RequireChangeAsync("UPDATE workspace_folders SET name = $2, parent_id = $3 WHERE id = $1", token, id, name, input.ParentId);
  }

  // Every project inside the folder or its subfolders, except the Default project, which a folder deletion keeps.
  public async Task<List<string>> ListProjectsInFolderAsync(string id, CancellationToken token)
  {
    List<string> projects = new();
    await using NpgsqlCommand command = database.CreateCommand(FolderTree + "SELECT p.id FROM memory_projects p JOIN tree t ON p.folder_id = t.id WHERE p.id <> $2");
    command.Parameters.AddWithValue(id);
    command.Parameters.AddWithValue(DefaultProjectId);
    await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
    while (await reader.ReadAsync(token))
    {
      projects.Add(reader.GetString(0));
    }

    return projects;
  }

  // Deletes the folder and its subfolders. Their projects must already be deleted; the Default project moves to the top level.
  public async Task DeleteFolderAsync(string id, CancellationToken token)
  {
    await using NpgsqlConnection connection = await database.OpenConnectionAsync(token);
    await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token);
    await using (NpgsqlCommand keep = new(FolderTree + "UPDATE memory_projects SET folder_id = NULL WHERE id = $2 AND folder_id IN (SELECT id FROM tree)", connection, transaction))
    {
      keep.Parameters.AddWithValue(id);
      keep.Parameters.AddWithValue(DefaultProjectId);
      await keep.ExecuteNonQueryAsync(token);
    }

    await using NpgsqlCommand delete = new(FolderTree + "DELETE FROM workspace_folders WHERE id IN (SELECT id FROM tree)", connection, transaction);
    delete.Parameters.AddWithValue(id);
    if (await delete.ExecuteNonQueryAsync(token) == 0)
    {
      throw new KeyNotFoundException("Folder was not found.");
    }

    await transaction.CommitAsync(token);
  }

  public static string ValidateName(string? name)
  {
    ReadOnlySpan<char> trimmed = name.AsSpan().Trim();
    if (trimmed.IsEmpty)
    {
      throw new ArgumentException("A name is required.");
    }

    if (trimmed.Length > MaxNameLength)
    {
      throw new ArgumentException($"Use a name of up to {MaxNameLength} characters.");
    }

    return trimmed.ToString();
  }

  private static string ValidateDescription(string? description)
  {
    if (description is null)
    {
      return "";
    }

    if (description.Length > MaxDescriptionLength)
    {
      throw new ArgumentException($"Use a description of up to {MaxDescriptionLength} characters.");
    }

    return description;
  }

  private async Task<bool> IsInFolderAsync(string candidate, string folder, CancellationToken token)
  {
    await using NpgsqlCommand command = database.CreateCommand(FolderTree + "SELECT EXISTS (SELECT 1 FROM tree WHERE id = $2)");
    command.Parameters.AddWithValue(folder);
    command.Parameters.AddWithValue(candidate);
    return (bool)(await command.ExecuteScalarAsync(token))!;
  }

  private static Project ReadProject(NpgsqlDataReader reader)
  {
    return new Project(reader.GetString(0), reader.GetString(1), reader.GetString(2), ReadOptional(reader, 3));
  }

  private static string? ReadOptional(NpgsqlDataReader reader, int ordinal)
  {
    if (reader.IsDBNull(ordinal))
    {
      return null;
    }

    return reader.GetString(ordinal);
  }

  private async Task RequireChangeAsync(string sql, CancellationToken token, params string?[] values)
  {
    if (await ExecuteAsync(sql, token, values) == 0)
    {
      throw new KeyNotFoundException("Item was not found.");
    }
  }

  private async Task<int> ExecuteAsync(string sql, CancellationToken token, params string?[] values)
  {
    await using NpgsqlCommand command = database.CreateCommand(sql);
    foreach (string? value in values)
    {
      command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = (object?)value ?? DBNull.Value });
    }

    return await command.ExecuteNonQueryAsync(token);
  }
}
