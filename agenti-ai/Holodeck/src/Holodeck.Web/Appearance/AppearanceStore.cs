using System.Text;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace Holodeck.Web.Appearance;

// Stores the one appearance shared by every browser: the main menu and every project use the same look.
public sealed class AppearanceStore(NpgsqlDataSource database)
{
  public const int MaxDocumentBytes = 16 * 1024;

  public async Task InitializeAsync(CancellationToken token)
  {
    const string Schema = """
      CREATE TABLE IF NOT EXISTS appearance (
        id boolean PRIMARY KEY DEFAULT true CHECK (id),
        settings jsonb NOT NULL,
        updated_at timestamptz NOT NULL);
      """;
    await using NpgsqlCommand command = database.CreateCommand(Schema);
    await command.ExecuteNonQueryAsync(token);
  }

  public async Task<AppearanceDocument> ReadAsync(CancellationToken token)
  {
    const string Sql = "SELECT settings::text FROM appearance WHERE id";
    await using NpgsqlCommand command = database.CreateCommand(Sql);
    object? settings = await command.ExecuteScalarAsync(token);
    if (settings is null)
    {
      return new AppearanceDocument(null);
    }

    return new AppearanceDocument(Parse((string)settings));
  }

  public async Task WriteAsync(AppearanceDocument document, CancellationToken token)
  {
    string settings = Validate(document.Settings);
    const string Sql = """
      INSERT INTO appearance(id, settings, updated_at) VALUES (true, $1, now())
      ON CONFLICT (id) DO UPDATE SET settings = excluded.settings, updated_at = excluded.updated_at
      """;
    await using NpgsqlCommand command = database.CreateCommand(Sql);
    command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Jsonb, Value = settings });
    await command.ExecuteNonQueryAsync(token);
  }

  private static string Validate(JsonElement? settings)
  {
    if (settings is null || settings.Value.ValueKind != JsonValueKind.Object)
    {
      throw new ArgumentException("Settings must be a JSON object.");
    }

    string text = settings.Value.GetRawText();
    if (Encoding.UTF8.GetByteCount(text) > MaxDocumentBytes)
    {
      throw new ArgumentException($"The appearance is larger than {MaxDocumentBytes} bytes.");
    }

    return text;
  }

  private static JsonElement Parse(string json)
  {
    using JsonDocument document = JsonDocument.Parse(json);
    return document.RootElement.Clone();
  }
}
