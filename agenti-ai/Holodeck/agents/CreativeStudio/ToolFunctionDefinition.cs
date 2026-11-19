using System.Text.Json;

namespace CreativeStudio;

public sealed record ToolFunctionDefinition(string Name, string Description, JsonElement Parameters);
