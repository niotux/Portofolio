using System.Text.Json;

namespace CreativeStudio;

// Durable private state accessed only through Holodeck's agent database API.
public sealed partial class CreativeStore(HolodeckClient holodeck)
{
  private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

  public async Task InitializeAsync(int dimensions, CancellationToken token)
  {
    if (dimensions < 1)
    {
      throw new ArgumentOutOfRangeException(nameof(dimensions));
    }

    string schema = $"""
      CREATE TABLE IF NOT EXISTS received(packet_id bigint PRIMARY KEY, channel text NOT NULL, sender text NOT NULL, name text NOT NULL, note text NOT NULL, files jsonb NOT NULL, handled bool NOT NULL DEFAULT false);
      CREATE TABLE IF NOT EXISTS assets(channel text NOT NULL, path text NOT NULL, description text NOT NULL, source_packet_id bigint NOT NULL, position bigserial NOT NULL, PRIMARY KEY(channel, path));
      INSERT INTO assets(channel, path, description, source_packet_id) SELECT channel, jsonb_array_elements_text(files), note, packet_id FROM received ON CONFLICT DO NOTHING;
      CREATE TABLE IF NOT EXISTS turns(turn_key text PRIMARY KEY, channel text NOT NULL, role text NOT NULL, text text NOT NULL, position bigserial NOT NULL);
      CREATE INDEX IF NOT EXISTS turns_channel_position ON turns(channel, position);
      CREATE TABLE IF NOT EXISTS briefs(channel text PRIMARY KEY, prose text NOT NULL, through_position bigint NOT NULL DEFAULT 0);
      CREATE TABLE IF NOT EXISTS outbox(send_key text PRIMARY KEY, channel text NOT NULL, text text NOT NULL, files jsonb NOT NULL, position bigserial NOT NULL);
      CREATE TABLE IF NOT EXISTS dispatched(send_key text PRIMARY KEY);
      CREATE TABLE IF NOT EXISTS notebook(id text PRIMARY KEY, channel text NOT NULL, title text NOT NULL, prose text NOT NULL, files jsonb NOT NULL, source_packet_id bigint NOT NULL, revision text NOT NULL, embedding vector({dimensions}) NOT NULL, embedded_with text NOT NULL, updated_at timestamptz NOT NULL DEFAULT now());
      CREATE INDEX IF NOT EXISTS notebook_embedding ON notebook USING hnsw (embedding vector_cosine_ops);
      CREATE TABLE IF NOT EXISTS notebook_revisions(note_id text NOT NULL, revision text NOT NULL, title text NOT NULL, prose text NOT NULL, files jsonb NOT NULL, source_packet_id bigint NOT NULL, created_at timestamptz NOT NULL DEFAULT now(), PRIMARY KEY(note_id, revision));
      CREATE TABLE IF NOT EXISTS prompt_revisions(prompt_id text NOT NULL, revision text NOT NULL, content text NOT NULL, origin text NOT NULL, created_at timestamptz NOT NULL DEFAULT now(), PRIMARY KEY(prompt_id, revision));
      CREATE TABLE IF NOT EXISTS prompt_heads(prompt_id text PRIMARY KEY, default_revision text NOT NULL, active_revision text NOT NULL, follow_default bool NOT NULL DEFAULT true);
      ALTER TABLE prompt_heads ADD COLUMN IF NOT EXISTS follow_default bool NOT NULL DEFAULT true;
      CREATE TABLE IF NOT EXISTS generations(id text PRIMARY KEY, packet_id bigint NOT NULL, channel text NOT NULL, request_json jsonb NOT NULL, state text NOT NULL, external_id text, error text, created_at timestamptz NOT NULL DEFAULT now(), updated_at timestamptz NOT NULL DEFAULT now());
      """;
    await RunAsync([Sql(schema)], token);
  }

  public async Task<bool> ReceiveAsync(StoredReceipt receipt, CancellationToken token)
  {
    string files = JsonSerializer.Serialize(receipt.Files, Json);
    List<SqlStatement> statements = [Sql("INSERT INTO received(packet_id, channel, sender, name, note, files) VALUES($1, $2, $3, $4, $5, $6::jsonb) ON CONFLICT DO NOTHING", receipt.PacketId, receipt.ChannelId, receipt.SenderId, receipt.From, receipt.Note, files)];
    foreach (string file in receipt.Files)
    {
      statements.Add(Sql("INSERT INTO assets(channel, path, description, source_packet_id) VALUES($1, $2, $3, $4) ON CONFLICT DO NOTHING", receipt.ChannelId, file, receipt.Note, receipt.PacketId));
    }

    List<SqlResult> result = await RunAsync(statements, token);
    return result[0].Affected != 0;
  }

  public async Task<StoredReceipt?> PendingReceiptAsync(CancellationToken token)
  {
    SqlResult result = await OneAsync(Sql("SELECT packet_id, channel, sender, name, note, files::text FROM received WHERE NOT handled ORDER BY packet_id LIMIT 1"), token);
    if (result.Rows.Count == 0)
    {
      return null;
    }

    List<JsonElement> row = result.Rows[0];
    return new StoredReceipt(row[0].GetInt64(), Text(row[1]), Text(row[2]), Text(row[3]), Text(row[4]), Files(row[5]));
  }

  public async Task CompleteTurnAsync(StoredReceipt receipt, string reply, List<string> files, CancellationToken token)
  {
    List<SqlStatement> statements = [Sql("SELECT packet_id FROM received WHERE packet_id = $1 FOR UPDATE", receipt.PacketId)];
    AppendTurnCompletion(statements, receipt, reply, files);
    List<SqlResult> result = await RunAsync(statements, token);
    if (result[0].Rows.Count == 0)
    {
      throw new KeyNotFoundException("That message receipt does not exist.");
    }
  }

