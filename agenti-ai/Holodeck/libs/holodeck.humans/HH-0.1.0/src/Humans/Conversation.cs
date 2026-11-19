using System.Text;

namespace Humans;

// Adapted from Person/Conversation.cs: compacted prose + recent verbatim turns, each reply kept with the thought
// that came before it, so the page shows what I thought as well as what I said.
public sealed class Conversation(RuntimeOptions options)
{
  private readonly List<Turn> turns = new();
  public string Summary { get; private set; } = "";
  public int Dropped { get; private set; }
  public int Position => Dropped + turns.Count;
  public int FirstKept => Dropped;
  public IReadOnlyList<Turn> Turns => turns;

  public void Restore(SavedSession saved)
  {
    Summary = saved.Summary;
    Dropped = saved.Dropped;
    turns.Clear();
    turns.AddRange(saved.Turns);
  }

  public void AddUser(string text)
  {
    turns.Add(new Turn("user", text, "", ""));
  }

  public void DropUser()
  {
    turns.RemoveAt(turns.Count - 1);
  }

  public void AddReply(string text, string thought, string recalled)
  {
    turns.Add(new Turn("assistant", text, thought, recalled));
  }

  public async Task<bool> CompactAsync(ChatModel model, string prompt, double temperature, CancellationToken token)
  {
    int cut = turns.Count - options.KeepTurns;
    while (cut > 0 && cut < turns.Count && turns[cut].Role != "user")
    {
      cut++;
    }
    if (cut <= 0)
    {
      return false;
    }

    StringBuilder input = new();
    if (Summary.Length > 0)
    {
      input.Append("Earlier summary:\n").Append(Summary).Append("\n\n");
    }
    for (int i = 0; i < cut; i++)
    {
      input.Append(turns[i].Role == "user" ? "They: " : "I: ").Append(turns[i].Text).Append('\n');
    }
    Completion result = await model.SayAsync([new Message("system", prompt), new Message("user", input.ToString())], token, null, temperature);
    if (string.IsNullOrWhiteSpace(result.Text))
    {
      throw new IOException("Compaction returned no prose; original context was retained.");
    }
    Summary = result.Text.Trim();
    turns.RemoveRange(0, cut);
    Dropped += cut;
    return true;
  }
}
