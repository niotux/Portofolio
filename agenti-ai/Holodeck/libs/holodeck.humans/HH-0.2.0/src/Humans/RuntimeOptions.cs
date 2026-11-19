namespace Humans;

// The library's allowances for one person: capacity, pacing and operating limits. Values live in runtime.json.
public sealed record RuntimeOptions
(
  int ContextTokens,
  int CompactPercent,
  int KeepEntries,
  double CharactersPerToken,
  int PollSeconds,
  int FormationAttempts,
  int FormEntries,
  EmbeddingOptions Embedding,
  int ThoughtTokens,
  int SpeechTokens,
  int UtilityTokens,
  double MinSimilarity,
  int PerCue,
  int MaxLookups,
  int QuietCycles,
  int MaxRecallCharacters,
  double ThoughtTemperature,
  double SpeechTemperature,
  double UtilityTemperature,
  double HandsTemperature,
  int OpenMessages,
  int TimeGapMinutes,
  string TimeZone,
  int MaxCyclesPerWake,
  int MaxCyclesPerHour,
  int IdleWakeMinutes,
  int HandsEntries,
  int LookEntries
);
