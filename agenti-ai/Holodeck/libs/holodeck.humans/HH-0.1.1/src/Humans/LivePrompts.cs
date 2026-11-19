using System.Text.Json;

namespace Humans;

// This person's HH version reads its live prompts from the shared catalog at the start of each operation.
public sealed class LivePrompts(PromptStore store, string version)
{
  public Task<PromptLive> ReadAsync(CancellationToken token)
  {
    return store.RequireLiveAsync(version, token);
  }

  public static void Record(string workspace, string file, PromptLive live)
  {
    try
    {
      PromptUse used = new(live.Version, live.Revision, live.Publication);
      File.WriteAllText(Path.Combine(workspace, file), JsonSerializer.Serialize(used));
    }
    catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
    {
      Console.WriteLine("The optional prompt-use record could not be written.");
    }
  }
}
