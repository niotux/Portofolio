namespace Humans;

public sealed record ChatResponse(List<ChatChoice> Choices, ChatUsage? Usage);
