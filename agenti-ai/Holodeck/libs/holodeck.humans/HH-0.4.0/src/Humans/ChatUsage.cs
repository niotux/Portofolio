namespace Humans;

public sealed record ChatUsage([property: System.Text.Json.Serialization.JsonPropertyName("prompt_tokens")] int PromptTokens, [property: System.Text.Json.Serialization.JsonPropertyName("completion_tokens")] int CompletionTokens);
