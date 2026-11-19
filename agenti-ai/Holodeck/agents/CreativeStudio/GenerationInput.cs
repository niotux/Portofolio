namespace CreativeStudio;

public sealed record GenerationInput(string Prompt, string NegativePrompt, int Width, int Height, long Seed);
