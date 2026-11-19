using System.Text.Json.Serialization;

namespace CreativeStudio;

public sealed record ChatStreamChoice(ChatStreamDelta? Delta, [property: JsonPropertyName("finish_reason")] string? FinishReason);
