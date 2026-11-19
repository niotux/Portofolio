using System.Text.Json.Serialization;

namespace CreativeStudio;

public sealed record ChatChoice(ChatResponseMessage? Message, [property: JsonPropertyName("finish_reason")] string? FinishReason);
