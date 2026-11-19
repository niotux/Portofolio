using System.Text;

namespace Humans;

// The person's own passes speak as the person. Their life is a chat transcript in which the model is the person: what
// reaches them (messages, buzzes, memories, the passing of time, what their body does) arrives as the other side, and
// their own thoughts and messages are their own turns. A pass ends where the person's turn ends, so the model is never
// asked to write the rest of a document. All prose comes from the pinned prompt set.
public sealed class Transcript(Page page)
{
  public List<Message> Messages(Summary? summary, List<Entry> entries, Surroundings world, string cue)
  {
    PageText text = page.Text;
    List<Message> messages = [new Message("system", page.Instruction("self.txt"))];
    StringBuilder turn = new();
    string role = "user";
    turn.Append(page.Fill(summary is null ? text.Start : text.Before).Replace("{text}", summary is null ? "" : summary.Text));
    DateTimeOffset? previous = null;
    foreach (Entry entry in entries)
    {
      bool mine = entry.Kind == EntryKinds.Thought || entry.Kind == EntryKinds.Said;
      bool gap = previous is not null && (entry.At - previous.Value).TotalMinutes >= page.TimeGapMinutes;
      previous = entry.At;
      if (gap)
      {
        // The passing of time reaches the person from outside, before their next moment.
        Switch(messages, turn, ref role, "user");
        Line(turn, page.Moment(entry.At));
      }
      Switch(messages, turn, ref role, mine ? "assistant" : "user");
      Line(turn, Render(entry, world));
    }
    Switch(messages, turn, ref role, "user");
    Line(turn, cue);
    messages.Add(new Message(role, turn.ToString()));
    return messages;
  }

  private string Render(Entry entry, Surroundings world)
  {
    PageText text = page.Text;
    switch (entry.Kind)
    {
      case EntryKinds.Thought:
        return page.Fill(text.MyThought).Replace("{text}", entry.Text);
      case EntryKinds.Said:
        return page.Fill(text.MySaid).Replace("{channel}", page.RoomOf(entry.Channel, world.Rooms).Label).Replace("{text}", entry.Text);
      case EntryKinds.Recollection:
        return page.Fill(text.Memory).Replace("{text}", entry.Text);
      case EntryKinds.Message:
        return page.Said(page.NameOf(entry.Speaker, world), page.RoomOf(entry.Channel, world.Rooms)) + " " + entry.Text;
      default:
        return entry.Text;
    }
  }

  // Consecutive moments on the same side are one turn.
  private static void Switch(List<Message> messages, StringBuilder turn, ref string role, string next)
  {
    if (role == next)
    {
      return;
    }
    if (turn.Length > 0)
    {
      messages.Add(new Message(role, turn.ToString()));
      turn.Clear();
    }
    role = next;
  }

  private static void Line(StringBuilder turn, string line)
  {
    if (line.Length == 0)
    {
      return;
    }
    if (turn.Length > 0)
    {
      turn.Append('\n');
    }
    turn.Append(line);
  }
}
