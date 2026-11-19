using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CreativeStudio;

public sealed partial class CreativeStore
{
  public async Task<NotebookNote> SaveNoteAsync(NotebookWrite write, CancellationToken token)
  {
    string id = write.NoteId ?? Guid.NewGuid().ToString("N");
    string fileJson = JsonSerializer.Serialize(write.Files, Json);
    NotebookContent content = new(write.Title, write.Prose, write.Files);
    string revision = Hash(JsonSerializer.Serialize(content, Json));
    await RunAsync([
      Sql("INSERT INTO notebook(id, channel, title, prose, files, source_packet_id, revision, embedding, embedded_with) VALUES($1, $2, $3, $4, $5::jsonb, $6, $7, $8::vector, $9) ON CONFLICT(id) DO UPDATE SET title = excluded.title, prose = excluded.prose, files = excluded.files, source_packet_id = excluded.source_packet_id, revision = excluded.revision, embedding = excluded.embedding, embedded_with = excluded.embedded_with, updated_at = now() WHERE notebook.channel = excluded.channel", id, write.Channel, write.Title, write.Prose, fileJson, write.SourcePacketId, revision, write.Vector, write.EmbeddingTag),
      Sql("INSERT INTO notebook_revisions(note_id, revision, title, prose, files, source_packet_id) SELECT id, revision, title, prose, files, source_packet_id FROM notebook WHERE id = $1 AND channel = $2 ON CONFLICT DO NOTHING", id, write.Channel)
    ], token);
    return await GetNoteAsync(write.Channel, id, token) ?? throw new IOException("That notebook entry belongs to another conversation.");
  }

  public async Task<NotebookNote?> GetNoteAsync(string channel, string id, CancellationToken token)
  {
    SqlResult result = await OneAsync(Sql("SELECT id, channel, title, prose, files::text, source_packet_id, revision FROM notebook WHERE channel = $1 AND id = $2", channel, id), token);
    return result.Rows.Count == 0 ? null : Note(result.Rows[0]);
  }

  public async Task<List<NotebookNote>> SearchNotesAsync(string channel, float[] vector, string embeddingTag, int limit, CancellationToken token)
  {
    RequireLimit(limit);
    SqlResult result = await OneAsync(Sql("SELECT id, channel, title, prose, files::text, source_packet_id, revision FROM notebook WHERE channel = $1 AND embedded_with = $2 ORDER BY embedding <=> $3::vector LIMIT $4", channel, embeddingTag, vector, limit), token);
    List<NotebookNote> notes = new();
    foreach (List<JsonElement> row in result.Rows)
    {
      notes.Add(Note(row));
    }

    return notes;
  }

  private static NotebookNote Note(List<JsonElement> row)
  {
    return new NotebookNote(Text(row[0]), Text(row[1]), Text(row[2]), Text(row[3]), Files(row[4]), row[5].GetInt64(), Text(row[6]));
  }

  private static string Hash(string content)
  {
    return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
  }
}
