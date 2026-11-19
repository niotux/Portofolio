namespace CreativeStudio;

public sealed record ChatResponse(List<ChatChoice>? Choices, ModelUsage? Usage);
