using Holodeck.Web.Rooms;
using Npgsql;

namespace Holodeck.Web.Projects;

// A project references local libraries independently of its placed agents. Removing a reference never
// removes room members or their files; those members remain available for repair or explicit removal.
public sealed class ProjectLibraryStore(NpgsqlDataSource database, AgentLibrary library)
{
  public async Task InitializeAsync(CancellationToken token)
  {
    const string Schema = """
      CREATE TABLE IF NOT EXISTS project_libraries (
        project_id text NOT NULL REFERENCES memory_projects(id) ON DELETE CASCADE,
        library_id text NOT NULL,
        version text NOT NULL,
        name text NOT NULL,
        description text NOT NULL,
        PRIMARY KEY(project_id, library_id, version));
      CREATE TABLE IF NOT EXISTS project_library_migrations (
        project_id text PRIMARY KEY REFERENCES memory_projects(id) ON DELETE CASCADE);
      """;
    await using (NpgsqlCommand command = database.CreateCommand(Schema))
    {
      await command.ExecuteNonQueryAsync(token);
    }

    await BackfillAsync(token);
  }

  private async Task BackfillAsync(CancellationToken token)
  {
    const string Sql = """
      WITH migrated AS (
        INSERT INTO project_library_migrations(project_id)
        SELECT id FROM memory_projects
        ON CONFLICT DO NOTHING
        RETURNING project_id)
      INSERT INTO project_libraries(project_id, library_id, version, name, description)
      SELECT DISTINCT m.project_id, split_part(m.library, '/', 1), split_part(m.library, '/', 2), split_part(m.library, '/', 1), ''
      FROM room_members m JOIN migrated p ON p.project_id = m.project_id
      WHERE NOT m.removed AND array_length(string_to_array(m.library, '/'), 1) = 4 AND split_part(m.library, '/', 3) = 'agents'
      ON CONFLICT DO NOTHING
      RETURNING project_id, library_id, version;
      """;
    await using NpgsqlConnection connection = await database.OpenConnectionAsync(token);
    await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token);
    List<ProjectLibraryMigration> migrations = new();
    await using (NpgsqlCommand command = new(Sql, connection, transaction))
    {
      await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(token);
      while (await reader.ReadAsync(token))
      {
        migrations.Add(new ProjectLibraryMigration(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
      }
    }

    Dictionary<string, LibrarySummary> installed = Index(library.ReadCatalog().Libraries);
    foreach (ProjectLibraryMigration migration in migrations)
    {
      if (!installed.ContainsKey(Key(migration.Id, migration.Version)))
      {
        continue;
      }

      List<LibrarySummary> additions = new();
      CollectDependencies(migration.Id, migration.Version, installed, new(), additions);
      foreach (LibrarySummary addition in additions)
      {
        await InsertReferenceAsync(connection, transaction, migration.ProjectId, addition);
      }
    }

    await transaction.CommitAsync(token);
  }

  public async Task<List<ProjectLibraryView>> ListAsync(string projectId)
  {
    await RequireProjectAsync(projectId);
    List<LibrarySummary> references = await ReadReferencesAsync(projectId);
    LibraryCatalog catalog = library.ReadCatalog();
    Dictionary<string, LibrarySummary> installed = Index(catalog.Libraries);
    Dictionary<string, LibrarySummary> attached = Index(references);
    List<ProjectLibraryView> views = new();
    foreach (LibrarySummary reference in references)
    {
      LibrarySummary display = installed.GetValueOrDefault(Key(reference.Id, reference.Version)) ?? reference;
      string? error = FindError(reference.Id, reference.Version, installed, attached, new());
      views.Add(new ProjectLibraryView(reference.Id, reference.Version, display.Name, display.Description, error is null, error));
    }

    views.Sort((first, second) => string.Compare(first.Name, second.Name, StringComparison.OrdinalIgnoreCase));
    return views;
  }

  public async Task AddAsync(string projectId, string id, string version)
  {
    ValidateIdentity(id, version);
    Dictionary<string, LibrarySummary> installed = Index(library.ReadCatalog().Libraries);
    List<LibrarySummary> additions = new();
    CollectDependencies(id, version, installed, new(), additions);
    await using NpgsqlConnection connection = await database.OpenConnectionAsync();
    await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
    await LockProjectAsync(connection, transaction, projectId);
    await MarkProjectAsync(connection, transaction, projectId);
    foreach (LibrarySummary addition in additions)
    {
      await InsertReferenceAsync(connection, transaction, projectId, addition);
    }

    await transaction.CommitAsync();
  }

  public async Task RemoveAsync(string projectId, string id, string version)
  {
    ValidateIdentity(id, version);
    await using NpgsqlConnection connection = await database.OpenConnectionAsync();
    await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
    await LockProjectAsync(connection, transaction, projectId);
    await MarkProjectAsync(connection, transaction, projectId);
    await using NpgsqlCommand command = new("DELETE FROM project_libraries WHERE project_id = $1 AND library_id = $2 AND version = $3", connection, transaction);
    command.Parameters.AddWithValue(projectId);
    command.Parameters.AddWithValue(id);
    command.Parameters.AddWithValue(version);
    if (await command.ExecuteNonQueryAsync() == 0)
    {
      throw new KeyNotFoundException("That library is not referenced by this project.");
    }

    await transaction.CommitAsync();
  }

  public async Task RequireAgentAsync(string projectId, LibraryAgent agent)
  {
    string? error = await ErrorForAsync(projectId, agent.Folder);
    if (error is not null)
    {
      throw new InvalidOperationException(error);
    }
  }

  public async Task RequireTeamAsync(string projectId, LibraryTeam team)
  {
    string[] reference = ParseReference(team.Folder, "teams");
    await RequireProjectAsync(projectId);
    string? error = await LibraryErrorAsync(projectId, reference[0], reference[1]);
    if (error is not null)
    {
      throw new InvalidOperationException(error);
    }

    foreach (LibraryTeamMember member in team.Members)
    {
      await RequireAgentAsync(projectId, member.Agent);
    }
  }

  public async Task<string?> ErrorForAsync(string projectId, string? reference)
  {
    if (reference is null)
    {
      return null;
    }

    if (!reference.Contains('/'))
    {
      return "This agent used an old-style definition that no longer exists. Replace its definition with one from a library.";
    }

    string[] parts = ParseReference(reference, "agents");
    string? error = await LibraryErrorAsync(projectId, parts[0], parts[1]);
    if (error is not null)
    {
      return error;
    }

    foreach (LibraryAgent agent in library.ReadCatalog().Agents)
    {
      if (agent.Folder == reference)
      {
        return null;
      }
    }

    return "The agent definition is missing from its local library.";
  }

  private async Task<string?> LibraryErrorAsync(string projectId, string id, string version)
  {
    List<LibrarySummary> references = await ReadReferencesAsync(projectId);
    return FindError(id, version, Index(library.ReadCatalog().Libraries), Index(references), new());
  }

  private static string? FindError(string id, string version, Dictionary<string, LibrarySummary> installed, Dictionary<string, LibrarySummary> attached, HashSet<string> checkedLibraries)
  {
    string key = Key(id, version);
    if (!attached.ContainsKey(key))
    {
      return $"Library {id} version {version} is not referenced by this project.";
    }

    if (!installed.TryGetValue(key, out LibrarySummary? source))
    {
      return $"Library {id} version {version} is unavailable in the local library folder.";
    }

    if (!checkedLibraries.Add(key))
    {
      return null;
    }

    if (source.Dependencies is not null)
    {
      foreach (LibraryReference dependency in source.Dependencies)
      {
        string? error = FindError(dependency.Id, dependency.Version, installed, attached, checkedLibraries);
        if (error is not null)
        {
          return error;
        }
      }
    }

    return null;
  }

  private static void CollectDependencies(string id, string version, Dictionary<string, LibrarySummary> installed, HashSet<string> visited, List<LibrarySummary> additions)
  {
    string key = Key(id, version);
    if (!installed.TryGetValue(key, out LibrarySummary? source))
    {
      throw new KeyNotFoundException($"Library {id} version {version} is not available locally.");
    }

    if (!visited.Add(key))
    {
      return;
    }

    if (source.Dependencies is not null)
    {
      foreach (LibraryReference dependency in source.Dependencies)
      {
        CollectDependencies(dependency.Id, dependency.Version, installed, visited, additions);
      }
    }

    additions.Add(source);
  }

  private async Task<List<LibrarySummary>> ReadReferencesAsync(string projectId)
  {
    List<LibrarySummary> references = new();
    await using NpgsqlCommand command = database.CreateCommand("SELECT library_id, version, name, description FROM project_libraries WHERE project_id = $1 ORDER BY library_id, version");
    command.Parameters.AddWithValue(projectId);
    await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
      references.Add(new LibrarySummary(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
    }

    return references;
  }

  private async Task RequireProjectAsync(string projectId)
  {
    const string Sql = "WITH marked AS (INSERT INTO project_library_migrations(project_id) SELECT id FROM memory_projects WHERE id = $1 ON CONFLICT DO NOTHING RETURNING project_id) SELECT EXISTS(SELECT 1 FROM memory_projects WHERE id = $1)";
    await using NpgsqlCommand command = database.CreateCommand(Sql);
    command.Parameters.AddWithValue(projectId);
    bool exists = (bool)(await command.ExecuteScalarAsync())!;
    if (!exists)
    {
      throw new KeyNotFoundException("Project was not found.");
    }
  }

  private static async Task LockProjectAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string projectId)
  {
    await using NpgsqlCommand command = new("SELECT id FROM memory_projects WHERE id = $1 FOR UPDATE", connection, transaction);
    command.Parameters.AddWithValue(projectId);
    if (await command.ExecuteScalarAsync() is null)
    {
      throw new KeyNotFoundException("Project was not found.");
    }
  }

  private static async Task InsertReferenceAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string projectId, LibrarySummary source)
  {
    const string Sql = "INSERT INTO project_libraries(project_id, library_id, version, name, description) VALUES ($1, $2, $3, $4, $5) ON CONFLICT (project_id, library_id, version) DO UPDATE SET name = EXCLUDED.name, description = EXCLUDED.description";
    await using NpgsqlCommand command = new(Sql, connection, transaction);
    command.Parameters.AddWithValue(projectId);
    command.Parameters.AddWithValue(source.Id);
    command.Parameters.AddWithValue(source.Version);
    command.Parameters.AddWithValue(source.Name);
    command.Parameters.AddWithValue(source.Description);
    await command.ExecuteNonQueryAsync();
  }

  private static async Task MarkProjectAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string projectId)
  {
    await using NpgsqlCommand command = new("INSERT INTO project_library_migrations(project_id) VALUES ($1) ON CONFLICT DO NOTHING", connection, transaction);
    command.Parameters.AddWithValue(projectId);
    await command.ExecuteNonQueryAsync();
  }

  private static Dictionary<string, LibrarySummary> Index(List<LibrarySummary> summaries)
  {
    Dictionary<string, LibrarySummary> indexed = new(StringComparer.Ordinal);
    foreach (LibrarySummary summary in summaries)
    {
      indexed.Add(Key(summary.Id, summary.Version), summary);
    }

    return indexed;
  }

  private static string[] ParseReference(string reference, string kind)
  {
    string[] parts = reference.Split('/');
    if (parts.Length != 4 || parts[2] != kind)
    {
      throw new ArgumentException("Use a versioned local library definition reference.");
    }

    ValidateIdentity(parts[0], parts[1]);
    return parts;
  }

  private static void ValidateIdentity(string id, string version)
  {
    LibraryFiles.RequireId(id, "Library Id");
    LibraryFiles.RequireId(version, "Library Version");
  }

  private static string Key(string id, string version) => id + "/" + version;
}
