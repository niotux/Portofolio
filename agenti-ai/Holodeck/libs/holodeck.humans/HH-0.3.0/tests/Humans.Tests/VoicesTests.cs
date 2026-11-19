using System.Text.Json;
using Xunit;

namespace Humans.Tests;

public sealed class VoicesTests
{
  [Fact]
  public async Task StreamingShowsOnlyOrdinaryProseAndNativeThinkingIsAlwaysDisabled()
  {
    StreamingHandler handler = new();
    ChatModel model = new(new HttpClient(handler), new ModelOptions("http://model.invalid/v1/", "test", "", 0.5, 100, 30));
    List<string> fragments = new();
    Completion result = await model.SayAsync([new Message("user", "hello")], CancellationToken.None, fragments.Add);
    Assert.Equal(2, fragments.Count);
    Assert.Equal("I feel curious. I should check my assumption.", result.Text);
    Assert.DoesNotContain("NATIVE", result.Text);
    Assert.Equal(14, result.Usage.PromptTokens);
    using JsonDocument body = JsonDocument.Parse(handler.Body!);
    Assert.Equal("none", body.RootElement.GetProperty("reasoning_effort").GetString());
    Assert.False(body.RootElement.TryGetProperty("chat_template_kwargs", out _));
    Assert.True(body.RootElement.GetProperty("stream").GetBoolean());
    Assert.False(body.RootElement.TryGetProperty("response_format", out _));
  }
}
