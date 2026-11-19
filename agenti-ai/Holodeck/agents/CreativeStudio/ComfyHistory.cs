namespace CreativeStudio;

public sealed record ComfyHistory(Dictionary<string, ComfyNodeOutput> Outputs, ComfyExecutionStatus Status);
