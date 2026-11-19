using System.Text.Json;

namespace CreativeStudio;

// Library files seed immutable versions; an explicit selection can retain a tuned version across library updates.
public sealed class PromptRegistry(string library, CreativeStore store, int maxPromptCharacters)
{
  private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
  private readonly Dictionary<string, PromptDefinition> definitions = new(StringComparer.Ordinal);

  public async Task InitializeAsync(CancellationToken token)
  {
    definitions.Clear();
    string directory = Path.GetFullPath(Path.Combine(library, "prompts"));
    string manifestPath = Path.Combine(directory, "registry.json");
    PromptManifest manifest = JsonSerializer.Deserialize<PromptManifest>(await File.ReadAllTextAsync(manifestPath, token), Json) ?? throw new IOException("The prompt registry is empty.");
    if (manifest.Prompts is null)
    {
      throw new IOException("The prompt registry has no prompts.");
    }

    foreach (PromptDefinition definition in manifest.Prompts)
    {
      if (string.IsNullOrWhiteSpace(definition.Id) || !definitions.TryAdd(definition.Id, definition))
      {
        throw new IOException("Each prompt needs a unique name.");
      }

      string path = Path.GetFullPath(Path.Combine(directory, definition.File));
      if (!path.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.Ordinal))
      {
        throw new IOException("A prompt file is outside the library's prompt folder.");
      }

      string content = await File.ReadAllTextAsync(path, token);
      ValidateContent(content);
      await store.SeedPromptAsync(definition.Id, content, token);
    }
  }

  public Task<PromptRevision> GetAsync(string id, CancellationToken token)
  {
    RequireDefinition(id);
    return store.ActivePromptAsync(id, token);
  }

  public Task<PromptRevision> SaveAsync(string id, string content, CancellationToken token)
  {
    RequireDefinition(id);
    ValidateContent(content);
    return store.SavePromptAsync(id, content, token);
  }

  public Task SelectAsync(string id, string revision, CancellationToken token)
  {
    RequireDefinition(id);
    return store.SelectPromptAsync(id, revision, token);
  }

  public Task<List<PromptRevision>> VersionsAsync(string id, CancellationToken token)
  {
    RequireDefinition(id);
    return store.PromptVersionsAsync(id, token);
  }

  public IReadOnlyDictionary<string, PromptDefinition> Definitions => definitions;

  private void RequireDefinition(string id)
  {
    if (!definitions.ContainsKey(id))
    {
      throw new KeyNotFoundException("That prompt is not registered.");
    }
  }

  private void ValidateContent(string content)
  {
    if (string.IsNullOrWhiteSpace(content) || content.Length > maxPromptCharacters)
    {
      throw new ArgumentException("Keep prompt content nonempty and within the configured prompt allowance.");
    }
  }
}
