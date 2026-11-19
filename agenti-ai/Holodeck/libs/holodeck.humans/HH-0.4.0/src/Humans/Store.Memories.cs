using System.Text.Json;

namespace Humans;

public sealed partial class Store
{
  // A stretch of history is queued once: its last entry identifies it.
  public async Task QueueFormationAsync(long first, long last, string text, CancellationToken token)
  {
    await OneAsync(Sql("INSERT INTO forms(first, last, text) VALUES($1, $2, $3) ON CONFLICT(last) DO NOTHING", first, last, text), token);
  }

  public async Task<long> LastFormedAsync(CancellationToken token)
  {
    SqlResult result = await OneAsync(Sql("SELECT coalesce(max(last), 0) FROM forms"), token);
    return result.Rows[0][0].GetInt64();
  }

  // Nearest memories by cosine distance. Memories of entries still in the live history, and those that came back
  // recently, are left out.
  public async Task<List<MemoryMatch>> NearestAsync(float[] query, string tag, long firstLive, List<long> skip, int count, CancellationToken token)
  {
    const string Search = """
      SELECT id, text, 1 - (vector <=> $1::vector) FROM memories
      WHERE embedded_with = $2 AND last < $3 AND id <> ALL($4::bigint[])
      ORDER BY vector <=> $1::vector LIMIT $5
      """;
    SqlResult result = await OneAsync(Sql(Search, query, tag, firstLive, skip, count), token);
    List<MemoryMatch> matches = new();
    foreach (List<JsonElement> row in result.Rows)
    {
      matches.Add(new MemoryMatch(row[0].GetInt64(), row[1].GetString()!, row[2].GetDouble()));
    }
    return matches;
  }

  public async Task<List<Memory>> NeedingEmbeddingAsync(string tag, CancellationToken token)
  {
    SqlResult result = await OneAsync(Sql("SELECT id, text FROM memories WHERE embedded_with <> $1 ORDER BY id", tag), token);
    List<Memory> memories = new();
    foreach (List<JsonElement> row in result.Rows)
    {
      memories.Add(new Memory(row[0].GetInt64(), row[1].GetString()!));
    }
    return memories;
  }

  public async Task ReembedAsync(long id, float[] vector, string tag, CancellationToken token)
  {
    await OneAsync(Sql("UPDATE memories SET vector = $2::vector, embedded_with = $3 WHERE id = $1", id, vector, tag), token);
  }

  // One worker owns this queue. Reclaim its working stretch after a lost response before taking new work.
  public async Task<Formation?> ClaimFormationAsync(int maxAttempts, CancellationToken token)
  {
    const string Claim = """
      UPDATE forms SET attempts = attempts + CASE WHEN state = 'working' THEN 0 ELSE 1 END, state = 'working'
      WHERE id = (SELECT id FROM forms WHERE state = 'working' OR (state = 'pending' AND attempts < $1) ORDER BY id LIMIT 1)
      RETURNING id, first, last, text, draft, attempts
      """;
    SqlResult result = await OneAsync(Sql(Claim, maxAttempts), token);
    if (result.Rows.Count == 0)
    {
      return null;
    }
    List<JsonElement> row = result.Rows[0];
    string? draft = row[4].ValueKind == JsonValueKind.Null ? null : row[4].GetString();
    return new Formation(row[0].GetInt64(), row[1].GetInt64(), row[2].GetInt64(), row[3].GetString()!, draft, row[5].GetInt32());
  }

  public async Task SaveDraftAsync(long form, string draft, CancellationToken token)
  {
    await OneAsync(Sql("UPDATE forms SET draft = $2 WHERE id = $1", form, draft), token);
  }

  public async Task KeepAsync(Formation form, List<string> texts, List<float[]> vectors, string tag, CancellationToken token)
  {
    List<SqlStatement> statements = new();
    for (int i = 0; i < texts.Count; i++)
    {
      // Several memories can come from one stretch; only the first carries the form's identity.
      object? source = i == 0 ? form.Id : null;
      statements.Add(Sql("INSERT INTO memories(form_id, first, last, text, vector, embedded_with) VALUES($1, $2, $3, $4, $5::vector, $6) ON CONFLICT(form_id) DO NOTHING", source, form.First, form.Last, texts[i], vectors[i], tag));
    }
    statements.Add(Sql("UPDATE forms SET state = 'done' WHERE id = $1", form.Id));
    await RunAsync(statements, token);
  }

  public async Task FormationFailedAsync(Formation form, int maximum, CancellationToken token)
  {
    await OneAsync(Sql("UPDATE forms SET state = $2 WHERE id = $1", form.Id, form.Attempts >= maximum ? "failed" : "pending"), token);
  }

  public async Task<int> MemoryCountAsync(CancellationToken token)
  {
    SqlResult result = await OneAsync(Sql("SELECT count(*) FROM memories"), token);
    return (int)result.Rows[0][0].GetInt64();
  }

  public async Task<Prepared?> PreparedAsync(string source, CancellationToken token)
  {
    SqlResult result = await OneAsync(Sql("SELECT source, portrait FROM prepared WHERE source = $1", source), token);
    return result.Rows.Count == 0 ? null : new Prepared(result.Rows[0][0].GetString()!, result.Rows[0][1].GetString()!);
  }

  // A preparation is stored only once complete, so an interrupted one is simply done again.
  public async Task SavePreparedAsync(Prepared prepared, CancellationToken token)
  {
    await OneAsync(Sql("INSERT INTO prepared(source, portrait) VALUES($1, $2) ON CONFLICT(source) DO UPDATE SET portrait = $2, at = now()", prepared.Source, prepared.Portrait), token);
  }
}
