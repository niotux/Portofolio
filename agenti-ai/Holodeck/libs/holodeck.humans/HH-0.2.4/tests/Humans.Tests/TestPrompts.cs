using System.Text.Json;

namespace Humans.Tests;

// A complete, plain prompt set for tests. Real prompts live only in the shared database.
public static class TestPrompts
{
  public static PromptBundle Bundle(string instruction)
  {
    PageText page = new("{name}'s life.", "Who {name} is: {portrait}", "[Before:", "[{name} thinks:", "[{name} remembers:", "]", "{speaker} in {channel}:", "({time})", "the chat with {others}", "#{channel}", "a gone conversation", "Visitor", "Buzz from {channel}.", "{name} opens {channel}.", "{name} opens {channel}; nothing new.", "{count} earlier in {channel}.", "{name} closes {channel}.", "Phone: {list}.", "Phone: nothing.", "{count} in {channel} from {senders}", "Nothing is open.", "No {channel} here.", "Nothing is given.", "{name} saves {contact}.", "Not for {channel}.", "Mention in {channel}.", "Muted {channel}.", "Unmuted {channel}.", "(Now: {time}. {screen} {reach} {unread})", "{channel} is open.", "Nothing is open.", "Reachable: {list}.", "Nothing unread.", "[{name} works it out:");
    ControlText controls = new("Open one.", "Open now: {focus}", "Nothing open.", "Write in {focus}.", "Check the phone.", "Close {focus}.", "Think on.", "Wait.", "Name {unknown} in {focus}.", "Mute {focus}.", "Unmute {focus}.", "Work it out.");
    Dictionary<string, string> files = new()
    {
      ["page.json"] = JsonSerializer.Serialize(page),
      ["controls.json"] = JsonSerializer.Serialize(controls),
      ["continue.txt"] = instruction,
      ["hands.txt"] = "hands instruction",
      ["look.txt"] = "look instruction",
      ["search.txt"] = "search instruction",
      ["remember.txt"] = "remember instruction",
      ["form.txt"] = "form instruction",
      ["compact.txt"] = "compact instruction",
      ["prepare.txt"] = "prepare instruction",
      ["lives.txt"] = "lives instruction",
      ["work.txt"] = "work instruction",
      ["check.txt"] = "check instruction"
    };
    return new PromptBundle(files);
  }
}
