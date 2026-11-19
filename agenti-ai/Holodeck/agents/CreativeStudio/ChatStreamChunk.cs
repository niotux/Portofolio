namespace CreativeStudio;

public sealed record ChatStreamChunk(List<ChatStreamChoice>? Choices, ModelUsage? Usage);
