namespace Humans;

// One moment of the continuing private history. Kind and channel are code's; Text is prose.
public sealed record Entry(long Id, string Kind, string Channel, string Speaker, string Text, DateTimeOffset At);
