using System.Text.Json;

namespace CreativeStudio;

public sealed partial class CreativeStore
{
  public async Task<List<StoredReference>> RecentReferencesAsync(string channel, int limit, int descriptionCharacters, CancellationToken token)
  {
    RequireLimit(limit);
    RequireLimit(descriptionCharacters);
    SqlResult result = await OneAsync(Sql("SELECT path, left(description, $3::integer) FROM assets WHERE channel = $1 ORDER BY position DESC LIMIT $2", channel, limit, descriptionCharacters), token);
    List<StoredReference> files = new();
    foreach (List<JsonElement> row in result.Rows)
    {
      files.Add(new StoredReference(Text(row[0]), Text(row[1])));
    }

    return files;
  }

  public async Task RecordGenerationFilesAsync(string id, List<string> files, CancellationToken token)
  {
    List<SqlStatement> statements = new();
    AppendGenerationFiles(statements, id, files, true);
    if (statements.Count > 0)
    {
      await RunAsync(statements, token);
    }
  }

  private static void AppendGenerationFiles(List<SqlStatement> statements, string id, List<string> files, bool completed = false)
  {
    string states = completed ? "state <> 'failed'" : "state NOT IN ('completed', 'failed')";
    foreach (string file in files)
    {
      statements.Add(Sql("INSERT INTO assets(channel, path, description, source_packet_id) SELECT channel, $2, 'Generated from this prompt: ' || coalesce(request_json #>> '{plan,input,prompt}', ''), packet_id FROM generations WHERE id = $1 AND " + states + " ON CONFLICT DO NOTHING", id, file));
    }
  }
}
