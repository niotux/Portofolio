using System.Text.Json;

namespace Humans;

public sealed partial class MemoryStore
{
  // Nearest memories by cosine distance, found by pgvector. Memories whose exchange is still on the page, and those
  // that came back recently, are left out.
  public async Task<List<MemoryMatch>> NearestAsync(float[] query, string tag, string channel, int firstKept, List<long> skip, int count, CancellationToken token)
  {
    const string Search = """
      SELECT id, text, 1 - (vector <=> $1::vector) FROM memories
      WHERE embedded_with = $2 AND NOT (channel = $3 AND position >= $4) AND id <> ALL($5::bigint[])
      ORDER BY vector <=> $1::vector LIMIT $6
      """;
    SqlResult result = await OneAsync(Sql(Search, query, tag, channel, firstKept, skip, count), token);
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

  // What I remember from earlier chats with this sender, oldest first: knowing who is talking is seeing them, not a search.
  public async Task<List<string>> KnownByAsync(string sender, string channel, int most, CancellationToken token)
  {
    const string Known = "SELECT text FROM (SELECT m.id, m.text FROM memories m JOIN received r ON r.packet_id = m.packet_id WHERE r.sender = $1 AND m.channel <> $2 ORDER BY m.id DESC LIMIT $3) recent ORDER BY id";
    SqlResult result = await OneAsync(Sql(Known, sender, channel, most), token);
    return Texts(result);
  }

  // Backstory lives in long-term memory like anything else; seeds use negative packet ids, in order.
  public async Task<List<string>> SeedsAsync(CancellationToken token)
  {
    SqlResult result = await OneAsync(Sql("SELECT text FROM memories WHERE packet_id < 0 ORDER BY packet_id DESC"), token);
    return Texts(result);
  }

  public async Task ReplaceSeedsAsync(List<string> texts, List<float[]> vectors, string tag, CancellationToken token)
  {
    List<SqlStatement> statements = [Sql("DELETE FROM memories WHERE packet_id < 0")];
    for (int i = 0; i < texts.Count; i++)
    {
      statements.Add(Sql("INSERT INTO memories(packet_id, channel, position, text, exchange, vector, embedded_with) VALUES($1, '', 0, $2, '', $3::vector, $4)", -(i + 1), texts[i], vectors[i], tag));
    }
    await RunAsync(statements, token);
  }

  // One worker owns this queue. Reclaim its working exchange after a lost response before taking new work.
  public async Task<Formation?> ClaimFormationAsync(int maxAttempts, CancellationToken token)
  {
    const string Claim = """
      UPDATE forms SET attempts = attempts + CASE WHEN state = 'working' THEN 0 ELSE 1 END, state = 'working'
      WHERE packet_id = (SELECT packet_id FROM forms WHERE state = 'working' OR (state = 'pending' AND attempts < $1) ORDER BY packet_id LIMIT 1)
      RETURNING packet_id, channel, position, earlier, thought, exchange, draft, attempts
      """;
    SqlResult result = await OneAsync(Sql(Claim, maxAttempts), token);
    if (result.Rows.Count == 0)
    {
      return null;
    }
    List<JsonElement> row = result.Rows[0];
    string? draft = row[6].ValueKind == JsonValueKind.Null ? null : row[6].GetString();
    return new Formation(row[0].GetInt64(), row[1].GetString()!, row[2].GetInt32(), row[3].GetString()!, row[4].GetString()!, row[5].GetString()!, draft, row[7].GetInt32());
  }

  public async Task SaveDraftAsync(long packet, string draft, CancellationToken token)
  {
    await OneAsync(Sql("UPDATE forms SET draft = $2 WHERE packet_id = $1", packet, draft), token);
  }

  public async Task KeepAsync(Formation form, string text, float[]? vector, string tag, CancellationToken token)
  {
    List<SqlStatement> statements = new();
    if (vector is not null)
    {
      statements.Add(Sql("INSERT INTO memories(packet_id, channel, position, text, exchange, vector, embedded_with) VALUES($1, $2, $3, $4, $5, $6::vector, $7) ON CONFLICT(packet_id) DO NOTHING", form.PacketId, form.Channel, form.Position, text, form.Exchange, vector, tag));
    }
    statements.Add(Sql("UPDATE forms SET state = 'done' WHERE packet_id = $1", form.PacketId));
    await RunAsync(statements, token);
  }

  public async Task FormationFailedAsync(Formation form, int maximum, CancellationToken token)
  {
    await OneAsync(Sql("UPDATE forms SET state = $2 WHERE packet_id = $1", form.PacketId, form.Attempts >= maximum ? "failed" : "pending"), token);
  }

  private static List<string> Texts(SqlResult result)
  {
    List<string> texts = new();
    foreach (List<JsonElement> row in result.Rows)
    {
      texts.Add(row[0].GetString()!);
    }
    return texts;
  }
}
