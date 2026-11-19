namespace Humans;

// What an operation ran with, kept beside the person's workspace for comparing runs. Never model prose.
public sealed record PromptUse(string Version, long Revision, int? Publication);
