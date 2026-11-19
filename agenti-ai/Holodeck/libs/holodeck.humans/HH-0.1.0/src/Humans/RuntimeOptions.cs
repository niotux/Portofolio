namespace Humans;

public sealed record RuntimeOptions(int RecentTurns, int KeepTurns, int CompactPercent, int ContextTokens, int PerCue, int QuietTurns, int KnownMemories, int MaxRecallCharacters, int PollSeconds, int FormationAttempts, EmbeddingOptions Embedding, int ThoughtTokens, double MinSimilarity, double SpeechTemperature, double UtilityTemperature);
