using System.Text;
using System.Text.Json;

namespace Humans;

// A person's own passes (noticing, thinking, speaking) continue a page of text: a chat log with private thoughts in
// brackets. There is no assistant, system prompt or user on the page, so the model has no one to be but the person.
// Utility passes (forming a memory, compacting) are ordinary instructions. All prose comes from the library.
public sealed class Page(string directory, Personality personality, string displayName, string other, string self)
{
  public string Name => string.IsNullOrWhiteSpace(personality.Name) ? displayName : personality.Name;

  public PageText Text => JsonSerializer.Deserialize<PageText>(File.ReadAllText(Path.Combine(directory, "page.json"))) ?? throw new IOException("The library page text is empty.");

  public string QueryInstruction => File.ReadAllText(Path.Combine(directory, "search.txt")).Trim();

  public string Fill(string line)
  {
    return line.Replace("{name}", Name).Replace("{personality}", personality.Description ?? "").Replace("{other}", other).Replace("{self}", self);
  }

  public string Instruction(string name)
  {
    return Fill(File.ReadAllText(Path.Combine(directory, name + ".txt")).Trim());
  }

  // The page up to and including what they just said.
  public string Document(Conversation chat, string said)
  {
    PageText text = Text;
    StringBuilder page = new(Fill(text.Header));
    page.Append("\n\n");
    // Who I am is always on the page, like a character introduced in a novel; it is never restated as a thought.
    if (self.Length > 0)
    {
      page.Append(Fill(text.Self)).Append('\n');
    }
    if (!string.IsNullOrWhiteSpace(personality.Description))
    {
      page.Append(Fill(text.Personality)).Append('\n');
    }
    page.Append('\n');
    if (chat.Summary.Length > 0)
    {
      page.Append(Fill(text.Summary)).Append(' ').Append(chat.Summary).Append(text.Close).Append('\n');
    }
    AppendTurns(page, text, chat.Turns, 0);
    return page.Append(text.Other).Append(": ").Append(said).Append('\n').ToString();
  }

  public string Lines(IReadOnlyList<Turn> turns, int start)
  {
    StringBuilder lines = new();
    AppendTurns(lines, Text, turns, start);
    return lines.ToString();
  }

  // What was said, without thoughts: what a reader of the chat would see.
  public string Dialogue(IReadOnlyList<Turn> turns, int start)
  {
    PageText text = Text;
    StringBuilder lines = new();
    for (int i = start; i < turns.Count; i++)
    {
      lines.Append(turns[i].Role == "user" ? text.Other : Name).Append(": ").Append(turns[i].Text).Append('\n');
    }
    return lines.ToString();
  }

  public string Exchange(string said, string thought, string reply)
  {
    List<Turn> turns = [new Turn("user", said, "", ""), new Turn("assistant", reply, thought, "")];
    return Lines(turns, 0);
  }

  private void AppendTurns(StringBuilder page, PageText text, IReadOnlyList<Turn> turns, int start)
  {
    for (int i = start; i < turns.Count; i++)
    {
      Turn turn = turns[i];
      if (turn.Role == "user")
      {
        page.Append(text.Other).Append(": ").Append(turn.Text).Append('\n');
        continue;
      }
      page.Append(turn.Recalled);
      if (turn.Thought.Length > 0)
      {
        page.Append(Fill(turn.Recalled.Length > 0 ? text.ThoughtAfterRecall : text.Thought)).Append(' ').Append(turn.Thought).Append(text.Close).Append('\n');
      }
      page.Append(Name).Append(": ").Append(turn.Text).Append('\n');
    }
  }
}
