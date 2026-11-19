namespace Humans;

// A personality's prepared self-portrait and the time zone of the life it describes; Source identifies the personality
// prompt it came from. An empty Zone means the configured one.
public sealed record Prepared(string Source, string Portrait, string Zone);
