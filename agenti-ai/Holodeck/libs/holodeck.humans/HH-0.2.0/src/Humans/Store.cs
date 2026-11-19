using System.Text.Json;

namespace Humans;

// Everything one person keeps lives in its own schema of Holodeck's PostgreSQL. The inbox holds what arrived, the
// stream is the one continuing private history, the outbox what was decided to be sent. Only formed prose memories are
// embedded; packets and history entries are never a search corpus.
public sealed partial class Store(Holodeck holodeck)
{
  private const string EntryColumns = "id, kind, channel, speaker, text, at";

  public async Task InitializeAsync(int dimensions, CancellationToken token)
  {
    string schema = $"""
      CREATE TABLE IF NOT EXISTS inbox(packet_id bigint PRIMARY KEY, channel text NOT NULL, sender text NOT NULL, speaker text NOT NULL, note text NOT NULL, tag bool NOT NULL, at timestamptz NOT NULL DEFAULT now(), buzzed bool NOT NULL DEFAULT false, seen bool NOT NULL DEFAULT false);
      CREATE TABLE IF NOT EXISTS stream(id bigserial PRIMARY KEY, kind text NOT NULL, channel text NOT NULL DEFAULT '', speaker text NOT NULL DEFAULT '', text text NOT NULL, at timestamptz NOT NULL DEFAULT now());
      CREATE TABLE IF NOT EXISTS summaries(upto bigint PRIMARY KEY, text text NOT NULL, at timestamptz NOT NULL DEFAULT now());
      CREATE TABLE IF NOT EXISTS state(key text PRIMARY KEY, value text NOT NULL);
      CREATE TABLE IF NOT EXISTS outbox(id bigint PRIMARY KEY, channel text NOT NULL, text text NOT NULL, send_key text NOT NULL);
      CREATE TABLE IF NOT EXISTS forms(id bigserial PRIMARY KEY, first bigint NOT NULL, last bigint NOT NULL UNIQUE, text text NOT NULL, draft text, attempts int NOT NULL DEFAULT 0, state text NOT NULL DEFAULT 'pending');
      CREATE TABLE IF NOT EXISTS memories(id bigserial PRIMARY KEY, form_id bigint UNIQUE, first bigint NOT NULL, last bigint NOT NULL, text text NOT NULL, at timestamptz NOT NULL DEFAULT now(), vector vector({dimensions}) NOT NULL, embedded_with text NOT NULL);
      CREATE INDEX IF NOT EXISTS memories_vector ON memories USING hnsw (vector vector_cosine_ops);
      CREATE TABLE IF NOT EXISTS prepared(source text PRIMARY KEY, portrait text NOT NULL, at timestamptz NOT NULL DEFAULT now());
      UPDATE forms SET state = 'pending' WHERE state = 'working';
      """;
    await RunAsync([Sql(schema)], token);
  }

  // Receipt is durable before the following /next acknowledges it; a redelivery changes nothing.
  // The sender is kept as a member, so the page can show whatever name the person later gives them.
  public async Task<bool> ReceiveAsync(Delivery packet, bool tag, CancellationToken token)
  {
    SqlResult result = await OneAsync(Sql("INSERT INTO inbox(packet_id, channel, sender, speaker, note, tag) VALUES($1, $2, $3, $4, $5, $6) ON CONFLICT DO NOTHING", packet.PacketId, packet.ChannelId, packet.SenderId, packet.From, packet.Note, tag), token);
    return result.Affected != 0;
  }

  // Channels with a tag the person has not yet been buzzed about. Buzzing tells only the channel.
  public async Task<List<string>> TakeBuzzesAsync(CancellationToken token)
  {
    SqlResult result = await OneAsync(Sql("UPDATE inbox SET buzzed = true WHERE tag AND NOT buzzed AND NOT seen RETURNING channel"), token);
    List<string> channels = new();
    foreach (List<JsonElement> row in result.Rows)
    {
      string channel = row[0].GetString()!;
      if (!channels.Contains(channel))
      {
        channels.Add(channel);
      }
    }
    return channels;
  }

  // Reading a conversation marks what it showed as seen. Older unseen messages beyond the shown ones are counted.
  public async Task<List<Received>> ReadAsync(string channel, int most, CancellationToken token)
  {
    const string Unseen = "SELECT packet_id, channel, sender, note, at FROM inbox WHERE channel = $1 AND NOT seen ORDER BY packet_id";
    List<SqlResult> results = await RunAsync([Sql(Unseen, channel), Sql("UPDATE inbox SET seen = true, buzzed = true WHERE channel = $1 AND NOT seen", channel)], token);
    List<Received> messages = new();
    foreach (List<JsonElement> row in results[0].Rows)
    {
      messages.Add(new Received(row[0].GetInt64(), row[1].GetString()!, row[2].GetString()!, row[3].GetString()!, row[4].GetDateTimeOffset()));
    }
    if (messages.Count > most)
    {
      messages.RemoveRange(0, messages.Count - most);
    }
    return messages;
  }

