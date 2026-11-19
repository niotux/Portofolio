using System.Text.Json;
using CreativeStudio;
using Xunit;

namespace CreativeStudio.Tests;

public sealed class ContextBuilderTests
{
  [Fact]
  public void SelectsRelevantNotesAndRecentWholeExchangesWithinTheSharedAllowance()
  {
    ContextBuilder context = new(new ContextOptions(80, 4, 20, 1, 1024));
    List<ChatMessage> recent = [ChatMessage.Text("user", "old question"), ChatMessage.Text("assistant", "old answer"), ChatMessage.Text("user", "new question"), ChatMessage.Text("assistant", "new answer")];
    List<ChatMessage> notes = [ChatMessage.Text("user", new string('x', 21)), ChatMessage.Text("user", "sea-facing station")];
    ChatMessage current = ChatMessage.Text("user", "Make the next scene.");
    List<ChatMessage> selected = context.Build("creative", recent, current, notes, 5);
    Assert.Same(current, selected[selected.Count - 1]);
    Assert.Contains(selected, message => message.TextContent == "sea-facing station");
    Assert.DoesNotContain(selected, message => message.TextContent == new string('x', 21));
    Assert.True(ContextBuilder.CountCharacters(selected) <= 75);
    Assert.DoesNotContain(selected, message => message.TextContent == "old question");
  }

  [Fact]
  public void NewestToolExperienceHasPriorityAndSelectedExperiencesRemainChronological()
  {
    ContextBuilder context = new(new ContextOptions(100, 0, 20, 1, 1024));
    ChatMessage old = ChatMessage.Text("user", "old notebook hit");
    ChatMessage selected = ChatMessage.Text("user", "selected");
    ChatMessage latest = ChatMessage.Text("user", "latest");
    List<ChatMessage> messages = context.Build("creative", new List<ChatMessage>(), ChatMessage.Text("user", "continue"), [old, selected, latest]);
    Assert.DoesNotContain(old, messages);
    Assert.True(messages.IndexOf(selected) < messages.IndexOf(latest));
    Assert.Contains(latest, messages);
  }

  [Fact]
  public void ImagePayloadStaysOutOfProseCostAndNeverSerializesAssemblyMetadata()
  {
    string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".png");
    try
    {
      File.WriteAllBytes(path, [1, 2, 3]);
      ChatMessage image = ChatMessage.ImageUser("Inspect this reference.", path, 3);
      using JsonDocument wire = JsonDocument.Parse(JsonSerializer.Serialize(image, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
      Assert.Equal("image_url", wire.RootElement.GetProperty("content")[1].GetProperty("type").GetString());
      Assert.StartsWith("data:image/png;base64,", wire.RootElement.GetProperty("content")[1].GetProperty("image_url").GetProperty("url").GetString()!);
      Assert.False(wire.RootElement.TryGetProperty("textContent", out _));
      Assert.False(wire.RootElement.TryGetProperty("imageCount", out _));
      Assert.Throws<IOException>(() => ChatMessage.ImageUser("Inspect.", path, 2));
      ContextBuilder context = new(new ContextOptions(50, 2, 10, 0, 3));
      Assert.Throws<ContextLimitException>(() => context.Build("creative", new List<ChatMessage>(), image));
    }
    finally
    {
      File.Delete(path);
    }
  }
}
