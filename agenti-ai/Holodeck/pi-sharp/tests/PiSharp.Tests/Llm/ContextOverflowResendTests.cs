using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using PiSharp.Core.Agent;
using PiSharp.Core.Messages;
using PiSharp.Core.Models;
using PiSharp.Llm.Http;
using PiSharp.Llm.OpenAi;

namespace PiSharp.Tests.Llm;

/// <summary>demo-01, 2026-10-21: the agent loop asks for min(cap, window - its estimate of the prompt), and vLLM counted one
/// token more than the estimate: "you requested 131072 output tokens and your prompt contains at least 68929 input tokens,
/// for a total of at least 200001 tokens". The client asks once more for exactly what the server says is left.</summary>
public sealed class ContextOverflowResendTests
{
    private const string Refusal = """{"object":"error","message":"This model's maximum context length is 200000 tokens. However, you requested 131072 output tokens and your prompt contains at least 68929 input tokens, for a total of at least 200001 tokens. Please reduce the length of the input prompt or the number of requested output tokens. (parameter=input_tokens, value=68929)","type":"BadRequestError","param":"input_tokens","code":400}""";

    private sealed class Server(Func<int, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<JsonObject> Bodies { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Bodies.Add(JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!.AsObject());
            return answer(Bodies.Count);
        }
    }

    private static HttpResponseMessage Sse() => new(HttpStatusCode.OK)
    {
        Content = new StringContent("data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"ok\"},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n", Encoding.UTF8, "text/event-stream"),
    };

    private static ModelConfig Model() => new() { Id = "miniclaude", BaseUrl = "http://vllm.test/v1", ApiKey = "k", ContextWindow = 200000, MaxOutputTokens = 131072 };

    private static LlmContext Context() => new("s", [new UserMessage("hi")], []);

    [Fact]
    public async Task AWindowRefusalIsResentOnceAskingForExactlyWhatTheServerSaysIsLeft()
    {
        var server = new Server(n => n == 1 ? new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent(Refusal) } : Sse());
        var client = new OpenAiCompletionsClient(new HttpClient(server)) { Retry = RetryPolicy.None };
        var events = new List<AssistantMessageEvent>();
        await foreach (var e in client.Stream(Model(), Context(), new StreamOptions { MaxTokens = 131072 })) events.Add(e);

        Assert.IsType<AssistantDone>(events[^1]);
        Assert.Equal(2, server.Bodies.Count);
        Assert.Equal(131072, (int)server.Bodies[0]["max_tokens"]!);
        Assert.Equal(200000 - 68929, (int)server.Bodies[1]["max_tokens"]!);
    }

    [Fact]
    public async Task OnlyOnceAndOnlyForThatRefusal()
    {
        var always = new Server(_ => new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent(Refusal) });
        var client = new OpenAiCompletionsClient(new HttpClient(always)) { Retry = RetryPolicy.None };
        var events = new List<AssistantMessageEvent>();
        await foreach (var e in client.Stream(Model(), Context(), new StreamOptions { MaxTokens = 131072 })) events.Add(e);
        Assert.IsType<AssistantError>(events[^1]);
        Assert.Equal(2, always.Bodies.Count); // one resend, then the error goes to the loop as before

        Assert.Null(OpenAiCompletionsClient.OutputRoom("HTTP 400 Bad Request: invalid tool schema"));
        Assert.Null(OpenAiCompletionsClient.OutputRoom("maximum context length is 200000 tokens. However, you requested 1000 output tokens and your prompt contains at least 250000 input tokens"));
        Assert.Equal(131071, OpenAiCompletionsClient.OutputRoom(Refusal));
    }
}
