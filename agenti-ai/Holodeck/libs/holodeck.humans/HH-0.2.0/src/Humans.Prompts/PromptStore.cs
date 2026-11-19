using System.Text.Json;

namespace Humans;

// Prompts of every HH version, shared by all its people in the library's Holodeck database. Edits change the live
// prompts at once; publishing keeps a numbered immutable copy; restoring makes a publication live again.
public sealed partial class PromptStore(LibraryDatabase database)
{
  private const string LiveColumns = """
    json_build_object('Version', l.version, 'Revision', l.revision,
      'Publication', (SELECT max(number) FROM hh_prompt_v1_publications p WHERE p.version = l.version AND p.content = l.content),
      'Latest', (SELECT max(number) FROM hh_prompt_v1_publications p WHERE p.version = l.version),
      'ChangedAt', l.changed_at, 'Content', l.content)::text
    """;
  private const string PublicationColumns = "json_build_object('Number', number, 'Label', label, 'CreatedAt', created_at, 'Content', content)::text";
  private bool ready;

  public async Task<PromptLive?> LiveAsync(string version, CancellationToken token)
  {
    List<SqlResult> results = await RunAsync([Sql($"SELECT {LiveColumns} FROM hh_prompt_v1_live l WHERE l.version = $1", HumanVersion.Require(version))], token);
    return results[0].Rows.Count == 0 ? null : Parse<PromptLive>(results[0].Rows[0][0]);
  }

  public async Task<PromptLive> RequireLiveAsync(string version, CancellationToken token)
  {
    return await LiveAsync(version, token) ?? throw new KeyNotFoundException(version + " has no prompts yet. Start them from an earlier version with the prompt tools.");
  }

  public async Task<List<PromptLive>> VersionsAsync(CancellationToken token)
  {
    List<SqlResult> results = await RunAsync([Sql($"SELECT {LiveColumns} FROM hh_prompt_v1_live l ORDER BY l.version")], token);
    List<PromptLive> versions = new();
    foreach (List<JsonElement> row in results[0].Rows)
    {
      versions.Add(Parse<PromptLive>(row[0]));
    }
    return versions;
  }

  public async Task<List<PromptPublication>> PublicationsAsync(string version, CancellationToken token)
  {
    List<SqlResult> results = await RunAsync([Sql($"SELECT {PublicationColumns} FROM hh_prompt_v1_publications WHERE version = $1 ORDER BY number DESC", HumanVersion.Require(version))], token);
    List<PromptPublication> publications = new();
    foreach (List<JsonElement> row in results[0].Rows)
    {
      publications.Add(Parse<PromptPublication>(row[0]));
    }
    return publications;
  }

  public async Task<PromptPublication> PublicationAsync(string version, int number, CancellationToken token)
  {
    List<SqlResult> results = await RunAsync([Sql($"SELECT {PublicationColumns} FROM hh_prompt_v1_publications WHERE version = $1 AND number = $2", HumanVersion.Require(version), number)], token);
    if (results[0].Rows.Count == 0)
    {
      throw new KeyNotFoundException(version + " has no published v" + number + ".");
    }
    return Parse<PromptPublication>(results[0].Rows[0][0]);
  }

  // The whole set is validated before it goes live, so a broken page.json never reaches a person.
  public async Task<long> EditAsync(string version, string name, string text, CancellationToken token)
  {
    PromptLive live = await RequireLiveAsync(version, token);
    PromptBundle edited = live.Content.Copy();
    if (live.Content.Files.TryGetValue(name, out string? before))
    {
      PromptBundle.ValidateReplacement(name, before, text);
    }
    edited.Files[name] = text;
    edited.Validate();
    return await ReplaceAsync(live, edited, token);
  }

  // Removing is for prompts a version no longer reads; a running person refuses a set missing one it needs.
  public async Task<long> RemoveAsync(string version, string name, CancellationToken token)
  {
    PromptLive live = await RequireLiveAsync(version, token);
    PromptBundle edited = live.Content.Copy();
    if (!edited.Files.Remove(name))
    {
      throw new KeyNotFoundException(version + " has no prompt " + name + ".");
    }
    edited.Validate();
    return await ReplaceAsync(live, edited, token);
  }

