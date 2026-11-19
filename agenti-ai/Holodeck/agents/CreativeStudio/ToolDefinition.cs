using System.Text.Json;

namespace CreativeStudio;

public sealed record ToolDefinition(string Type, ToolFunctionDefinition Function)
{
  public static ToolDefinition Create(string name, string description, JsonElement parameters)
  {
    if (parameters.ValueKind != JsonValueKind.Object)
    {
      throw new ArgumentException("A tool's controls must be a JSON schema object.");
    }
    return new ToolDefinition("function", new ToolFunctionDefinition(name, description, parameters.Clone()));
  }
}
