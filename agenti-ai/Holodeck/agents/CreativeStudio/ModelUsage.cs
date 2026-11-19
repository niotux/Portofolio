using System.Text.Json.Serialization;

namespace CreativeStudio;

public sealed record ModelUsage([property: JsonPropertyName("prompt_tokens")] int PromptTokens, [property: JsonPropertyName("completion_tokens")] int CompletionTokens);
