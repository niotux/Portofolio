namespace CreativeStudio;

public sealed record GenerateImageInput(string Workflow, string Prompt, string? NegativePrompt, int? Width, int? Height, long? Seed);
