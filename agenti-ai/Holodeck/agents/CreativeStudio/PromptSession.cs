namespace CreativeStudio;

// Captures exactly the published prompt versions used by one conversation turn.
public sealed class PromptSession(PromptRegistry registry)
{
  private readonly Dictionary<string, PreparedPrompt> used = new(StringComparer.Ordinal);

  public async Task<string> ReadAsync(string purpose, CancellationToken token)
  {
    if (used.TryGetValue(purpose, out PreparedPrompt? existing))
    {
      return existing.Content;
    }

    PromptRevision revision = await registry.GetAsync(purpose, token);
    PreparedPrompt prompt = new(purpose, revision.Revision, revision.Content);
    used.Add(purpose, prompt);
    return prompt.Content;
  }

  public List<PreparedPrompt> Snapshot()
  {
    List<PreparedPrompt> result = new();
    foreach (PreparedPrompt prompt in used.Values)
    {
      result.Add(prompt);
    }

    return result;
  }
}
