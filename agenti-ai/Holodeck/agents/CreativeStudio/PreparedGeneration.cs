namespace CreativeStudio;

public sealed record PreparedGeneration(GenerationPlan Plan, List<PreparedPrompt> Prompts, List<string> InspectedFiles, string Brief);
