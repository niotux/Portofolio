using System.Text.Json;
using System.Text.Json.Serialization;

namespace CreativeStudio;

public sealed record ComfyExecutionStatus([property: JsonPropertyName("status_str")] string Status, bool Completed, List<JsonElement>? Messages);