  private async Task<long> ReplaceAsync(PromptLive live, PromptBundle edited, CancellationToken token)
  {
    string version = live.Version;
    const string Update = "UPDATE hh_prompt_v1_live SET content = $3::jsonb, revision = revision + 1, changed_at = now() WHERE version = $1 AND revision = $2 RETURNING revision";
    List<SqlResult> results = await RunAsync([Sql(Update, live.Version, live.Revision, JsonSerializer.Serialize(edited))], token);
    if (results[0].Rows.Count == 0)
    {
      throw new InvalidOperationException("Someone changed " + version + "'s prompts at the same moment. Read them again and repeat the edit.");
    }
    return results[0].Rows[0][0].GetInt64();
  }

  public async Task<int> PublishAsync(string version, string label, CancellationToken token)
  {
    if (string.IsNullOrWhiteSpace(label))
    {
      throw new ArgumentException("A publication needs a label saying what it is.");
    }
    const string Lock = "SELECT 1 FROM hh_prompt_v1_live WHERE version = $1 FOR UPDATE";
    const string Insert = """
      INSERT INTO hh_prompt_v1_publications(version, number, label, content)
      SELECT l.version, COALESCE((SELECT max(number) FROM hh_prompt_v1_publications p WHERE p.version = l.version), 0) + 1, $2, l.content
      FROM hh_prompt_v1_live l
      WHERE l.version = $1 AND l.content IS DISTINCT FROM (SELECT content FROM hh_prompt_v1_publications p WHERE p.version = l.version ORDER BY number DESC LIMIT 1)
      RETURNING number
      """;
    string checkedVersion = HumanVersion.Require(version);
    List<SqlResult> results = await RunAsync([Sql(Lock, checkedVersion), Sql(Insert, checkedVersion, label)], token);
    if (results[0].Rows.Count == 0)
    {
      throw new KeyNotFoundException(version + " has no prompts yet.");
    }
    if (results[1].Rows.Count == 0)
    {
      throw new InvalidOperationException("Nothing changed since the latest publication of " + version + ".");
    }
    return results[1].Rows[0][0].GetInt32();
  }

  public async Task<long> RestoreAsync(string version, int number, CancellationToken token)
  {
    const string Restore = """
      UPDATE hh_prompt_v1_live l SET content = p.content, revision = l.revision + 1, changed_at = now()
      FROM hh_prompt_v1_publications p WHERE l.version = $1 AND p.version = $1 AND p.number = $2
      RETURNING l.revision
      """;
    List<SqlResult> results = await RunAsync([Sql(Restore, HumanVersion.Require(version), number)], token);
    if (results[0].Rows.Count == 0)
    {
      throw new KeyNotFoundException(version + " has no published v" + number + ".");
    }
    return results[0].Rows[0][0].GetInt64();
  }

  // A new HH version begins with the latest publication of the version it grew from.
  public async Task StartFromAsync(string version, string from, CancellationToken token)
  {
    List<PromptPublication> history = await PublicationsAsync(from, token);
    if (history.Count == 0)
    {
      throw new KeyNotFoundException(from + " has no published prompts to start from.");
    }
    await CreateAsync(version, history[0].Content, "Started from " + from + " v" + history[0].Number, token);
  }

  public async Task CreateAsync(string version, PromptBundle content, string label, CancellationToken token)
  {
    content.Validate();
    string checkedVersion = HumanVersion.Require(version);
    string json = JsonSerializer.Serialize(content);
    const string Live = "INSERT INTO hh_prompt_v1_live(version, content, revision) VALUES($1, $2::jsonb, 1) ON CONFLICT (version) DO NOTHING RETURNING revision";
    const string First = "INSERT INTO hh_prompt_v1_publications(version, number, label, content) SELECT $1, 1, $3, $2::jsonb WHERE NOT EXISTS(SELECT 1 FROM hh_prompt_v1_publications WHERE version = $1)";
    List<SqlResult> results = await RunAsync([Sql(Live, checkedVersion, json), Sql(First, checkedVersion, json, label)], token);
    if (results[0].Rows.Count == 0)
    {
      throw new InvalidOperationException(version + " already has prompts.");
    }
  }

  private async Task<List<SqlResult>> RunAsync(List<SqlStatement> statements, CancellationToken token)
  {
    if (!ready)
    {
      await database.RunAsync([Sql(Schema)], token);
      ready = true;
    }
    return await database.RunAsync(statements, token);
  }

  private static T Parse<T>(JsonElement value)
  {
    return JsonSerializer.Deserialize<T>(value.GetString()!) ?? throw new IOException("The prompt database returned no content.");
  }

  private static SqlStatement Sql(string query, params object?[] parameters)
  {
    return new SqlStatement(query, new List<object?>(parameters));
  }
}
