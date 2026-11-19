namespace Humans;

// A personality's prepared self-portrait; Source identifies the personality prompt it came from.
public sealed record Prepared(string Source, string Portrait);
