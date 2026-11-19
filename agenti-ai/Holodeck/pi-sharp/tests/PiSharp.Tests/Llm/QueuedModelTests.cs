using System.IO.Pipelines;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using PiSharp.Core.Agent;
using PiSharp.Core.Messages;
using PiSharp.Core.Models;
using PiSharp.Llm;
using PiSharp.Llm.Http;
using PiSharp.Llm.OpenAi;

namespace PiSharp.Tests.Llm;

/// <summary>
/// 2026-10-29: team-lead-2 waited 14:03-14:41, and again after its restart, on one routine turn to qwen3-8-flash, a one-slot
/// llama.cpp on strata. llama.cpp opened the stream at once and kept the request in its queue; after ten minutes without
/// content the client gave up and sent it again at once, to the back of the same queue, without a line (digital-scribe:
/// "let's do all this properly, no quick fixes"). The router now says where a request stands (queued #n, working); the client
/// waits on a request the router still has, tells what it waits on, and says why whenever it sends one again.
/// </summary>
public sealed class QueuedModelTests
{
    private static ModelConfig Model() => new() { Id = "qwen3-8-flash", BaseUrl = "http://router.test/v1", ApiKey = "k", ContextWindow = 200000, MaxOutputTokens = 8192 };

    private const string Ok = "data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"ok\"},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n";

