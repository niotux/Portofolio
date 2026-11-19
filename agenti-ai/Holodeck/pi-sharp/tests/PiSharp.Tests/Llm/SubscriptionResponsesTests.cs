using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PiSharp.Core.Agent;
using PiSharp.Core.Json;
using PiSharp.Core.Messages;
using PiSharp.Core.Models;
using PiSharp.Llm.Auth;
using PiSharp.Llm.Responses;

namespace PiSharp.Tests.Llm;

public class SubscriptionResponsesTests
{
    internal static ModelConfig Model(string provider = "openai-codex") => new()
    { Id = "model", Provider = provider, Connection = "personal", BaseUrl = "https://untrusted.invalid", Reasoning = true, Input = [ModelInput.Text, ModelInput.Image] };

    [Fact]
    public async Task StreamsToolCallThenReplaysProviderSignaturesAndToolResultAfterSessionSerialization()
    {
        var output = new JsonArray(
            JsonNode.Parse("""{"type":"reasoning","id":"rs_1","summary":[{"text":"Plan"}],"encrypted_content":"encrypted-state"}"""),
            JsonNode.Parse("""{"type":"function_call","id":"fc_1","call_id":"call_1","name":"read","arguments":"{\"path\":\"a.txt\"}"}"""));
        var payload = Event("response.output_item.added", 0, new JsonObject { ["type"] = "reasoning", ["id"] = "rs_1" })
            + Delta("response.reasoning_summary_text.delta", 0, "Plan")
            + Event("response.output_item.done", 0, output[0]!.DeepClone())
            + Event("response.output_item.added", 1, JsonNode.Parse("""{"type":"function_call","id":"fc_1","call_id":"call_1","name":"read","arguments":""}""")!)
            + Delta("response.function_call_arguments.delta", 1, "{\"path\":\"a.txt\"}")
            + Event("response.output_item.done", 1, output[1]!.DeepClone())
            + Terminal(output);
        using var http = new HttpClient(new CeilingFakeHandler(async request =>
        {
            Assert.Equal("https://chatgpt.com/backend-api/codex/responses", request.RequestUri!.AbsoluteUri);
            Assert.Equal("oauth-secret", request.Headers.Authorization!.Parameter);
            Assert.Equal("account", request.Headers.GetValues("chatgpt-account-id").Single());
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!;
            Assert.Equal("system", body["instructions"]!.GetValue<string>());
            Assert.Null(body["max_output_tokens"]);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(payload, Encoding.UTF8, "text/event-stream") };
        }));
        var source = new Source(SubscriptionProvider.Codex);
        var client = new SubscriptionResponsesClient(http, source);
        var events = await Collect(client.Stream(Model(), new("system", [new UserMessage("hello")], []), new(), default));
        var done = Assert.IsType<AssistantDone>(events.Last());
        Assert.Equal(StopReason.ToolUse, done.Reason);
        Assert.Single(events.OfType<ToolCallEnd>());
        Assert.Equal("a.txt", done.Message.ToolCalls.Single().Arguments["path"]!.GetValue<string>());
        Assert.Equal(80, done.Message.Usage.Input);
        Assert.Equal(20, done.Message.Usage.CacheRead);
        var saved = JsonSerializer.Serialize<Message>(done.Message, CoreJsonContext.Default.Message);
        var restored = (AssistantMessage)JsonSerializer.Deserialize(saved, CoreJsonContext.Default.Message)!;
        var requestBody = ResponsesRequest.Build(Model(), new("system", [restored, new ToolResultMessage
        { ToolCallId = "call_1|fc_1", ToolName = "read", Content = [new TextContent("file content"), PiSharp.Tests.Tools.ImageFixtures.Png()] }], []), new());
        var input = requestBody["input"]!.AsArray();
        Assert.Equal("encrypted-state", input[0]!["encrypted_content"]!.GetValue<string>());
        Assert.Equal("fc_1", input[1]!["id"]!.GetValue<string>());
        Assert.Equal("call_1", input[2]!["call_id"]!.GetValue<string>());
        Assert.Equal("input_image", input[2]!["output"]![1]!["type"]!.GetValue<string>());
        var foreign = ResponsesRequest.Build(Model("xai"), new("", [restored], []), new());
        Assert.DoesNotContain("encrypted-state", foreign.ToJsonString());
        Assert.DoesNotContain("fc_1", foreign.ToJsonString());
    }

    [Fact]
    public async Task CompletedOnlyTextPreservesPhaseWithoutDuplicateDeltas()
    {
        var output = new JsonArray(JsonNode.Parse("""{"type":"message","id":"msg_1","role":"assistant","phase":"final_answer","content":[{"type":"output_text","text":"Hello"}]}"""));
        using var http = Http(Terminal(output));
        var client = new SubscriptionResponsesClient(http, new Source(SubscriptionProvider.Xai));
        var events = await Collect(client.Stream(Model("xai"), new("", [], []), new(), default));
        Assert.Equal("Hello", string.Concat(events.OfType<TextDelta>().Select(x => x.Delta)));
        var message = Assert.IsType<AssistantDone>(events.Last()).Message;
        var replay = ResponsesRequest.Build(Model("xai"), new("", [message], []), new());
        Assert.Equal("final_answer", replay["input"]![0]!["phase"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(401, "subscription_reconnect_required")]
    [InlineData(403, "subscription_reconnect_required")]
    [InlineData(429, "subscription_limit_reached")]
    [InlineData(402, "subscription_limit_reached")]
    public async Task LimitsAndAuthFailuresStopWithoutFallbackOrRetries(int status, string code)
    {
        var calls = 0;
        using var http = new HttpClient(new CeilingFakeHandler(_ =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent("oauth-secret") });
        }));
        var client = new SubscriptionResponsesClient(http, new Source(SubscriptionProvider.Xai));
        var events = await Collect(client.Stream(Model("xai") with { ApiKey = "paid-key" }, new("", [], []), new() { ApiKey = "override" }, default));
        var error = Assert.IsType<AssistantError>(events.Last());
        Assert.Equal(code, error.Error.ErrorCode);
        Assert.DoesNotContain("oauth-secret", error.Error.ErrorMessage);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task BrokerQuotaHeaderAndHttpStatusSurviveInStructuredAssistantError()
    {
        using var http = new HttpClient(new CeilingFakeHandler(_ =>
        {
            var response = new HttpResponseMessage((HttpStatusCode)429) { Content = new StringContent(string.Empty) };
            response.Headers.TryAddWithoutValidation("X-RateLimit-Reason", "quota");
            return Task.FromResult(response);
        }));
        var events = await Collect(new SubscriptionResponsesClient(http, new Source(SubscriptionProvider.Xai))
            .Stream(Model("xai"), new("", [], []), new(), default));
        var error = Assert.IsType<AssistantError>(events.Last());
        Assert.Equal(429, error.Error.ProviderHttpStatus);
        Assert.True(error.Error.ProviderRequestFailed);
        Assert.Equal("quota", error.Error.RateLimitReason);
        Assert.Empty(error.Error.Text);
    }

    [Fact]
    public async Task PrematureEofAndMalformedToolArgumentsNeverCompleteSuccessfully()
    {
        foreach (var payload in new[] { "data: [DONE]\n\n", Terminal(new JsonArray(JsonNode.Parse("""{"type":"function_call","id":"fc_1","call_id":"c1","name":"read","arguments":"{bad"}"""))) })
        {
            using var http = Http(payload);
            var events = await Collect(new SubscriptionResponsesClient(http, new Source(SubscriptionProvider.Codex)) { RetryDelays = [TimeSpan.FromMilliseconds(1)] }
                .Stream(Model(), new("", [], []), new(), default));
            Assert.IsType<AssistantError>(events.Last());
            Assert.Empty(events.OfType<AssistantDone>());
        }
    }

    [Fact]
    public async Task MissingConnectionNeverConsultsApiKeysOrCallsNetwork()
    {
        using var http = new HttpClient(new CeilingFakeHandler(_ => throw new Exception("Must not send")));
        var events = await Collect(new SubscriptionResponsesClient(http).Stream(Model() with { Connection = null, ApiKey = "paid" }, new("", [], []), new(), default));
        Assert.Equal("subscription_auth_error", Assert.IsType<AssistantError>(events.Last()).Error.ErrorCode);
    }

    [Theory]
    [InlineData("openai-codex", "session_id")]
    [InlineData("xai", "x-grok-conv-id")]
    public async Task TheCacheKeyRoutesEveryRequestToOneCache(string provider, string header)
    {
        var ok = Terminal(new JsonArray(JsonNode.Parse("""{"type":"message","id":"msg_1","role":"assistant","content":[{"type":"output_text","text":"Hi"}]}""")));
        HttpRequestMessage? seen = null;
        JsonNode? body = null;
        using var http = new HttpClient(new CeilingFakeHandler(async request =>
        {
            seen = request;
            body = JsonNode.Parse(await request.Content!.ReadAsStringAsync());
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ok, Encoding.UTF8, "text/event-stream") };
        }));
        var client = new SubscriptionResponsesClient(http, new Source(SubscriptionResponsesClient.Provider(provider)));
        await Collect(client.Stream(Model(provider), new("", [], []), new() { CacheKey = "seat-session-1" }, default));
        Assert.Equal("seat-session-1", seen!.Headers.GetValues(header).Single());
        Assert.Equal(provider == "openai-codex" ? "seat-session-1" : null, body!["prompt_cache_key"]?.GetValue<string>());

        // A key the backend would refuse (over 64 characters) is not sent at all.
        await Collect(client.Stream(Model(provider), new("", [], []), new() { CacheKey = new string('k', 65) }, default));
        Assert.False(seen!.Headers.Contains(header));
        Assert.Null(body!["prompt_cache_key"]);
    }

    [Fact]
    public async Task ServerErrorBeforeTheFirstTokenIsRetried()
    {
        var calls = 0;
        var ok = Terminal(new JsonArray(JsonNode.Parse("""{"type":"message","id":"msg_1","role":"assistant","content":[{"type":"output_text","text":"Hello"}]}""")));
        using var http = new HttpClient(new CeilingFakeHandler(_ => Task.FromResult(++calls == 1
            ? new HttpResponseMessage(HttpStatusCode.BadGateway)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ok, Encoding.UTF8, "text/event-stream") })));
        var client = new SubscriptionResponsesClient(http, new Source(SubscriptionProvider.Xai)) { RetryDelays = [TimeSpan.FromMilliseconds(1)] };
        var events = await Collect(client.Stream(Model("xai"), new("", [], []), new(), default));
        Assert.IsType<AssistantDone>(events.Last());
        Assert.Single(events.OfType<AssistantStart>());
        Assert.Empty(events.OfType<AssistantError>());
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task TheProvidersWordsReachTheSeatAndEveryRetryIsLogged()
    {
        // 2026-10-25: two Grok seats failed one request for over ten minutes; their logs said only "Subscription provider
        // returned an error." and nothing between the retries.
        var failed = "data: " + new JsonObject
        {
            ["type"] = "response.failed",
            ["response"] = new JsonObject { ["status"] = "failed", ["error"] = new JsonObject { ["code"] = "server_error", ["message"] = "Upstream\nmodel failed" } },
        }.ToJsonString() + "\n\n";
        using var http = new HttpClient(new CeilingFakeHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(failed, Encoding.UTF8, "text/event-stream") })));
        var log = new List<string>();
        var client = new SubscriptionResponsesClient(http, new Source(SubscriptionProvider.Xai))
            { RetryDelays = [TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(1)], Diagnostics = log.Add };
        var events = await Collect(client.Stream(Model("xai"), new("", [], []), new(), default));

        var error = Assert.IsType<AssistantError>(events.Last()).Error;
        Assert.Equal("server_error", error.ErrorCode);
        Assert.Equal("Subscription provider returned an error (server_error): Upstream model failed", error.ErrorMessage);
        Assert.Equal(2, log.Count);
        Assert.Contains("(server_error): Upstream model failed (attempt 1 of 3); retrying in 0s", log[0]);
        Assert.Contains("(attempt 2 of 3)", log[1]);
    }

    [Fact]
    public async Task ASeatThatCanRunTheTurnElsewhereGetsItBackAfterOneRetry_OthersRetryOn()
    {
        var calls = 0;
        var failed = "data: " + new JsonObject
        {
            ["type"] = "response.failed",
            ["response"] = new JsonObject { ["status"] = "failed", ["error"] = new JsonObject { ["code"] = "server_error", ["message"] = "The model is currently at capacity due to high demand." } },
        }.ToJsonString() + "\n\n";
        using var http = new HttpClient(new CeilingFakeHandler(_ => { calls++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(failed, Encoding.UTF8, "text/event-stream") }); }));
        var troubles = new List<string>();
        var log = new List<string>();
        var delays = Enumerable.Repeat(TimeSpan.FromMilliseconds(1), 8).ToArray();
        var hybrid = new SubscriptionResponsesClient(http, new Source(SubscriptionProvider.Xai))
            { RetryDelays = delays, HandBackOnProviderTrouble = () => true, OnProviderTrouble = troubles.Add, Diagnostics = log.Add };
        var events = await Collect(hybrid.Stream(Model("xai"), new("", [], []), new(), default));
        Assert.IsType<AssistantError>(events.Last());
        Assert.Equal(2, calls);
        Assert.Equal(["server_error"], troubles);
        Assert.Contains("handing the turn back to run elsewhere", log[^1]);

        calls = 0;
        var onlySubscription = new SubscriptionResponsesClient(http, new Source(SubscriptionProvider.Xai))
            { RetryDelays = delays, HandBackOnProviderTrouble = () => false, OnProviderTrouble = troubles.Add, Diagnostics = _ => { } };
        await Collect(onlySubscription.Stream(Model("xai"), new("", [], []), new(), default));
        Assert.Equal(9, calls);
        Assert.Single(troubles);
    }

    // pi13, 2026-10-27: a spent allowance (HTTP 429 from the broker) never comes back by retrying; a seat that can run the
    // turn on its own model hands it back at once, and one that cannot gets the error as before.
    [Fact]
    public async Task ASpentAllowanceIsHandedBackAtOnceBySeatsThatCanRunElsewhere()
    {
        var calls = 0;
        using var http = new HttpClient(new CeilingFakeHandler(_ => { calls++; return Task.FromResult(new HttpResponseMessage((HttpStatusCode)429)
            { Content = new StringContent("{\"error\":{\"message\":\"weekly allowance spent\"}}", Encoding.UTF8, "application/json") }); }));
        var troubles = new List<string>();
        var delays = Enumerable.Repeat(TimeSpan.FromMilliseconds(1), 8).ToArray();
        var hybrid = new SubscriptionResponsesClient(http, new Source(SubscriptionProvider.Xai))
            { RetryDelays = delays, HandBackOnProviderTrouble = () => true, OnProviderTrouble = troubles.Add, Diagnostics = _ => { } };
        var events = await Collect(hybrid.Stream(Model("xai"), new("", [], []), new(), default));
        Assert.Equal(SubscriptionResponsesClient.QuotaErrorCode, Assert.IsType<AssistantError>(events.Last()).Error.ErrorCode);
        Assert.Equal(1, calls);
        Assert.Equal([SubscriptionResponsesClient.QuotaErrorCode], troubles);

        calls = 0;
        var onlySubscription = new SubscriptionResponsesClient(http, new Source(SubscriptionProvider.Xai))
            { RetryDelays = delays, HandBackOnProviderTrouble = () => false, OnProviderTrouble = troubles.Add, Diagnostics = _ => { } };
        Assert.IsType<AssistantError>((await Collect(onlySubscription.Stream(Model("xai"), new("", [], []), new(), default))).Last());
        Assert.Equal(1, calls);
        Assert.Single(troubles);
    }

    [Theory]
    [InlineData("server_is_overloaded", true)]
    [InlineData("server_error", true)]
    [InlineData("response_failed", true)]
    [InlineData("rate_limit_exceeded", false)]
    [InlineData("context_length_exceeded", false)]
    public async Task AProviderSideFailureInTheStreamIsRetriedButNotAQuotaOrRequestError(string code, bool retried)
    {
        var calls = 0;
        var failed = "data: " + new JsonObject
        {
            ["type"] = "response.failed",
            ["response"] = new JsonObject { ["status"] = "failed", ["error"] = new JsonObject { ["code"] = code, ["message"] = "busy" } },
        }.ToJsonString() + "\n\n";
        var ok = Terminal(new JsonArray(JsonNode.Parse("""{"type":"message","id":"msg_1","role":"assistant","content":[{"type":"output_text","text":"Hello"}]}""")));
        using var http = new HttpClient(new CeilingFakeHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(++calls == 1 ? failed : ok, Encoding.UTF8, "text/event-stream") })));
        var client = new SubscriptionResponsesClient(http, new Source(SubscriptionProvider.Codex)) { RetryDelays = [TimeSpan.FromMilliseconds(1)] };
        var events = await Collect(client.Stream(Model("openai-codex"), new("", [], []), new(), default));
        if (retried)
        {
            Assert.IsType<AssistantDone>(events.Last());
            Assert.Equal(2, calls);
        }
        else
        {
            var error = Assert.IsType<AssistantError>(events.Last()).Error;
            Assert.Equal(code, error.ErrorCode);
            Assert.False(error.ProviderRequestFailed); // not a provider outage: it does not count toward "model unavailable"
            Assert.Equal(1, calls);
        }
    }

    [Fact]
    public async Task RetriesStopAtTheirLimitAndNeverFollowContent()
    {
        var calls = 0;
        using var failing = new HttpClient(new CeilingFakeHandler(_ => { calls++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)); }));
        var client = new SubscriptionResponsesClient(failing, new Source(SubscriptionProvider.Xai)) { RetryDelays = [TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(1)] };
        var events = await Collect(client.Stream(Model("xai"), new("", [], []), new(), default));
        Assert.Equal("subscription_server_error", Assert.IsType<AssistantError>(events.Last()).Error.ErrorCode);
        Assert.Equal(3, calls);

        calls = 0;
        using var partial = new HttpClient(new CeilingFakeHandler(_ =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(Event("response.output_item.added", 0, JsonNode.Parse("""{"type":"message","id":"msg_1","role":"assistant","content":[]}""")!)
                + Delta("response.output_text.delta", 0, "Hel"), Encoding.UTF8, "text/event-stream") });
        }));
        var streamed = await Collect(new SubscriptionResponsesClient(partial, new Source(SubscriptionProvider.Xai)) { RetryDelays = [TimeSpan.FromMilliseconds(1)] }
            .Stream(Model("xai"), new("", [], []), new(), default));
        Assert.NotEmpty(streamed.OfType<TextDelta>());
        Assert.Equal("subscription_stream_incomplete", Assert.IsType<AssistantError>(streamed.Last()).Error.ErrorCode);
        Assert.Equal(1, calls);
    }
    private static readonly string OkStream = Terminal(new JsonArray(JsonNode.Parse("""{"type":"message","id":"msg_1","role":"assistant","content":[{"type":"output_text","text":"Hello"}]}""")));

    private static HttpRequestException Refused() => new("Connection refused (127.0.0.1:18787)",
        new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.ConnectionRefused), null);

    [Fact]
    public async Task ABrokerThatCannotBeReachedIsReconnectedBeyondTheRetries()
    {
        var calls = 0;
        var log = new List<string>();
        using var http = new HttpClient(new CeilingFakeHandler(_ => ++calls <= 4 ? throw Refused()
            : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(OkStream, Encoding.UTF8, "text/event-stream") })));
        var client = new SubscriptionResponsesClient(http, new Source(SubscriptionProvider.Codex))
        { RetryDelays = [], FirstReconnectDelay = TimeSpan.FromMilliseconds(1), MaxReconnectDelay = TimeSpan.FromMilliseconds(2), Diagnostics = log.Add };
        var events = await Collect(client.Stream(Model("openai-codex"), new("", [], []), new(), default));
        Assert.IsType<AssistantDone>(events.Last());
        Assert.Single(events.OfType<AssistantStart>());
        Assert.Equal(5, calls);
        Assert.Equal(4, log.Count(l => l.Contains("reconnecting to the subscription broker", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task AHybridSeatHandsAnUnreachableBrokerBackInsteadOfWaiting()
    {
        var calls = 0;
        var troubles = new List<string>();
        using var http = new HttpClient(new CeilingFakeHandler(_ => { calls++; throw Refused(); }));
        var client = new SubscriptionResponsesClient(http, new Source(SubscriptionProvider.Codex))
        { RetryDelays = [TimeSpan.FromMilliseconds(1)], HandBackOnProviderTrouble = () => true, OnProviderTrouble = troubles.Add, Diagnostics = _ => { } };
        var events = await Collect(client.Stream(Model("openai-codex"), new("", [], []), new(), default));
        Assert.Equal(SubscriptionResponsesClient.ConnectionErrorCode, Assert.IsType<AssistantError>(events.Last()).Error.ErrorCode);
        Assert.Equal(2, calls);
        Assert.Equal([SubscriptionResponsesClient.ConnectionErrorCode], troubles);
    }

    [Fact]
    public async Task AStreamThatBreaksBeforeItsFirstTokenIsRetriedApartFromTheRequestRetries()
    {
        var calls = 0;
        var log = new List<string>();
        using var http = new HttpClient(new CeilingFakeHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(++calls <= 2 ? "" : OkStream, Encoding.UTF8, "text/event-stream") })));
        var client = new SubscriptionResponsesClient(http, new Source(SubscriptionProvider.Codex))
        { RetryDelays = [], FirstStreamRetryDelay = TimeSpan.FromMilliseconds(1), Diagnostics = log.Add };
        var events = await Collect(client.Stream(Model("openai-codex"), new("", [], []), new(), default));
        Assert.IsType<AssistantDone>(events.Last());
        Assert.Equal(3, calls);
        Assert.Equal(2, log.Count(l => l.Contains("stream retry", StringComparison.Ordinal)));

        calls = 0;
        using var broken = new HttpClient(new CeilingFakeHandler(_ => { calls++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("", Encoding.UTF8, "text/event-stream") }); }));
        var limited = new SubscriptionResponsesClient(broken, new Source(SubscriptionProvider.Codex))
        { RetryDelays = [TimeSpan.FromMilliseconds(1)], StreamRetries = 2, FirstStreamRetryDelay = TimeSpan.FromMilliseconds(1), Diagnostics = _ => { } };
        var failed = await Collect(limited.Stream(Model("openai-codex"), new("", [], []), new(), default));
        Assert.Equal("subscription_stream_incomplete", Assert.IsType<AssistantError>(failed.Last()).Error.ErrorCode);
        Assert.Equal(4, calls); // the first try, two stream retries, then the one request retry
    }

    [Fact]
    public async Task RetryAfterComesBeforeTheSchedule()
    {
        var calls = 0;
        var log = new List<string>();
        using var http = new HttpClient(new CeilingFakeHandler(_ =>
        {
            if (++calls > 1) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(OkStream, Encoding.UTF8, "text/event-stream") });
            var busy = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            busy.Headers.TryAddWithoutValidation("retry-after-ms", "5");
            return Task.FromResult(busy);
        }));
        // The schedule says ten minutes; the server says five milliseconds.
        var client = new SubscriptionResponsesClient(http, new Source(SubscriptionProvider.Codex)) { RetryDelays = [TimeSpan.FromMinutes(10)], Diagnostics = log.Add };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var events = await Collect(client.Stream(Model("openai-codex"), new("", [], []), new(), timeout.Token));
        Assert.IsType<AssistantDone>(events.Last());
        Assert.Equal(2, calls);
        Assert.Contains(log, l => l.Contains("(Retry-After)", StringComparison.Ordinal));
    }

    [Fact]
    public void RetryWaitsAreSpreadByTenPercentAndConnectionFailuresAreKnown()
    {
        var waits = Enumerable.Range(0, 200).Select(_ => SubscriptionResponsesClient.Jitter(TimeSpan.FromSeconds(10))).ToList();
        Assert.All(waits, w => Assert.InRange(w.TotalMilliseconds, 9_000, 11_000));
        Assert.True(waits.Distinct().Count() > 1);
        Assert.Equal(TimeSpan.Zero, SubscriptionResponsesClient.Jitter(TimeSpan.Zero));

        Assert.True(SubscriptionResponsesClient.IsConnectionFailure(Refused()));
        Assert.True(SubscriptionResponsesClient.IsConnectionFailure(new HttpRequestException(HttpRequestError.ConnectionError, "down")));
        Assert.False(SubscriptionResponsesClient.IsConnectionFailure(new HttpRequestException("Response status code does not indicate success: 502")));
    }

    private static HttpClient Http(string body) => new(new CeilingFakeHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent(body, Encoding.UTF8, "text/event-stream") })));

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task CancellationAndDeadlinesStopHeadersAndIdleStreams(bool headers, bool cancel)
    {
        using var handler = new WaitingHandler(headers);
        using var http = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        var client = new SubscriptionResponsesClient(http, new Source(SubscriptionProvider.Xai))
        { HeadersTimeout = TimeSpan.FromMilliseconds(cancel ? 5000 : 50), IdleTimeout = TimeSpan.FromMilliseconds(cancel ? 5000 : 50), RetryDelays = [], StreamRetries = 0 };
        if (cancel) cancellation.CancelAfter(50);
        var events = await Collect(client.Stream(Model("xai"), new("", [], []), new(), cancellation.Token));
        var error = Assert.IsType<AssistantError>(events.Last());
        Assert.Equal(cancel ? StopReason.Aborted : StopReason.Error, error.Reason);
        Assert.Equal(cancel ? "cancelled" : "subscription_timeout", error.Error.ErrorCode);
        Assert.Equal(1, handler.Calls);
    }

    private sealed class WaitingHandler(bool headers) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            if (headers) await Task.Delay(Timeout.Infinite, ct);
            return new(HttpStatusCode.OK) { Content = new StreamContent(new WaitingStream()) };
        }
    }
    private sealed class WaitingStream : MemoryStream
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        { await Task.Delay(Timeout.Infinite, cancellationToken); return 0; }
    }
    private static string Event(string type, int index, JsonNode item) => "data: " + new JsonObject { ["type"] = type, ["output_index"] = index, ["item"] = item }.ToJsonString() + "\n\n";
    private static string Delta(string type, int index, string delta) => "data: " + new JsonObject { ["type"] = type, ["output_index"] = index, ["delta"] = delta }.ToJsonString() + "\n\n";
    private static string Terminal(JsonArray output) => "data: " + new JsonObject
    {
        ["type"] = "response.completed", ["response"] = new JsonObject
        {
            ["status"] = "completed", ["output"] = output,
            ["usage"] = JsonNode.Parse("""{"input_tokens":100,"output_tokens":10,"total_tokens":110,"input_tokens_details":{"cached_tokens":20}}"""),
        },
    }.ToJsonString() + "\n\n";

    internal static async Task<List<AssistantMessageEvent>> Collect(IAsyncEnumerable<AssistantMessageEvent> stream)
    { var events = new List<AssistantMessageEvent>(); await foreach (var evt in stream) events.Add(evt); return events; }
    private sealed class Source(SubscriptionProvider provider) : ISubscriptionCredentialSource
    {
        public Task<SubscriptionCredential> GetAsync(string connectionId, SubscriptionProvider requested, CancellationToken ct)
        {
            Assert.Equal(provider, requested);
            return Task.FromResult(new SubscriptionCredential(provider, "oauth-secret", "refresh", DateTimeOffset.UtcNow.AddHours(1), "account"));
        }
    }
}
