using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Holodeck.Web.Settings;

// Parses and writes settings JSON. Values keep their original text, including numbers too large or precise for a
// double, so a save rewrites only what was changed.
public static class SettingsDocument
{
  public const string Empty = "{}";
  private static readonly JsonDocumentOptions ReadOptions = new() { MaxDepth = SettingsLimits.MaxDepth * 4, AllowDuplicateProperties = false };
  private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true, IndentSize = 2, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

  // Throws JsonException for anything that is not one well-formed JSON value, including repeated property names.
  public static JsonNode? Parse(string text)
  {
    return JsonNode.Parse(text, null, ReadOptions);
  }

  public static string Write(JsonNode document)
  {
    return document.ToJsonString(WriteOptions) + "\n";
  }
}
