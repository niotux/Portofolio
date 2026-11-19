using System.Text.Json.Serialization;

namespace CreativeStudio;

public sealed record ChatResponseMessage(string? Content, [property: JsonPropertyName("tool_calls")] List<ToolResponseCall>? ToolCalls);
