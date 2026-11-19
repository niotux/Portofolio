using System.Text.Json;

namespace Humans;

// Everything I keep lives in my own schema of Holodeck's PostgreSQL, reached through the agent API; recall is a pgvector
// query there. Only formed prose has an embedding. Received packets are transport receipts, never a search corpus.
public sealed partial class MemoryStore(Holodeck holodeck)
{
  public async Task InitializeAsync(int dimensions, CancellationToken token)
  {
    string schema = $"""
      CREATE TABLE IF NOT EXISTS received(packet_id bigint PRIMARY KEY, channel text NOT NULL, sender text NOT NULL, name text NOT NULL, note text NOT NULL, human bool NOT NULL, handled bool NOT NULL DEFAULT false);
      CREATE TABLE IF NOT EXISTS sessions(channel text PRIMARY KEY, summary text NOT NULL DEFAULT '', dropped int NOT NULL DEFAULT 0);
      CREATE TABLE IF NOT EXISTS turns(channel text NOT NULL, position int NOT NULL, packet_id bigint NOT NULL, role text NOT NULL, text text NOT NULL, thought text NOT NULL, recalled text NOT NULL DEFAULT '', PRIMARY KEY(channel, position), UNIQUE(packet_id, role));
      CREATE TABLE IF NOT EXISTS outbox(packet_id bigint PRIMARY KEY, channel text NOT NULL, text text NOT NULL, send_key text NOT NULL);
      CREATE TABLE IF NOT EXISTS forms(packet_id bigint PRIMARY KEY, channel text NOT NULL, position int NOT NULL, earlier text NOT NULL, thought text NOT NULL, exchange text NOT NULL, draft text, attempts int NOT NULL DEFAULT 0, state text NOT NULL DEFAULT 'pending');
      CREATE TABLE IF NOT EXISTS memories(id bigserial PRIMARY KEY, packet_id bigint UNIQUE NOT NULL, channel text NOT NULL, position int NOT NULL, text text NOT NULL, exchange text NOT NULL, at timestamptz NOT NULL DEFAULT now(), vector vector({dimensions}) NOT NULL, embedded_with text NOT NULL);
      CREATE INDEX IF NOT EXISTS memories_vector ON memories USING hnsw (vector vector_cosine_ops);
      UPDATE forms SET state = 'pending' WHERE state = 'working';
      """;
    await RunAsync([Sql(schema)], token);
  }

  public async Task<bool> ReceiveAsync(Delivery packet, bool human, CancellationToken token)
  {
    SqlResult result = await OneAsync(Sql("INSERT INTO received(packet_id, channel, sender, name, note, human) VALUES($1, $2, $3, $4, $5, $6) ON CONFLICT DO NOTHING", packet.PacketId, packet.ChannelId, packet.SenderId, packet.From, packet.Note, human), token);
    return result.Affected != 0;
  }

  public async Task<Delivery?> PendingAsync(CancellationToken token)
  {
    SqlResult result = await OneAsync(Sql("SELECT packet_id, channel, sender, name, note FROM received WHERE human AND NOT handled ORDER BY packet_id LIMIT 1"), token);
    if (result.Rows.Count == 0)
    {
      return null;
    }
    List<JsonElement> row = result.Rows[0];
    return new Delivery(row[0].GetInt64(), row[1].GetString()!, "", row[2].GetString()!, row[3].GetString()!, row[4].GetString()!, new List<string>());
  }

  public async Task<SavedSession> SessionAsync(string channel, CancellationToken token)
  {
    List<SqlResult> results = await RunAsync([Sql("SELECT summary, dropped FROM sessions WHERE channel = $1", channel), Sql("SELECT role, text, thought, recalled FROM turns WHERE channel = $1 AND position >= coalesce((SELECT dropped FROM sessions WHERE channel = $1), 0) ORDER BY position", channel)], token);
    string summary = "";
    int dropped = 0;
    if (results[0].Rows.Count > 0)
    {
      summary = results[0].Rows[0][0].GetString()!;
      dropped = results[0].Rows[0][1].GetInt32();
    }
    List<Turn> turns = new();
    foreach (List<JsonElement> row in results[1].Rows)
    {
      turns.Add(new Turn(row[0].GetString()!, row[1].GetString()!, row[2].GetString()!, row[3].GetString()!));
    }
    return new SavedSession(summary, dropped, turns);
  }

  public async Task SaveSummaryAsync(string channel, Conversation chat, CancellationToken token)
  {
    await OneAsync(Sql("INSERT INTO sessions(channel, summary, dropped) VALUES($1, $2, $3) ON CONFLICT(channel) DO UPDATE SET summary = $2, dropped = $3", channel, chat.Summary, chat.Dropped), token);
  }

  // One transaction: both turns, the handled receipt, the reply to send, and the exchange waiting to become a memory.
  public async Task SaveExchangeAsync(Delivery packet, int position, string input, string reply, string thought, string recalled, string earlier, string exchange, CancellationToken token)
  {
    const string InsertTurn = "INSERT INTO turns(channel, position, packet_id, role, text, thought, recalled) VALUES($1, $2, $3, $4, $5, $6, $7)";
    await RunAsync(
    [
      Sql(InsertTurn, packet.ChannelId, position, packet.PacketId, "user", input, "", ""),
      Sql(InsertTurn, packet.ChannelId, position + 1, packet.PacketId, "assistant", reply, thought, recalled),
      Sql("UPDATE received SET handled = true WHERE packet_id = $1", packet.PacketId),
      Sql("INSERT INTO outbox(packet_id, channel, text, send_key) VALUES($1, $2, $3, $4)", packet.PacketId, packet.ChannelId, reply, Guid.NewGuid().ToString("N")),
      Sql("INSERT INTO forms(packet_id, channel, position, earlier, thought, exchange) VALUES($1, $2, $3, $4, $5, $6)", packet.PacketId, packet.ChannelId, position, earlier, thought, exchange)
    ], token);
  }

  public async Task<PendingSend?> NextSendAsync(CancellationToken token)
  {
    SqlResult result = await OneAsync(Sql("SELECT packet_id, channel, text, send_key FROM outbox ORDER BY packet_id LIMIT 1"), token);
    if (result.Rows.Count == 0)
    {
      return null;
    }
    List<JsonElement> row = result.Rows[0];
    return new PendingSend(row[0].GetInt64(), row[1].GetString()!, row[2].GetString()!, row[3].GetString()!);
  }

  public async Task SentAsync(long packet, CancellationToken token)
  {
    await OneAsync(Sql("DELETE FROM outbox WHERE packet_id = $1", packet), token);
  }

  public async Task HeardWithoutReplyAsync(long packet, CancellationToken token)
  {
    await OneAsync(Sql("UPDATE received SET handled = true WHERE packet_id = $1", packet), token);
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
}
