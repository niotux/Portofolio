namespace Humans;

// Recalled holds the page lines that came to me before that reply (what I took them to mean, what it brought back), so
// they stay in mind while the turn does.
public sealed record Turn(string Role, string Text, string Thought, string Recalled);
