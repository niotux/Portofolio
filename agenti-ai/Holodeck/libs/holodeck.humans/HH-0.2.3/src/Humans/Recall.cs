using System.Globalization;
using System.Text;

namespace Humans;

// Before thinking about something new: what in it my memory might hold more about, a search for each, and my own
// remembering of what came back. Each is an instructed prose pass; the things to look for are lines. Memories of what
// is still in my live history, and those that came back recently, are left out.
public sealed class Recall(ChatModel model, Embedder embedder, Store store, Page page, RuntimeOptions options)
{
  private readonly Dictionary<long, int> surfaced = new();
  private int cycle;

  public async Task<List<string>> RememberAsync(string recent, long firstLive, ActivityReporter? activity, CancellationToken token)
  {
    cycle++;
    List<string> recollections = new();
    if (await store.MemoryCountAsync(token) == 0)
    {
      return recollections;
    }
    Action<string>? onText = activity is null ? null : activity.Append;
    activity?.Begin("look", "What to remember");
    string looked = await PassAsync("look.txt", recent, token, onText);
    activity?.End();
    foreach (string subject in Lines(looked, options.MaxLookups))
    {
      List<string> found = await FindAsync(subject, firstLive, token);
      Console.WriteLine("Looked for \"" + subject + "\": " + found.Count + " came back.");
      if (found.Count == 0)
      {
        continue;
      }
      StringBuilder input = new("Trying to remember: ");
      input.Append(subject).Append("\n\nWhat it brought back:\n");
      foreach (string memory in found)
      {
        input.Append(memory).Append('\n');
      }
      activity?.Begin("remember", "Remembering");
      string remembering = await PassAsync("remember.txt", input.ToString(), token, onText);
      activity?.End();
      if (remembering.Length > 0)
      {
        recollections.Add(remembering);
      }
    }
    return recollections;
  }

  private async Task<List<string>> FindAsync(string subject, long firstLive, CancellationToken token)
  {
    List<long> skip = new();
    foreach (KeyValuePair<long, int> recalled in surfaced)
    {
      if (cycle - recalled.Value <= options.QuietCycles)
      {
        skip.Add(recalled.Key);
      }
    }
    float[] query = await embedder.EmbedAsync(subject, true, token);
    List<string> found = new();
    int characters = 0;
    foreach (MemoryMatch match in await store.NearestAsync(query, embedder.Tag, firstLive, skip, options.PerCue, token))
    {
      Console.WriteLine("Recall candidate " + match.Similarity.ToString("0.000", CultureInfo.InvariantCulture) + ": " + match.Text);
      if (match.Similarity < options.MinSimilarity)
      {
        continue;
      }
      characters += match.Text.Length;
      if (characters > options.MaxRecallCharacters)
      {
        break;
      }
      found.Add(match.Text.Trim());
      surfaced[match.Id] = cycle;
    }
    return found;
  }

  private async Task<string> PassAsync(string instruction, string input, CancellationToken token, Action<string>? onText)
  {
    Completion result = await model.SayAsync([new Message("system", page.Instruction(instruction)), new Message("user", input)], token, onText, options.UtilityTemperature);
    return result.Text.Trim();
  }

  // Each nonempty line is one subject; a list mark the model put in front of it is not part of it.
  public static List<string> Lines(string text, int most)
  {
    List<string> lines = new();
    ReadOnlySpan<char> rest = text.AsSpan();
    while (rest.Length > 0 && lines.Count < most)
    {
      int end = rest.IndexOf('\n');
      ReadOnlySpan<char> line = (end < 0 ? rest : rest[..end]).TrimStart("-*•0123456789.) ").Trim();
      if (line.Length > 0)
      {
        lines.Add(line.ToString());
      }
      rest = end < 0 ? ReadOnlySpan<char>.Empty : rest[(end + 1)..];
    }
    return lines;
  }
}
