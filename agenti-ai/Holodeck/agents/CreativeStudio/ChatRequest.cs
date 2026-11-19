using System.Text.Json.Serialization;

namespace CreativeStudio;

public sealed record ChatRequest(string Model, List<ChatMessage> Messages, double Temperature, [property: JsonPropertyName("max_tokens")] int MaxTokens, bool Stream, [property: JsonPropertyName("chat_template_kwargs")] ThinkingOptions Thinking, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] List<ToolDefinition>? Tools, [property: JsonPropertyName("tool_choice"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ToolChoice, [property: JsonPropertyName("stream_options"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ChatStreamOptions? StreamOptions);
