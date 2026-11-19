using System.Net;
using System.Text;
using System.Text.Json;
using CreativeStudio;
using Xunit;

namespace CreativeStudio.Tests;

public sealed class ModelTransportTests
{
  [Fact]
  public async Task ConversationStreamsOnlyProseWithThinkingDisabledAndNoOperationFormat()
  {
    string? sent = null;
    using HttpClient http = new(new ModelTestHandler(async (request, token) =>
    {
      sent = await request.Content!.ReadAsStringAsync(token);
      string stream = "data: {\"choices\":[{\"delta\":{\"content\":\"The station \"}}]}\n\ndata: {\"choices\":[{\"delta\":{\"content\":\"faces the sea.\"},\"finish_reason\":\"stop\"}]}\n\ndata: {\"choices\":[],\"usage\":{\"prompt_tokens\":18,\"completion_tokens\":7}}\n\ndata: [DONE]\n\n";
      return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(stream, Encoding.UTF8, "text/event-stream") };
    }));
    CreativeModel model = new(http, Options());
    StringBuilder visible = new();
    ModelCompletion completion = await model.SayAsync([ChatMessage.Text("user", "Describe this scene.")], CancellationToken.None, value => visible.Append(value));
    Assert.Equal("The station faces the sea.", completion.Text);
    Assert.Equal(completion.Text, visible.ToString());
    Assert.Equal(18, completion.Usage.PromptTokens);
    using JsonDocument requestBody = JsonDocument.Parse(sent!);
    Assert.False(requestBody.RootElement.GetProperty("chat_template_kwargs").GetProperty("enable_thinking").GetBoolean());
    Assert.False(requestBody.RootElement.TryGetProperty("tools", out _));
    Assert.False(requestBody.RootElement.TryGetProperty("response_format", out _));
  }

  [Theory]
  [InlineData("data: {\"choices\":[{\"delta\":{\"content\":\"unfinished\"}}]}\n\n")]
  [InlineData("data: {\"choices\":[{\"delta\":{\"content\":\"unfinished\"},\"finish_reason\":\"length\"}]}\n\ndata: [DONE]\n\n")]
  public async Task InterruptedAndTruncatedStreamsAreNotReturnedAsFinishedReplies(string stream)
  {
    using HttpClient http = new(new ModelTestHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(stream, Encoding.UTF8, "text/event-stream") })));
    CreativeModel model = new(http, Options());
    await Assert.ThrowsAsync<IOException>(() => model.SayAsync([ChatMessage.Text("user", "Continue.")], CancellationToken.None, _ => { }));
  }

  [Fact]
  public async Task FocusedOperationUsesOnlyAvailableToolAndKeepsArgumentsAfterResponseDisposal()
  {
    string? sent = null;
    using HttpClient http = new(new ModelTestHandler(async (request, token) =>
    {
      sent = await request.Content!.ReadAsStringAsync(token);
      return JsonResponse("""
        {"choices":[{"finish_reason":"tool_calls","message":{"content":"","tool_calls":[{"id":"call-1","type":"function","function":{"name":"remember","arguments":"{\"title\":\"Station\",\"text\":\"The windows face the sea.\"}"}}]}}],"usage":{"prompt_tokens":30,"completion_tokens":12}}
        """);
    }));
    CreativeModel model = new(http, Options());
    using JsonDocument controls = JsonDocument.Parse("""{"type":"object","properties":{"title":{"type":"string"},"text":{"type":"string"}},"required":["title","text"]}""");
    ModelAction action = await model.ActAsync([ChatMessage.Text("user", "Keep the station detail.")], [ToolDefinition.Create("remember", "Keep a prose note.", controls.RootElement)], CancellationToken.None);
    ToolCall call = Assert.Single(action.Calls);
    Assert.Equal("remember", call.Name);
    Assert.Equal("The windows face the sea.", call.Arguments.GetProperty("text").GetString());
    using JsonDocument requestBody = JsonDocument.Parse(sent!);
    Assert.Equal("auto", requestBody.RootElement.GetProperty("tool_choice").GetString());
    Assert.False(requestBody.RootElement.GetProperty("chat_template_kwargs").GetProperty("enable_thinking").GetBoolean());
    Assert.False(requestBody.RootElement.TryGetProperty("response_format", out _));
  }

  [Theory]
  [InlineData("unknown", "{}")]
  [InlineData("remember", "{\"title\":\"one\",\"title\":\"two\"}")]
  [InlineData("remember", "[]")]
  public async Task UnavailableAndMalformedOperationsAreRejected(string name, string arguments)
  {
    string response = "{\"choices\":[{\"finish_reason\":\"tool_calls\",\"message\":{\"tool_calls\":[{\"type\":\"function\",\"function\":{\"name\":" + JsonSerializer.Serialize(name) + ",\"arguments\":" + JsonSerializer.Serialize(arguments) + "}}]}}]}";
    using HttpClient http = new(new ModelTestHandler((_, _) => Task.FromResult(JsonResponse(response))));
    CreativeModel model = new(http, Options());
    using JsonDocument controls = JsonDocument.Parse("""{"type":"object"}""");
    await Assert.ThrowsAsync<IOException>(() => model.ActAsync([ChatMessage.Text("user", "Use the notebook.")], [ToolDefinition.Create("remember", "Keep a note.", controls.RootElement)], CancellationToken.None));
  }

  [Fact]
  public async Task EmbeddingsUseConfiguredDimensionsAndRejectZeroVectors()
  {
    int requests = 0;
    using HttpClient http = new(new ModelTestHandler((_, _) =>
    {
      requests++;
      string json = requests == 1 ? "{\"data\":[{\"embedding\":[0.3,0.4,0.8]}]}" : "{\"data\":[{\"embedding\":[0,0,1]}]}";
      return Task.FromResult(JsonResponse(json));
    }));
    EmbeddingClient embeddings = new(http, new EmbeddingOptions("http://embedding.test/v1/", "notes", "", 2, 30));
    float[] vector = await embeddings.EmbedAsync("The station faces the sea.", CancellationToken.None);
    Assert.Equal(new float[] { 0.3f, 0.4f }, vector);
    await Assert.ThrowsAsync<IOException>(() => embeddings.EmbedAsync("A grounded note.", CancellationToken.None));
  }

  private static ModelOptions Options() => new("http://model.test/v1/", "creative", "", 0.3, 500, 30);

  private static HttpResponseMessage JsonResponse(string json)
  {
    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
  }
}
