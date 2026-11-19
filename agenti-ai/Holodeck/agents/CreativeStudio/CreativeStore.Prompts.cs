using System.Text.Json;

namespace CreativeStudio;

public sealed partial class CreativeStore
{
  public async Task SeedPromptAsync(string id, string content, CancellationToken token)
  {
    string revision = Hash(content);
    await RunAsync([
      Sql("INSERT INTO prompt_revisions(prompt_id, revision, content, origin) VALUES($1, $2, $3, 'library') ON CONFLICT DO NOTHING", id, revision, content),
      Sql("INSERT INTO prompt_heads(prompt_id, default_revision, active_revision) VALUES($1, $2, $2) ON CONFLICT(prompt_id) DO UPDATE SET active_revision = CASE WHEN prompt_heads.follow_default THEN excluded.default_revision ELSE prompt_heads.active_revision END, default_revision = excluded.default_revision", id, revision)
    ], token);
  }

  public async Task<PromptRevision> ActivePromptAsync(string id, CancellationToken token)
  {
    SqlResult result = await OneAsync(Sql("SELECT r.prompt_id, r.revision, r.content, r.origin, true FROM prompt_revisions r JOIN prompt_heads h ON h.prompt_id = r.prompt_id AND h.active_revision = r.revision WHERE r.prompt_id = $1", id), token);
    if (result.Rows.Count == 0)
    {
      throw new KeyNotFoundException("That prompt is not registered.");
    }

    return Prompt(result.Rows[0]);
  }

  public async Task<PromptRevision> SavePromptAsync(string id, string content, CancellationToken token)
  {
    await ActivePromptAsync(id, token);
    string revision = Hash(content);
    await OneAsync(Sql("INSERT INTO prompt_revisions(prompt_id, revision, content, origin) VALUES($1, $2, $3, 'draft') ON CONFLICT DO NOTHING", id, revision, content), token);
    SqlResult result = await OneAsync(Sql("SELECT r.prompt_id, r.revision, r.content, r.origin, r.revision = h.active_revision FROM prompt_revisions r JOIN prompt_heads h ON h.prompt_id = r.prompt_id WHERE r.prompt_id = $1 AND r.revision = $2", id, revision), token);
    return Prompt(result.Rows[0]);
  }

  public async Task SelectPromptAsync(string id, string revision, CancellationToken token)
  {
    SqlResult result = await OneAsync(Sql("UPDATE prompt_heads SET active_revision = $2, follow_default = false WHERE prompt_id = $1 AND EXISTS(SELECT 1 FROM prompt_revisions WHERE prompt_id = $1 AND revision = $2)", id, revision), token);
    if (result.Affected == 0)
    {
      throw new KeyNotFoundException("That prompt revision does not exist.");
    }
  }

  public async Task<List<PromptRevision>> PromptVersionsAsync(string id, CancellationToken token)
  {
    SqlResult result = await OneAsync(Sql("SELECT r.prompt_id, r.revision, r.content, r.origin, r.revision = h.active_revision FROM prompt_revisions r JOIN prompt_heads h ON h.prompt_id = r.prompt_id WHERE r.prompt_id = $1 ORDER BY r.created_at, r.revision", id), token);
    List<PromptRevision> revisions = new();
    foreach (List<JsonElement> row in result.Rows)
    {
      revisions.Add(Prompt(row));
    }

    return revisions;
  }

  private static PromptRevision Prompt(List<JsonElement> row)
  {
    return new PromptRevision(Text(row[0]), Text(row[1]), Text(row[2]), Text(row[3]), row[4].GetBoolean());
  }
}
