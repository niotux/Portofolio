using System.Text.Json;
using System.Text.Json.Serialization;

namespace CreativeStudio;

public sealed record ComfyQueue([property: JsonPropertyName("queue_running")] List<List<JsonElement>> Running, [property: JsonPropertyName("queue_pending")] List<List<JsonElement>> Pending);
