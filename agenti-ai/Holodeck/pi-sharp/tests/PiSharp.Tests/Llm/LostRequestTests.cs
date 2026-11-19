using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using PiSharp.Core.Agent;
using PiSharp.Core.Messages;
using PiSharp.Core.Models;
using PiSharp.Llm.Http;
using PiSharp.Llm.OpenAi;

namespace PiSharp.Tests.Llm;

/// <summary>
/// Every seat the hub's stall recovery restarted between 2026-10-22 and 10-28 sat 30-35 minutes on an open connection to the
/// router with no answer, while the router had no request running that long (digital-scribe, 2026-10-28: "agents going quiet
/// and holding work... how do we prevent and address that?"). The client asks the router about the request while it waits.
/// </summary>
public sealed class LostRequestTests
{
    /// <summary>The first POST never answers; later ones stream "ok". GET /requests/{id} answers with <see cref="State"/>.</summary>
    private sealed class Router(Func<string, int, string?> state) : HttpMessageHandler
    {
        public int Posts, Probes;
        public List<string> Ids { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method == HttpMethod.Get)
            {
                var id = request.RequestUri!.AbsolutePath.Split('/')[^1];
                var answer = state(id, ++Probes);
                return answer is null
                    ? new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("Not Found") }
                    : new HttpResponseMessage(answer == "unknown" ? HttpStatusCode.NotFound : HttpStatusCode.OK)
                        { Content = new StringContent(new JsonObject { ["state"] = answer }.ToJsonString(), Encoding.UTF8, "application/json") };
            }
            Ids.Add(request.Headers.GetValues(OpenAiCompletionsClient.RequestIdHeader).Single());
            if (++Posts == 1) await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"ok\"},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n", Encoding.UTF8, "text/event-stream"),
            };
        }
    }

    private static ModelConfig Model() => new() { Id = "qwen", BaseUrl = "http://router.test/v1", ApiKey = "k", ContextWindow = 200000, MaxOutputTokens = 8192 };

    private static OpenAiCompletionsClient Client(Router router) => new(new HttpClient(router))
    {
        Retry = RetryPolicy.Patient, RequestProbeEvery = TimeSpan.FromMinutes(5), RequestProbeDelay = (_, ct) => Task.Delay(1, ct),
    };

    private static async Task<List<AssistantMessageEvent>> Run(OpenAiCompletionsClient client)
    {
        var events = new List<AssistantMessageEvent>();
        using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await foreach (var e in client.Stream(Model(), new LlmContext("s", [new UserMessage("hi")], []), new StreamOptions(), guard.Token)) events.Add(e);
        return events;
    }

    [Theory]
    [InlineData("finished")]
    [InlineData("unknown")]
    public async Task ARequestTheRouterNoLongerHasIsSentAgain(string state)
    {
        var router = new Router((_, _) => state);
        var events = await Run(Client(router));
        Assert.IsType<AssistantDone>(events[^1]);
        Assert.Equal(2, router.Posts);
        Assert.NotEqual(router.Ids[0], router.Ids[1]);
        Assert.Equal(state == "finished" ? 1 : 2, router.Probes);
    }

    [Fact]
    public async Task ARequestTheRouterStillWorksOnIsWaitedFor_AndAServerThatCannotSayIsNotAskedAgain()
    {
        // Working three times, then gone: the client waited through the three and resent after two unknowns.
        var router = new Router((_, probe) => probe <= 3 ? "working" : "unknown");
        Assert.IsType<AssistantDone>((await Run(Client(router)))[^1]);
        Assert.Equal(5, router.Probes);
        Assert.Equal(2, router.Posts);

        // A plain model server answers the probe with a 404 page: one probe, then the wait goes on as before.
        var plain = new Router((_, _) => null);
        var client = Client(plain);
        using var shortWait = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in client.Stream(Model(), new LlmContext("s", [new UserMessage("hi")], []), new StreamOptions(), shortWait.Token)) { }
        });
        Assert.Equal(1, plain.Probes);
        Assert.Equal(1, plain.Posts);
    }
}