  public async Task<int> UnseenCountAsync(string channel, CancellationToken token)
  {
    SqlResult result = await OneAsync(Sql("SELECT count(*) FROM inbox WHERE channel = $1 AND NOT seen", channel), token);
    return (int)result.Rows[0][0].GetInt64();
  }

  // What the phone shows when checked: per channel, how many unseen and from whom. No message content.
  public async Task<List<Unread>> UnreadAsync(CancellationToken token)
  {
    SqlResult result = await OneAsync(Sql("SELECT channel, count(*), array_agg(DISTINCT sender) FROM inbox WHERE NOT seen GROUP BY channel ORDER BY min(packet_id)"), token);
    List<Unread> unread = new();
    foreach (List<JsonElement> row in result.Rows)
    {
      List<string> senders = new();
      foreach (JsonElement sender in row[2].EnumerateArray())
      {
        senders.Add(sender.GetString()!);
      }
      unread.Add(new Unread(row[0].GetString()!, (int)row[1].GetInt64(), senders));
    }
    return unread;
  }

  public async Task<Entry> AppendAsync(string kind, string channel, string speaker, string text, CancellationToken token)
  {
    SqlResult result = await OneAsync(Sql($"INSERT INTO stream(kind, channel, speaker, text) VALUES($1, $2, $3, $4) RETURNING {EntryColumns}", kind, channel, speaker, text), token);
    return ReadEntry(result.Rows[0]);
  }

  // What I write and its delivery are decided together, so a restart neither loses nor repeats it.
  public async Task<Entry> SayAsync(string channel, string speaker, string text, CancellationToken token)
  {
    const string Insert = $"WITH said AS (INSERT INTO stream(kind, channel, speaker, text) VALUES($1, $2, $3, $4) RETURNING {EntryColumns}), queued AS (INSERT INTO outbox(id, channel, text, send_key) SELECT id, channel, text, $5 FROM said) SELECT {EntryColumns} FROM said";
    SqlResult result = await OneAsync(Sql(Insert, EntryKinds.Said, channel, speaker, text, Guid.NewGuid().ToString("N")), token);
    return ReadEntry(result.Rows[0]);
  }

  public async Task<PendingSend?> NextSendAsync(CancellationToken token)
  {
    SqlResult result = await OneAsync(Sql("SELECT id, channel, text, send_key FROM outbox ORDER BY id LIMIT 1"), token);
    if (result.Rows.Count == 0)
    {
      return null;
    }
    List<JsonElement> row = result.Rows[0];
    return new PendingSend(row[0].GetInt64(), row[1].GetString()!, row[2].GetString()!, row[3].GetString()!);
  }

  public async Task SentAsync(long id, CancellationToken token)
  {
    await OneAsync(Sql("DELETE FROM outbox WHERE id = $1", id), token);
  }

  // The latest summary and every entry after it: the history the person currently lives in.
  public async Task<History> HistoryAsync(CancellationToken token)
  {
    List<SqlResult> results = await RunAsync([Sql("SELECT upto, text FROM summaries ORDER BY upto DESC LIMIT 1"), Sql($"SELECT {EntryColumns} FROM stream WHERE id > coalesce((SELECT max(upto) FROM summaries), 0) ORDER BY id")], token);
    Summary? summary = results[0].Rows.Count == 0 ? null : new Summary(results[0].Rows[0][0].GetInt64(), results[0].Rows[0][1].GetString()!);
    List<Entry> entries = new();
    foreach (List<JsonElement> row in results[1].Rows)
    {
      entries.Add(ReadEntry(row));
    }
    return new History(summary, entries);
  }

  public async Task SaveSummaryAsync(Summary summary, CancellationToken token)
  {
    await OneAsync(Sql("INSERT INTO summaries(upto, text) VALUES($1, $2) ON CONFLICT(upto) DO UPDATE SET text = $2", summary.Upto, summary.Text), token);
  }

  public async Task<string?> StateAsync(string key, CancellationToken token)
  {
    SqlResult result = await OneAsync(Sql("SELECT value FROM state WHERE key = $1", key), token);
    return result.Rows.Count == 0 ? null : result.Rows[0][0].GetString();
  }

  public async Task SetStateAsync(string key, string value, CancellationToken token)
  {
    await OneAsync(Sql("INSERT INTO state(key, value) VALUES($1, $2) ON CONFLICT(key) DO UPDATE SET value = $2", key, value), token);
  }

  public async Task<Dictionary<string, string>> ContactsAsync(CancellationToken token)
  {
    SqlResult result = await OneAsync(Sql("SELECT substr(key, 9), value FROM state WHERE key LIKE 'contact:%'"), token);
    Dictionary<string, string> contacts = new();
    foreach (List<JsonElement> row in result.Rows)
    {
      contacts[row[0].GetString()!] = row[1].GetString()!;
    }
    return contacts;
  }

  public async Task SaveContactAsync(string member, string name, CancellationToken token)
  {
    await SetStateAsync("contact:" + member, name, token);
  }

  private static Entry ReadEntry(List<JsonElement> row)
  {
    return new Entry(row[0].GetInt64(), row[1].GetString()!, row[2].GetString()!, row[3].GetString()!, row[4].GetString()!, row[5].GetDateTimeOffset());
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
