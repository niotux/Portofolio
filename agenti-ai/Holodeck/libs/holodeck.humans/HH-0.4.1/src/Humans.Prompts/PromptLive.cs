namespace Humans;

// The prompts people of one HH version run right now. Publication is the published number with identical content, if
// any; Latest is the newest publication.
public sealed record PromptLive(string Version, long Revision, int? Publication, int? Latest, DateTimeOffset ChangedAt, PromptBundle Content);