  private static void AppendTurnCompletion(List<SqlStatement> statements, StoredReceipt receipt, string reply, List<string> files)
  {
    string key = "reply." + receipt.PacketId;
    statements.Add(Sql("INSERT INTO turns(turn_key, channel, role, text) SELECT $2, channel, 'user', note FROM received WHERE packet_id = $1 AND NOT handled ON CONFLICT DO NOTHING", receipt.PacketId, "user." + receipt.PacketId));
    if (reply.Length > 0 || files.Count > 0)
    {
      statements.Add(Sql("INSERT INTO turns(turn_key, channel, role, text) SELECT $2, channel, 'assistant', $3 FROM received WHERE packet_id = $1 AND NOT handled ON CONFLICT DO NOTHING", receipt.PacketId, key, reply));
      statements.Add(Sql("INSERT INTO outbox(send_key, channel, text, files) SELECT $2, channel, $3, $4::jsonb FROM received WHERE packet_id = $1 AND NOT handled ON CONFLICT DO NOTHING", receipt.PacketId, key, reply, JsonSerializer.Serialize(files, Json)));
    }

    statements.Add(Sql("UPDATE received SET handled = true WHERE packet_id = $1", receipt.PacketId));
  }

  public async Task<List<StoredTurn>> RecentTurnsAsync(string channel, int limit, CancellationToken token)
  {
    RequireLimit(limit);
    SqlResult result = await OneAsync(Sql("SELECT role, text, position FROM (SELECT role, text, position FROM turns WHERE channel = $1 AND position > coalesce((SELECT through_position FROM briefs WHERE channel = $1), 0) ORDER BY position DESC LIMIT $2) recent ORDER BY position", channel, limit), token);
    List<StoredTurn> turns = new();
    foreach (List<JsonElement> row in result.Rows)
    {
      turns.Add(new StoredTurn(Text(row[0]), Text(row[1]), row[2].GetInt64()));
    }

    return turns;
  }

  public async Task<List<StoredTurn>> OldestTurnsAsync(string channel, int limit, CancellationToken token)
  {
    RequireLimit(limit);
    SqlResult result = await OneAsync(Sql("SELECT role, text, position FROM turns WHERE channel = $1 AND position > coalesce((SELECT through_position FROM briefs WHERE channel = $1), 0) ORDER BY position LIMIT $2", channel, limit), token);
    List<StoredTurn> turns = new();
    foreach (List<JsonElement> row in result.Rows)
    {
      turns.Add(new StoredTurn(Text(row[0]), Text(row[1]), row[2].GetInt64()));
    }

    return turns;
  }

  public async Task<string> BriefAsync(string channel, CancellationToken token)
  {
    SqlResult result = await OneAsync(Sql("SELECT prose FROM briefs WHERE channel = $1", channel), token);
    return result.Rows.Count == 0 ? "" : Text(result.Rows[0][0]);
  }

  // The caller names the final turn actually summarized, so unseen turns remain available.
  public async Task SaveBriefAsync(string channel, string prose, long throughPosition, CancellationToken token)
  {
    await OneAsync(Sql("INSERT INTO briefs(channel, prose, through_position) VALUES($1, $2, $3) ON CONFLICT(channel) DO UPDATE SET prose = excluded.prose, through_position = excluded.through_position WHERE briefs.through_position <= excluded.through_position", channel, prose, throughPosition), token);
  }

  public async Task<StoredSend?> NextSendAsync(CancellationToken token)
  {
    SqlResult result = await OneAsync(Sql("SELECT send_key, channel, text, files::text FROM outbox ORDER BY position LIMIT 1"), token);
    if (result.Rows.Count == 0)
    {
      return null;
    }

    List<JsonElement> row = result.Rows[0];
    return new StoredSend(Text(row[0]), Text(row[1]), Text(row[2]), Files(row[3]));
  }

  public async Task QueueSendAsync(StoredSend send, CancellationToken token)
  {
    await OneAsync(Sql("INSERT INTO outbox(send_key, channel, text, files) SELECT $1, $2, $3, $4::jsonb WHERE NOT EXISTS(SELECT 1 FROM dispatched WHERE send_key = $1) ON CONFLICT DO NOTHING", send.Key, send.Channel, send.Text, JsonSerializer.Serialize(send.Files, Json)), token);
  }

  public async Task SentAsync(string key, CancellationToken token)
  {
    await RunAsync([
      Sql("INSERT INTO dispatched(send_key) SELECT send_key FROM outbox WHERE send_key = $1 ON CONFLICT DO NOTHING", key),
      Sql("DELETE FROM outbox WHERE send_key = $1", key)
    ], token);
  }

  private async Task<SqlResult> OneAsync(SqlStatement statement, CancellationToken token)
  {
    List<SqlResult> results = await RunAsync([statement], token);
    return results[0];
  }

  private Task<List<SqlResult>> RunAsync(List<SqlStatement> statements, CancellationToken token)
  {
    return holodeck.DatabaseAsync(statements, token);
  }

  private static SqlStatement Sql(string sql, params object?[] parameters)
  {
    return new SqlStatement(sql, new List<object?>(parameters));
  }

  private static string Text(JsonElement value) => value.GetString() ?? "";

  private static List<string> Files(JsonElement value)
  {
    return JsonSerializer.Deserialize<List<string>>(Text(value), Json) ?? throw new IOException("A saved file list is empty.");
  }

  private static void RequireLimit(int limit)
  {
    if (limit < 1)
    {
      throw new ArgumentOutOfRangeException(nameof(limit));
    }
  }
}
