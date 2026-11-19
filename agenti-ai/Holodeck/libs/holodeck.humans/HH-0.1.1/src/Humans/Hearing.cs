using System.Text;

namespace Humans;

// Before I think: from what is happening now, the few things worth trying to remember, then for each one what my
// memory brings back, written as my remembering. Someone I have talked to before I recognise first. Each is a separate
// instructed prose pass; the things to remember are lines.
public sealed class Hearing(ChatModel model, Page page, RuntimeOptions options)
{
  private const int MaxLookups = 3;
  // What is happening now: the last few lines, not the whole chat.
  private const int ActiveTurns = 4;

  public async Task<List<string>> WorthRememberingAsync(string context, CancellationToken token, Action<string>? onText)
  {
    string written = await PassAsync("look", context, token, onText);
    return Lines(written, MaxLookups);
  }

  public async Task<string> RecogniseAsync(List<string> known, CancellationToken token, Action<string>? onText)
  {
    StringBuilder input = new("My memories of earlier chats with them:\n");
    foreach (string memory in known)
    {
      input.Append(memory).Append('\n');
    }
    return await PassAsync("recognise", input.ToString(), token, onText);
  }

  public async Task<string> RememberAsync(string subject, List<string> memories, CancellationToken token, Action<string>? onText)
  {
    StringBuilder input = new("Trying to remember: ");
    input.Append(subject).Append("\n\nWhat it brought back:\n");
    foreach (string memory in memories)
    {
      input.Append(memory).Append('\n');
    }
    return await PassAsync("remember", input.ToString(), token, onText);
  }

  public string Context(IReadOnlyList<Turn> turns, string said)
  {
    string chat = page.Dialogue(turns, Math.Max(0, turns.Count - ActiveTurns));
    return "The last lines of the chat:\n" + (chat.Length > 0 ? chat : "(nothing yet)\n") + "\nWhat they just wrote:\n" + said;
  }

  private async Task<string> PassAsync(string instruction, string input, CancellationToken token, Action<string>? onText)
  {
    Completion result = await model.SayAsync([new Message("system", page.Instruction(instruction)), new Message("user", input)], token, onText, options.UtilityTemperature);
    return result.Text.Trim();
  }

  // Each non-empty line is one subject; a list mark the model put in front of it is not part of it.
  private static List<string> Lines(string text, int most)
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
