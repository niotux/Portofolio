namespace Humans;

// What the hands did with the controls they had: an action constant and its one argument (a conversation, or a name).
public sealed record HandsChoice(string Action, string Argument);
