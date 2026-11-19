namespace Humans;

public sealed record ToolBody(string Model, List<Message> Messages, double Temperature, [property: System.Text.Json.Serialization.JsonPropertyName("max_completion_tokens")] int MaxTokens, [property: System.Text.Json.Serialization.JsonPropertyName("reasoning_effort")] string ReasoningEffort, List<Tool> Tools, [property: System.Text.Json.Serialization.JsonPropertyName("tool_choice")] string ToolChoice);
