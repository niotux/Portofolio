namespace CreativeStudio;

// Character and image allowances bound assembly conservatively; measured model usage is recorded separately.
public sealed record ContextOptions(int MaxCharacters, int RecentMessages, int MaxNoteCharacters, int MaxImages, long MaxImageBytes);
