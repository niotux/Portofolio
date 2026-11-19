namespace Humans;

public sealed record PromptPublication(int Number, string Label, DateTimeOffset CreatedAt, PromptBundle Content);