    /// <summary>A router: POSTs answered by <see cref="Post"/>, GET /requests/{id} by <see cref="State"/>.</summary>
    private sealed class Router : HttpMessageHandler
    {
        public Func<int, CancellationToken, Task<HttpResponseMessage>> Post { get; init; } = (_, _) => Task.FromResult(Sse(Ok));
        public Func<int, (string State, int? Position)?> State { get; init; } = _ => ("working", null);
        public int Posts, Probes;
        public List<HttpRequestMessage> Sent { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method == HttpMethod.Get)
            {
                var answer = State(Interlocked.Increment(ref Probes));
                if (answer is not { } a) return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("Not Found") };
                var json = new JsonObject { ["state"] = a.State };
                if (a.Position is { } p) json["position"] = p;
                return new HttpResponseMessage(a.State == "unknown" ? HttpStatusCode.NotFound : HttpStatusCode.OK)
                    { Content = new StringContent(json.ToJsonString(), Encoding.UTF8, "application/json") };
            }
            lock (Sent) Sent.Add(request);
            return await Post(Interlocked.Increment(ref Posts), ct);
        }
    }

    private static HttpResponseMessage Sse(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "text/event-stream") };

    private static async Task<List<AssistantMessageEvent>> Run(OpenAiCompletionsClient client, int seconds = 20)
    {
        var events = new List<AssistantMessageEvent>();
        using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
        await foreach (var e in client.Stream(Model(), new LlmContext("s", [new UserMessage("hi")], []), new StreamOptions(), guard.Token)) events.Add(e);
        return events;
    }

    [Fact]
    public async Task A_request_the_router_holds_in_a_backends_queue_is_waited_on_and_what_it_waits_on_is_told()
    {
        var release = new TaskCompletionSource();
        var router = new Router
        {
            Post = async (_, ct) => { await release.Task.WaitAsync(ct); return Sse(Ok); },
            State = probe =>
            {
                if (probe >= 3) release.TrySetResult();
                return probe switch { 1 => ("queued", 3), 2 => ("queued", 1), _ => ("working", null) };
            },
        };
        var told = new List<ModelWait>();
        var client = new OpenAiCompletionsClient(new HttpClient(router))
        {
            Retry = RetryPolicy.Patient, RequestProbeEvery = TimeSpan.FromMinutes(5), RequestProbeDelay = (_, ct) => Task.Delay(30, ct),
            OnWait = w => { lock (told) told.Add(w); }, WaitNoticeAfter = TimeSpan.FromMilliseconds(1),
        };

        var events = await Run(client);

        Assert.IsType<AssistantDone>(events[^1]);
        Assert.Equal(1, router.Posts); // queued is waited on: before 2026-10-29 an unknown answer ended the asking
        var states = told.Select(w => (w.State, w.Position)).ToList();
        Assert.Contains(("queued", (int?)3), states);
        Assert.Contains(("queued", (int?)1), states);
        Assert.Equal("done", told[^1].State);
        Assert.Equal("waiting on qwen3-8-flash since " + told[0].Since.UtcDateTime.ToString("HH:mm") + "Z (queued #3)",
            told.First(w => w.Position == 3).Line);
        Assert.Null(told[^1].Line);
    }

    /// <summary>2026-10-30: pi13 waited 40 minutes on one local answer that streamed only thinking; its status said it was
    /// working on a message and it was restarted as frozen. A long answer is told as it streams: "receiving", how much, and
    /// whether the model thinks, then done.</summary>
    [Fact]
    public async Task A_long_answer_is_told_as_it_streams_with_how_much_came_and_whether_the_model_thinks()
    {
        var pipe = new Pipe();
        var router = new Router
        {
            Post = (_, _) =>
            {
                var content = new StreamContent(pipe.Reader.AsStream());
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
            },
        };
        var told = new List<ModelWait>();
        var client = new OpenAiCompletionsClient(new HttpClient(router))
        {
            Retry = RetryPolicy.Patient, OnWait = w => { lock (told) told.Add(w); }, WaitNoticeAfter = TimeSpan.FromMilliseconds(40),
        };
        var thinking = "data: {\"choices\":[{\"index\":0,\"delta\":{\"reasoning_content\":\"" + new string('x', 400) + "\"}}]}\n\n";
        _ = Task.Run(async () =>
        {
            for (var i = 0; i < 6; i++) { await pipe.Writer.WriteAsync(Encoding.UTF8.GetBytes(thinking)); await Task.Delay(30); }
            await pipe.Writer.WriteAsync(Encoding.UTF8.GetBytes(Ok));
            await pipe.Writer.CompleteAsync();
        });

        var events = await Run(client);

        Assert.IsType<AssistantDone>(events[^1]);
        List<ModelWait> seen;
        lock (told) seen = [.. told];
        var receiving = seen.Where(w => w.State == "receiving").ToList();
        Assert.NotEmpty(receiving);
        Assert.True(receiving[^1].Chars >= receiving[0].Chars && receiving[^1].Chars > 0);
        Assert.Contains(receiving, w => w.Thinking);
        Assert.Equal("done", seen[^1].State);
        Assert.Null(seen[^1].Line);
    }

    [Fact]
    public void A_receiving_line_says_the_model_how_much_came_and_whether_it_thinks()
    {
        var since = new DateTimeOffset(2026, 10, 30, 4, 12, 0, TimeSpan.Zero);
        Assert.Equal("receiving from miniclaude since 04:12Z: ~12k tokens so far (thinking)", new ModelWait("miniclaude", since, "receiving", null, 48_000, true).Line);
        Assert.Equal("receiving from miniclaude since 04:12Z: ~1.2k tokens so far", new ModelWait("miniclaude", since, "receiving", null, 4_800).Line);
        Assert.Equal("receiving from miniclaude since 04:12Z: ~300 tokens so far", new ModelWait("miniclaude", since, "receiving", null, 1_200).Line);
        Assert.Equal("waiting on miniclaude since 04:12Z (the model has it)", new ModelWait("miniclaude", since, "working").Line);
    }

    [Fact]
    public async Task A_stream_that_gives_nothing_while_the_router_works_on_it_is_waited_on_not_sent_again()
    {
        var pipe = new Pipe();
        var router = new Router
        {
            Post = (_, _) =>
            {
                var content = new StreamContent(pipe.Reader.AsStream());
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
            },
            State = probe =>
            {
                if (probe == 2) _ = Task.Run(async () => { await pipe.Writer.WriteAsync(Encoding.UTF8.GetBytes(Ok)); await pipe.Writer.CompleteAsync(); });
                return ("working", null);
            },
        };
        var log = new List<string>();
        var client = new OpenAiCompletionsClient(new HttpClient(router))
        {
            Retry = RetryPolicy.Patient, BodyTimeout = TimeSpan.FromMilliseconds(150), Log = l => { lock (log) log.Add(l); },
            AskRouterBeforeResend = true,
        };

        var events = await Run(client);

        Assert.IsType<AssistantDone>(events[^1]);
        Assert.Equal(1, router.Posts);
        Assert.True(router.Probes >= 2);
        Assert.Contains(log, l => l.Contains("the router says working, so it is waited on, not sent again"));
    }

    [Fact]
    public async Task A_stream_that_gives_nothing_and_the_router_no_longer_has_is_sent_again_and_the_seat_log_says_why()
    {
        var router = new Router
        {
            Post = (post, _) =>
            {
                if (post > 1) return Task.FromResult(Sse(Ok));
                var silent = new StreamContent(new Pipe().Reader.AsStream());
                silent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = silent });
            },
            State = _ => ("unknown", null),
        };
        var log = new List<string>();
        var client = new OpenAiCompletionsClient(new HttpClient(router))
        {
            Retry = RetryPolicy.Patient, BodyTimeout = TimeSpan.FromMilliseconds(150), Log = l => { lock (log) log.Add(l); },
            AskRouterBeforeResend = true,
        };

        var events = await Run(client);

        Assert.IsType<AssistantDone>(events[^1]);
        Assert.Equal(2, router.Posts);
        var line = Assert.Single(log, l => l.Contains("sending it again"));
        Assert.Contains("the router could not say it still works on it", line);
        Assert.Contains("request pi-", line);
    }

    [Fact]
    public async Task A_busy_answer_from_the_router_is_final_for_a_caller_that_can_go_elsewhere()
    {
        var router = new Router
        {
            Post = (_, _) =>
            {
                var busy = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {
                    Content = new StringContent("""{"error":{"message":"provider 'strata-1-11' is busy","type":"server_error","code":"backend_busy"}}""", Encoding.UTF8, "application/json"),
                };
                busy.Headers.TryAddWithoutValidation("retry-after", "30");
                return Task.FromResult(busy);
            },
        };
        var client = new OpenAiCompletionsClient(new HttpClient(router))
        {
            Retry = RetryPolicy.Patient, BusyIsFinal = true,
            Headers = new Dictionary<string, string> { [OpenAiCompletionsClient.QueueTimeoutHeader] = "120000" },
        };

        var events = await Run(client, seconds: 10);

        var error = Assert.IsType<AssistantError>(events[^1]);
        Assert.Equal(StopReason.Error, error.Reason);
        Assert.Equal(1, router.Posts);
        Assert.Equal("120000", router.Sent[0].Headers.GetValues(OpenAiCompletionsClient.QueueTimeoutHeader).Single());
    }

    [Fact]
    public async Task A_seat_that_can_fail_over_tells_a_failed_local_turn_and_an_answered_one_and_asks_for_a_short_queue_wait()
    {
        var failing = true;
        var router = new Router
        {
            Post = (_, _) =>
            {
                if (!failing) return Task.FromResult(Sse(Ok));
                var busy = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("""{"error":{"message":"busy","code":"backend_busy"}}""", Encoding.UTF8, "application/json") };
                busy.Headers.TryAddWithoutValidation("retry-after", "30");
                return Task.FromResult(busy);
            },
        };
        var troubles = new List<string>();
        var answers = 0;
        using var seat = new SubscriptionRouter(null)
        {
            LocalHttp = new HttpClient(router),
            FailOverOnLocalTrouble = () => true,
            OnLocalTrouble = troubles.Add,
            OnLocalAnswer = () => answers++,
            LocalQueueLimit = TimeSpan.FromSeconds(90),
        };
        var model = Model() with { Provider = "openai-completions" };

        await foreach (var _ in seat.Stream(model, new LlmContext("s", [new UserMessage("hi")], []), new StreamOptions(), default)) { }
        failing = false;
        await foreach (var _ in seat.Stream(model, new LlmContext("s", [new UserMessage("hi")], []), new StreamOptions(), default)) { }

        Assert.Single(troubles);
        Assert.Equal(1, answers);
        Assert.Equal("90000", router.Sent[0].Headers.GetValues(OpenAiCompletionsClient.QueueTimeoutHeader).Single());
    }
}
