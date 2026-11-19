using System.Text.Json;
using Xunit;

namespace Humans.Tests;

public sealed class ChatModelTests
{
  [Theory]
  [InlineData("https://api.groq.com/openai/v1/", "https://api.groq.com/openai/v1/chat/completions")]
  [InlineData("https://api.openai.com/v1", "https://api.openai.com/v1/chat/completions")]
  public async Task PageContinuationUsesAuthenticatedChatCompletions(string baseUrl, string expectedUrl)
  {
    StreamingHandler handler = new();
    ChatModel model = new(new HttpClient(handler), new ModelOptions(baseUrl, "configured-model", "test-key", 0.5, 800, 30));
    List<string> fragments = new();
    const string Page = "Someone: Hello.\n[Person thinks:";
    const string Instruction = "Continue this page in the person's own voice.";
    List<string> stop = ["]", "\nSomeone:", "\n\n"];
    Completion result = await model.ContinueAsync(Page, Instruction, stop, 160, CancellationToken.None, fragments.Add, 0.3);

    Assert.Equal(expectedUrl, handler.Address!.AbsoluteUri);
    Assert.Equal("Bearer test-key", handler.Authorization);
    Assert.Equal("I feel curious. I should check my assumption.", result.Text);
    Assert.Equal(result.Text, string.Concat(fragments));
    Assert.Equal(new Usage(14, 9), result.Usage);
    using JsonDocument request = JsonDocument.Parse(handler.Body!);
    JsonElement body = request.RootElement;
    Assert.Equal("configured-model", body.GetProperty("model").GetString());
    Assert.Equal(160, body.GetProperty("max_completion_tokens").GetInt32());
    Assert.Equal(0.3, body.GetProperty("temperature").GetDouble());
    Assert.Equal("none", body.GetProperty("reasoning_effort").GetString());
    Assert.True(body.GetProperty("stream_options").GetProperty("include_usage").GetBoolean());
    Assert.Equal(2, body.GetProperty("messages").GetArrayLength());
    Assert.Equal("system", body.GetProperty("messages")[0].GetProperty("role").GetString());
    Assert.Equal(Instruction, body.GetProperty("messages")[0].GetProperty("content").GetString());
    Assert.Equal("user", body.GetProperty("messages")[1].GetProperty("role").GetString());
    Assert.Equal(Page, body.GetProperty("messages")[1].GetProperty("content").GetString());
    Assert.Equal(3, body.GetProperty("stop").GetArrayLength());
    for (int index = 0; index < stop.Count; index++)
    {
      Assert.Equal(stop[index], body.GetProperty("stop")[index].GetString());
    }
    Assert.False(body.TryGetProperty("prompt", out _));
    Assert.False(body.TryGetProperty("n_predict", out _));
    Assert.False(body.TryGetProperty("repeat_penalty", out _));
    Assert.False(body.TryGetProperty("cache_prompt", out _));
    Assert.False(body.TryGetProperty("chat_template_kwargs", out _));
  }

  [Fact]
  public async Task SpeechUsesConfiguredOutputLimitWhenNoOverrideIsGiven()
  {
    StreamingHandler handler = new();
    ChatModel model = new(new HttpClient(handler), new ModelOptions("http://model.invalid/v1/", "test", "", 0.6, 800, 30));
    await model.ContinueAsync("Person:", "Continue the page.", ["\n\n"], 0, CancellationToken.None);
    using JsonDocument request = JsonDocument.Parse(handler.Body!);
    Assert.Equal(800, request.RootElement.GetProperty("max_completion_tokens").GetInt32());
    Assert.Equal(0.6, request.RootElement.GetProperty("temperature").GetDouble());
    Assert.Null(handler.Authorization);
  }

  [Fact]
  public async Task UtilityRequestsOmitStreamingOptionsAndStopWhenUnused()
  {
    ProseHandler handler = new("A memory in my own words.");
    ChatModel model = new(new HttpClient(handler), new ModelOptions("http://model.invalid/v1/", "test", "", 0.6, 800, 30));
    Completion result = await model.SayAsync([new Message("user", "remember")], CancellationToken.None);
    Assert.Equal("A memory in my own words.", result.Text);
    Assert.Equal(new Usage(10, 10), result.Usage);
    using JsonDocument request = JsonDocument.Parse(handler.Body!);
    Assert.False(request.RootElement.GetProperty("stream").GetBoolean());
    Assert.False(request.RootElement.TryGetProperty("stream_options", out _));
    Assert.False(request.RootElement.TryGetProperty("stop", out _));
    Assert.False(request.RootElement.TryGetProperty("chat_template_kwargs", out _));
  }

  [Fact]
  public async Task InterruptedPageStreamFailsInsteadOfSavingPartialText()
  {
    StreamingHandler handler = new(false);
    ChatModel model = new(new HttpClient(handler), new ModelOptions("http://model.invalid/v1/", "test", "", 0.6, 800, 30));
    await Assert.ThrowsAsync<IOException>(() => model.ContinueAsync("Person:", "Continue the page.", ["\n\n"], 0, CancellationToken.None));
  }
}
