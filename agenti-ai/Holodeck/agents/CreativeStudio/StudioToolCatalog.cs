using System.Text.Json;

namespace CreativeStudio;

public sealed class StudioToolCatalog(string library)
{
  private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

  public async Task<List<ToolDefinition>> ReadAsync(PromptSession prompts, CancellationToken token)
  {
    string source = await File.ReadAllTextAsync(Path.Combine(library, "controls", "Tools.json"), token);
    StudioControls controls = JsonSerializer.Deserialize<StudioControls>(source, Json) ?? throw new IOException("The tool controls are empty.");
    List<ToolDefinition> tools = new();
    foreach (StudioControl control in controls.Tools)
    {
      tools.Add(ToolDefinition.Create(control.Name, await prompts.ReadAsync(control.Prompt, token), control.Parameters));
    }

    return tools;
  }
}
