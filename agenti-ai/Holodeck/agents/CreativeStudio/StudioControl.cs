using System.Text.Json;

namespace CreativeStudio;

public sealed record StudioControl(string Name, string Prompt, JsonElement Parameters);
