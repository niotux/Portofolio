using System.Text.Json.Serialization;

namespace CreativeStudio;

public sealed record ThinkingOptions([property: JsonPropertyName("enable_thinking")] bool EnableThinking);
