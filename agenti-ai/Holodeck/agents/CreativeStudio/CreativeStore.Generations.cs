using System.Text.Json;

namespace CreativeStudio;

public sealed partial class CreativeStore
{
  public async Task<StoredGeneration> SaveGenerationAsync(StoredGeneration generation, CancellationToken token)
  {
    await OneAsync(Sql("INSERT INTO generations(id, packet_id, channel, request_json, state, external_id, error) VALUES($1, $2, $3, $4::jsonb, $5, $6, $7) ON CONFLICT DO NOTHING", generation.Id, generation.PacketId, generation.Channel, generation.RequestJson, generation.State, generation.ExternalId, generation.Error), token);
    StoredGeneration saved = await GenerationAsync(generation.Id, token) ?? throw new IOException("The generation request was not saved.");
    RequireSameGeneration(saved, generation);

    return saved;
  }

  // Scheduling and acknowledging the input share one transaction: a restart sees both or neither.
  public async Task ScheduleGenerationAsync(StoredGeneration generation, StoredReceipt receipt, string reply, CancellationToken token)
  {
    if (generation.PacketId != receipt.PacketId || generation.Channel != receipt.ChannelId)
    {
      throw new ArgumentException("The generation must belong to the message being handled.");
    }

    StoredGeneration? existing = await GenerationAsync(generation.Id, token);
    if (existing is not null)
    {
      RequireSameGeneration(existing, generation);
    }

    List<SqlStatement> statements = [
      Sql("SELECT packet_id FROM received WHERE packet_id = $1 FOR UPDATE", receipt.PacketId),
      Sql("INSERT INTO generations(id, packet_id, channel, request_json, state, external_id, error) SELECT $1, $2, $3, $4::jsonb, $5, $6, $7 FROM received WHERE packet_id = $2 AND NOT handled ON CONFLICT DO NOTHING", generation.Id, generation.PacketId, generation.Channel, generation.RequestJson, generation.State, generation.ExternalId, generation.Error)
    ];
    AppendTurnCompletion(statements, receipt, reply, new List<string>());
    List<SqlResult> results = await RunAsync(statements, token);
    if (results[0].Rows.Count == 0)
    {
      throw new KeyNotFoundException("That message receipt does not exist.");
    }
  }

  public async Task<StoredGeneration?> GenerationAsync(string id, CancellationToken token)
  {
    SqlResult result = await OneAsync(Sql("SELECT id, packet_id, channel, request_json::text, state, external_id, error FROM generations WHERE id = $1", id), token);
    return result.Rows.Count == 0 ? null : Generation(result.Rows[0]);
  }

  public async Task<StoredGeneration?> PendingGenerationAsync(CancellationToken token)
  {
    SqlResult result = await OneAsync(Sql("SELECT id, packet_id, channel, request_json::text, state, external_id, error FROM generations WHERE state NOT IN ('completed', 'failed', 'needs-review') ORDER BY created_at, id LIMIT 1"), token);
    return result.Rows.Count == 0 ? null : Generation(result.Rows[0]);
  }

  public async Task<List<StoredGeneration>> ListGenerationsAsync(int limit, CancellationToken token)
  {
    RequireLimit(limit);
    SqlResult result = await OneAsync(Sql("SELECT id, packet_id, channel, request_json::text, state, external_id, error FROM generations ORDER BY created_at DESC, id LIMIT $1", limit), token);
    List<StoredGeneration> generations = new();
    foreach (List<JsonElement> row in result.Rows)
    {
      generations.Add(Generation(row));
    }

    return generations;
  }

  public async Task UpdateGenerationAsync(string id, string state, string? externalId, string? error, CancellationToken token)
  {
    SqlResult result = await OneAsync(Sql("UPDATE generations SET state = $2, external_id = coalesce(external_id, $3::text), error = $4, updated_at = now() WHERE id = $1 AND state NOT IN ('completed', 'failed') AND ($3::text IS NULL OR external_id IS NULL OR external_id = $3::text)", id, state, externalId, error), token);
    if (result.Affected == 0)
    {
      StoredGeneration saved = await GenerationAsync(id, token) ?? throw new KeyNotFoundException("That generation request does not exist.");
      if (externalId is not null && saved.ExternalId is not null && saved.ExternalId != externalId)
      {
        throw new IOException("That request already belongs to a different renderer job.");
      }
    }
  }

