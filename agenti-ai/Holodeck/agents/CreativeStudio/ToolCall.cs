using System.Text.Json;

namespace CreativeStudio;

public sealed record ToolCall(string Id, string Name, JsonElement Arguments);
