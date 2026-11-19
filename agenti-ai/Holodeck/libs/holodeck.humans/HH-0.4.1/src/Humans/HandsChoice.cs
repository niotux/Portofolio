namespace Humans;

// What the hands did with the controls they had: an action constant and its argument (a conversation, a name, a question or an alarm time) and, for an alarm, what it is for.
public sealed record HandsChoice(string Action, string Argument, string Note = "");