  public async Task CompleteGenerationAsync(string id, string text, List<string> files, CancellationToken token)
  {
    string key = "generation." + id;
    List<SqlStatement> statements = new();
    AppendGenerationFiles(statements, id, files);
    statements.Add(Sql("INSERT INTO outbox(send_key, channel, text, files) SELECT $2, channel, $3, $4::jsonb FROM generations WHERE id = $1 AND state NOT IN ('completed', 'failed') ON CONFLICT DO NOTHING", id, key, text, JsonSerializer.Serialize(files, Json)));
    statements.Add(Sql("INSERT INTO turns(turn_key, channel, role, text) SELECT $2, channel, 'assistant', $3 FROM generations WHERE id = $1 AND state NOT IN ('completed', 'failed') ON CONFLICT DO NOTHING", id, key, text));
    statements.Add(Sql("UPDATE generations SET state = 'completed', error = NULL, updated_at = now() WHERE id = $1 AND state <> 'failed'", id));
    await RunAsync(statements, token);
  }

  public async Task ReviewGenerationAsync(string id, string text, string error, CancellationToken token)
  {
    string key = "uncertain." + id;
    await RunAsync([
      Sql("INSERT INTO outbox(send_key, channel, text, files) SELECT $2, channel, $3, '[]'::jsonb FROM generations WHERE id = $1 AND state NOT IN ('completed', 'failed', 'needs-review') AND NOT EXISTS(SELECT 1 FROM dispatched WHERE send_key = $2) ON CONFLICT DO NOTHING", id, key, text),
      Sql("UPDATE generations SET state = 'needs-review', error = $2, updated_at = now() WHERE id = $1 AND state NOT IN ('completed', 'failed')", id, error)
    ], token);
  }

  public async Task FailGenerationAsync(string id, string text, string error, CancellationToken token)
  {
    string key = "failed." + id;
    await RunAsync([
      Sql("INSERT INTO outbox(send_key, channel, text, files) SELECT $2, channel, $3, '[]'::jsonb FROM generations WHERE id = $1 AND state NOT IN ('completed', 'failed') AND NOT EXISTS(SELECT 1 FROM dispatched WHERE send_key = $2) ON CONFLICT DO NOTHING", id, key, text),
      Sql("UPDATE generations SET state = 'failed', error = $2, updated_at = now() WHERE id = $1 AND state NOT IN ('completed', 'failed')", id, error)
    ], token);
  }

  // Explicit resumption keeps the original request and external identity for reconciliation.
  public async Task ResumeGenerationAsync(string id, CancellationToken token)
  {
    SqlResult result = await OneAsync(Sql("UPDATE generations SET state = CASE WHEN external_id IS NULL THEN 'prepared' ELSE 'submitted' END, error = NULL, updated_at = now() WHERE id = $1 AND state = 'needs-review'", id), token);
    if (result.Affected == 0)
    {
      throw new InvalidOperationException("That generation is not waiting for review.");
    }
  }

  private static void RequireSameGeneration(StoredGeneration saved, StoredGeneration requested)
  {
    bool sameRequest = JsonEquivalent(saved.RequestJson, requested.RequestJson);
    if (saved.PacketId != requested.PacketId || saved.Channel != requested.Channel || !sameRequest)
    {
      throw new IOException("That generation identity already belongs to a different request.");
    }
  }

  private static StoredGeneration Generation(List<JsonElement> row)
  {
    return new StoredGeneration(Text(row[0]), row[1].GetInt64(), Text(row[2]), Text(row[3]), Text(row[4]), row[5].ValueKind == JsonValueKind.Null ? null : Text(row[5]), row[6].ValueKind == JsonValueKind.Null ? null : Text(row[6]));
  }

  private static bool JsonEquivalent(string first, string second)
  {
    using JsonDocument left = JsonDocument.Parse(first);
    using JsonDocument right = JsonDocument.Parse(second);
    return JsonElement.DeepEquals(left.RootElement, right.RootElement);
  }
}
