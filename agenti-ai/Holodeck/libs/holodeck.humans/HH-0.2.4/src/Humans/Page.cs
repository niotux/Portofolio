using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Humans;

// The person's own passes (thinking, speaking) continue one page: the continuing history of a life, with private
// thoughts in brackets and messages as they appear on the person's screen. There is no assistant or user on the page,
// so the model has no one to be but the person. All prose comes from the pinned prompt set.
public sealed class Page(string name, TimeZoneInfo zone, int timeGapMinutes)
{
  public static readonly List<string> Needed = ["page.json", "controls.json", "continue.txt", "hands.txt", "look.txt", "search.txt", "remember.txt", "form.txt", "compact.txt", "prepare.txt", "lives.txt", "work.txt", "check.txt"];
  private PromptLive? live;
  private PageText? text;
  private ControlText? controls;

  public string Name => name;

  public string Portrait { get; set; } = "";

  public PromptLive? Live => live;

  public PageText Text => text ?? throw new InvalidOperationException("No prompts were loaded for this operation.");

  public ControlText Controls => controls ?? throw new InvalidOperationException("No prompts were loaded for this operation.");

  public string QueryInstruction => Read("search.txt").Trim();

  // Pins one prompt set for a whole operation; edits reach the next one. A set this version cannot run is refused
  // before anything changes.
  public void Use(PromptLive prompts)
  {
    prompts.Content.Validate();
    prompts.Content.Require(Needed);
    PageText page = JsonSerializer.Deserialize<PageText>(prompts.Content.Get("page.json")) ?? throw new ArgumentException("page.json is empty.");
    ControlText control = JsonSerializer.Deserialize<ControlText>(prompts.Content.Get("controls.json")) ?? throw new ArgumentException("controls.json is empty.");
    RequireLabels(page, control);
    live = prompts with { Content = prompts.Content.Copy() };
    text = page;
    controls = control;
  }

  public string Fill(string line)
  {
    return line.Replace("{name}", name).Replace("{portrait}", Portrait);
  }

  public string Instruction(string file)
  {
    return Fill(Read(file).Trim());
  }

  public string Thought => Fill(Text.Thought);

  public string Speech(Room room)
  {
    return Said(name, room);
  }

  public string Said(string speaker, Room room)
  {
    return Fill(Text.Said).Replace("{speaker}", speaker).Replace("{channel}", room.Label);
  }

  // The whole page: who I am, what I remember of before, and everything since.
  public string Document(Summary? summary, List<Entry> entries, Surroundings world)
  {
    StringBuilder page = new(Fill(Text.Header));
    page.Append("\n\n");
    if (Portrait.Length > 0)
    {
      page.Append(Fill(Text.Portrait)).Append("\n\n");
    }
    if (summary is not null)
    {
      page.Append(Fill(Text.Summary)).Append(' ').Append(summary.Text).Append(Text.Close).Append("\n\n");
    }
    AppendEntries(page, entries, 0, world);
    return page.ToString();
  }

  public string Entries(List<Entry> entries, int start, Surroundings world)
  {
    StringBuilder lines = new();
    AppendEntries(lines, entries, Math.Max(0, start), world);
    return lines.ToString();
  }

  public string Event(string label, Room? room)
  {
    string line = Fill(label);
    return room is null ? line : line.Replace("{channel}", room.Label).Replace("{others}", string.Join(", ", room.Others));
  }

  public string Clock(DateTimeOffset at)
  {
    return TimeZoneInfo.ConvertTime(at, zone).ToString("dddd d MMMM, HH:mm", CultureInfo.InvariantCulture);
  }

  public string Moment(DateTimeOffset at)
  {
    DateTimeOffset local = TimeZoneInfo.ConvertTime(at, zone);
    return Text.Time.Replace("{time}", local.ToString("dddd d MMMM, HH:mm", CultureInfo.InvariantCulture));
  }

  private void AppendEntries(StringBuilder page, List<Entry> entries, int start, Surroundings world)
  {
    PageText labels = Text;
    DateTimeOffset? previous = start > 0 && start <= entries.Count ? entries[start - 1].At : null;
    for (int i = start; i < entries.Count; i++)
    {
      Entry entry = entries[i];
      if (previous is null || (entry.At - previous.Value).TotalMinutes >= timeGapMinutes)
      {
        page.Append(Moment(entry.At)).Append('\n');
      }
      previous = entry.At;
      switch (entry.Kind)
      {
        case EntryKinds.Thought:
          page.Append(Thought).Append(' ').Append(entry.Text).Append(labels.Close).Append('\n');
          break;
        case EntryKinds.Worked:
          page.Append(Fill(labels.Worked)).Append(' ').Append(entry.Text).Append(labels.Close).Append('\n');
          break;
        case EntryKinds.Recollection:
          page.Append(Fill(labels.Remember)).Append(' ').Append(entry.Text).Append(labels.Close).Append('\n');
          break;
        case EntryKinds.Message:
          // A message keeps its sender; the page shows whatever the person now calls them.
          page.Append(Said(NameOf(entry.Speaker, world), RoomOf(entry.Channel, world.Rooms))).Append(' ').Append(entry.Text).Append('\n');
          break;
        case EntryKinds.Said:
          page.Append(Said(entry.Speaker, RoomOf(entry.Channel, world.Rooms))).Append(' ').Append(entry.Text).Append('\n');
          break;
        default:
          page.Append(entry.Text).Append('\n');
          break;
      }
    }
  }

  private string NameOf(string member, Surroundings world)
  {
    return world.Names.TryGetValue(member, out string? known) ? known : Fill(Text.Human);
  }

  private Room RoomOf(string channel, Dictionary<string, Room> rooms)
  {
    if (rooms.TryGetValue(channel, out Room? room))
    {
      return room;
    }
    return new Room(channel, Fill(Text.Gone), false, new List<string>(), "", false);
  }

  private string Read(string file)
  {
    if (live is null)
    {
      throw new InvalidOperationException("No prompts were loaded for this operation.");
    }
    return live.Content.Get(file);
  }

  private static void RequireLabels(PageText page, ControlText control)
  {
    string[] labels = [page.Header, page.Portrait, page.Summary, page.Thought, page.Remember, page.Close, page.Said, page.Time, page.Direct, page.Group, page.Gone, page.Human, page.Buzz, page.Open, page.OpenEmpty, page.Earlier, page.Closed, page.Phone, page.PhoneEmpty, page.PhoneLine, page.NoConversation, page.Unavailable, page.Unprepared, page.Named, page.Elsewhere, page.Mention, page.Muted, page.Unmuted, page.Now, page.ScreenOpen, page.ScreenIdle, page.Reach, page.UnreadNone, page.Worked, control.Open, control.Conversation, control.Idle, control.Write, control.Check, control.Close, control.Think, control.Wait, control.Name, control.Mute, control.Unmute, control.Work];
    foreach (string label in labels)
    {
      if (string.IsNullOrWhiteSpace(label))
      {
        throw new ArgumentException("page.json or controls.json is missing a label this HH version needs.");
      }
    }
  }
}
