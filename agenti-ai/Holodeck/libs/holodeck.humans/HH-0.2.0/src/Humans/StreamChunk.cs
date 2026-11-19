namespace Humans;

public sealed record StreamChunk(List<StreamChoice>? Choices, ChatUsage? Usage);
