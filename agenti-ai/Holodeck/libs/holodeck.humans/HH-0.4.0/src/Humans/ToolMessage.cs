namespace Humans;

public sealed record ToolMessage(string? Content, [property: System.Text.Json.Serialization.JsonPropertyName("tool_calls")] List<ToolCall>? ToolCalls);
