namespace Humans;

// A stretch of the history waiting to become a prose memory.
public sealed record Formation(long Id, long First, long Last, string Text, string? Draft, int Attempts);
