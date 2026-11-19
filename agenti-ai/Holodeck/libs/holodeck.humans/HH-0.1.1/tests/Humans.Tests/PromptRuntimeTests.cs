using Xunit;

namespace Humans.Tests;

public sealed class PromptRuntimeTests
{
  [Fact]
  public void ARunningPageKeepsItsOwnPromptsAcrossLiveEdits()
  {
    PromptBundle content = TestPrompts.Bundle("first instruction");
    PromptLive first = new("HH-0.1.1", 4, 2, 2, DateTimeOffset.UtcNow, content);
    Page foreground = Page();
    Page background = Page();
    foreground.Use(first);
    background.Use(first);

    content.Files["continue.txt"] = "edited after the operation started";
    background.Use(new PromptLive("HH-0.1.1", 5, null, 2, DateTimeOffset.UtcNow, TestPrompts.Bundle("new live instruction")));

    Assert.Equal("first instruction", foreground.Instruction("continue"));
    Assert.Equal("new live instruction", background.Instruction("continue"));
    Assert.Equal(4, foreground.Live!.Revision);
    Assert.Equal(5, background.Live!.Revision);
    Assert.Equal("search instruction", foreground.QueryInstruction);
    Assert.Equal("[Alex thinks:", foreground.Fill(foreground.Text.Thought));
  }

  [Fact]
  public void APageWithoutPromptsRefusesInsteadOfReadingFiles()
  {
    Assert.Throws<InvalidOperationException>(() => Page().Instruction("continue"));
  }

  [Fact]
  public void VersionsMustBeHumanReleaseNames()
  {
    Assert.Equal("HH-0.1.1", HumanVersion.Require("HH-0.1.1"));
    Assert.Throws<ArgumentException>(() => HumanVersion.Require("0.1.1"));
    Assert.Throws<ArgumentException>(() => HumanVersion.Require("HH-01.1.1"));
  }

  private static Page Page()
  {
    return new Page(new Personality("Alex", "curious"), "Person", "someone", "");
  }
}
