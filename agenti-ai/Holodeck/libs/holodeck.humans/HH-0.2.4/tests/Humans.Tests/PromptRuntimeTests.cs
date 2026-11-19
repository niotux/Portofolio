using Xunit;

namespace Humans.Tests;

public sealed class PromptRuntimeTests
{
  [Fact]
  public void ARunningPageKeepsItsOwnPromptsAcrossLiveEdits()
  {
    PromptBundle content = TestPrompts.Bundle("first instruction");
    PromptLive first = new("HH-0.2.0", 4, 2, 2, DateTimeOffset.UtcNow, content);
    Page foreground = Page();
    Page background = Page();
    foreground.Use(first);
    background.Use(first);

    content.Files["continue.txt"] = "edited after the operation started";
    background.Use(new PromptLive("HH-0.2.0", 5, null, 2, DateTimeOffset.UtcNow, TestPrompts.Bundle("new live instruction")));

    Assert.Equal("first instruction", foreground.Instruction("continue.txt"));
    Assert.Equal("new live instruction", background.Instruction("continue.txt"));
    Assert.Equal(4, foreground.Live!.Revision);
    Assert.Equal("[Alex thinks:", foreground.Thought);
  }

  [Fact]
  public void ASetThisVersionCannotRunIsRefusedAndTheOldOneKept()
  {
    Page page = Page();
    page.Use(new PromptLive("HH-0.2.0", 1, 1, 1, DateTimeOffset.UtcNow, TestPrompts.Bundle("kept")));
    PromptBundle missing = TestPrompts.Bundle("broken");
    missing.Files.Remove("hands.txt");
    Assert.Throws<ArgumentException>(() => page.Use(new PromptLive("HH-0.2.0", 2, null, 1, DateTimeOffset.UtcNow, missing)));
    Assert.Equal("kept", page.Instruction("continue.txt"));
    Assert.Throws<InvalidOperationException>(() => Page().Instruction("continue.txt"));
  }

  [Fact]
  public void ThePageRendersOneHistoryAcrossConversations()
  {
    Page page = Page();
    page.Use(new PromptLive("HH-0.2.0", 1, 1, 1, DateTimeOffset.UtcNow, TestPrompts.Bundle("x")));
    page.Portrait = "a carpenter";
    DateTimeOffset at = new(2026, 11, 14, 10, 0, 0, TimeSpan.Zero);
    Surroundings world = new(new Dictionary<string, Room> { ["a"] = new Room("a", "the chat with Ana", true, ["Ana"], "", false) }, new Dictionary<string, string> { ["ana-id"] = "Ana" });
    List<Entry> entries =
    [
      new Entry(1, EntryKinds.Event, "", "", "Buzz from the chat with Ana.", at),
      new Entry(2, EntryKinds.Message, "a", "ana-id", "Hello?", at),
      new Entry(3, EntryKinds.Thought, "", "", "She sounds worried.", at.AddMinutes(1)),
      new Entry(4, EntryKinds.Said, "a", "Alex", "I'm here.", at.AddMinutes(30)),
      new Entry(5, EntryKinds.Message, "gone", "stranger-id", "Anyone?", at.AddMinutes(31))
    ];
    string document = page.Document(new Summary(0, "I woke early."), entries, world);
    Assert.Contains("Who Alex is: a carpenter", document);
    Assert.Contains("[Before: I woke early.]", document);
    Assert.Contains("Ana in the chat with Ana: Hello?\n[Alex thinks: She sounds worried.]\n", document);
    Assert.Contains("(Saturday 14 November, 10:30)\nAlex in the chat with Ana: I'm here.", document);
    Assert.Contains("Visitor in a gone conversation: Anyone?", document);
  }

  [Fact]
  public void TagsAreTheNameAsAWordOrACallToEveryone()
  {
    Assert.True(Body.Mentions("Mara, are you there?", "Mara Ionescu"));
    Assert.True(Body.Mentions("ok @everyone, lunch", "Mara"));
    Assert.False(Body.Mentions("Tamara said hi", "Mara"));
    Assert.False(Body.Mentions("hi all", "Mara"));
  }

  [Fact]
  public void LookupsAndMemoriesAreLinesAndParagraphs()
  {
    Assert.Equal(["my brother", "the old house"], Recall.Lines("- my brother\n\n2. the old house\nthird", 2));
    Assert.Equal(["First.", "Second."], MemoryKeeper.Paragraphs("First.\n\n\n\nSecond.\n"));
  }

  [Fact]
  public void VersionsMustBeHumanReleaseNames()
  {
    Assert.Equal("HH-0.2.0", HumanVersion.Require("HH-0.2.0"));
    Assert.Throws<ArgumentException>(() => HumanVersion.Require("0.2.0"));
  }

  private static Page Page()
  {
    return new Page("Alex", TimeZoneInfo.Utc, 10);
  }
}
