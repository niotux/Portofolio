namespace CreativeStudio;

public sealed record ModelAction(string Text, List<ToolCall> Calls, ModelUsage Usage);
