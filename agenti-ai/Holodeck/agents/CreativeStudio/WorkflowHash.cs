using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CreativeStudio;

// PostgreSQL JSONB may reorder object properties. Snapshot hashes must survive that round trip.
public static class WorkflowHash
{
  public static string Compute(JsonNode graph)
  {
    using JsonDocument document = JsonDocument.Parse(graph.ToJsonString());
    using MemoryStream bytes = new();
    using (Utf8JsonWriter writer = new(bytes))
    {
      Write(writer, document.RootElement);
    }
    return Convert.ToHexStringLower(SHA256.HashData(bytes.ToArray()));
  }

  private static void Write(Utf8JsonWriter writer, JsonElement element)
  {
    if (element.ValueKind == JsonValueKind.Object)
    {
      List<JsonProperty> properties = new();
      foreach (JsonProperty property in element.EnumerateObject())
      {
        properties.Add(property);
      }
      properties.Sort((left, right) => string.CompareOrdinal(left.Name, right.Name));
      writer.WriteStartObject();
      foreach (JsonProperty property in properties)
      {
        writer.WritePropertyName(property.Name);
        Write(writer, property.Value);
      }
      writer.WriteEndObject();
    }
    else if (element.ValueKind == JsonValueKind.Array)
    {
      writer.WriteStartArray();
      foreach (JsonElement item in element.EnumerateArray())
      {
        Write(writer, item);
      }
      writer.WriteEndArray();
    }
    else if (element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out decimal number))
    {
      writer.WriteRawValue(number.ToString("G29", CultureInfo.InvariantCulture));
    }
    else
    {
      element.WriteTo(writer);
    }
  }
}
