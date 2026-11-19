namespace Humans;

// One buzz of the phone: a conversation with something new, and whether any of it calls the person.
public sealed record Buzz(string Channel, bool Mention);
