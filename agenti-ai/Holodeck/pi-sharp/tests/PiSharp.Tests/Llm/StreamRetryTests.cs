// pi-sharp-hh-7: the provider answered, streamed some content, and then died (connection reset,
// the body bound, a 5xx mid-stream). Today that becomes an error turn that ends the run and
// costs a run-level resume. Instead: drop the partial, tell the mirror and tracer the content
// was discarded, and re-issue the same request with backoff. These tests fake the shape with a
// raw socket that serves a few SSE deltas and then tears the connection down.
using System.Net;
using System.Net.Sockets;
using System.Text;
using PiSharp.Core.Agent;
using PiSharp.Core.Messages;
using PiSharp.Core.Models;
using PiSharp.Llm;
using PiSharp.Llm.Http;
using PiSharp.Llm.OpenAi;

namespace PiSharp.Tests.Llm;

public sealed class StreamRetryTests : IAsyncDisposable
{
    // KeepAlive: true so a retry that abandons a dead pooled connection lands on the same
    // listener again instead of racing a fresh accept.
    private const string ChunkHeaders =
        "HTTP/1.1 200 OK\r\nContent-Type: text/event-stream\r\nKeep-Alive: timeout=600\r\nTransfer-Encoding: chunked\r\n\r\n";

    private readonly TcpListener _listener;
    private Func<TcpClient, Task>? _handler;

    private static ModelConfig Model(string baseUrl) => new() { Id = "fake", BaseUrl = baseUrl, ApiKey = "dummy" };

    private static LlmContext Context() =>
        new("test system prompt", [new UserMessage("hello")], []);

    public StreamRetryTests()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        _ = Task.Run(AcceptLoopAsync);
    }

    public async ValueTask DisposeAsync()
    {
        _listener.Stop();
        await Task.Delay(50);
    }

    private async Task AcceptLoopAsync()
    {
        while (true)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(); }
            catch (ObjectDisposedException) { return; }
            var handler = _handler;
            if (handler is null) { client.Dispose(); continue; }
            _ = Task.Run(() => RunHandlerAsync(client, handler));
        }
    }

    private static async Task RunHandlerAsync(TcpClient client, Func<TcpClient, Task> handler)
    {
        try { await handler(client); }
        catch (Exception) { /* the socket dies; the client sees the reset */ }
        finally { client.Dispose(); }
    }

    private string BaseUrl => $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/v1";

    /// <summary>
    /// Reads the request head, writes <paramref name="deltas"/> as separate chunked SSE events
    /// (each flushed, so the client sees and pulls them), then hard-closes the socket: the
    /// connection reset after partial content.
    /// </summary>
    private static async Task PartialThenResetAsync(Stream stream, IReadOnlyList<string> deltas)
    {
        var buf = new byte[8192];
        _ = await stream.ReadAsync(buf.AsMemory(0, buf.Length));
        await stream.WriteAsync(Encoding.ASCII.GetBytes(ChunkHeaders));
        foreach (var delta in deltas)
        {
            var chunk = "data: {\"choices\":[{\"index\":0,\"delta\":{\"role\":\"assistant\",\"content\":\"" + delta + "\"}}]}\n\n";
            var bytes = Encoding.UTF8.GetBytes(chunk);
            await stream.WriteAsync(Encoding.ASCII.GetBytes(Convert.ToString(bytes.Length, 16) + "\r\n"));
            await stream.WriteAsync(bytes);
            await stream.WriteAsync(Encoding.ASCII.GetBytes("\r\n"));
            await stream.FlushAsync();
            await Task.Delay(50); // let the client pull and render each delta before the reset
        }
        // A hard close sends RST/FIN: the client's next read throws a transport error.
    }

    /// <summary>Serves a complete one-shot stream: a few deltas, finish_reason, [DONE], close.</summary>
    private static async Task CompleteStreamAsync(Stream stream, string text)
    {
        var buf = new byte[8192];
        _ = await stream.ReadAsync(buf.AsMemory(0, buf.Length));
        await stream.WriteAsync(Encoding.ASCII.GetBytes(ChunkHeaders));
        var parts = "data: {\"choices\":[{\"index\":0,\"delta\":{\"role\":\"assistant\",\"content\":\"" + text + "\"}}]}\n\n"
                  + "data: {\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n";
        var bytes = Encoding.UTF8.GetBytes(parts);
        await stream.WriteAsync(Encoding.ASCII.GetBytes(Convert.ToString(bytes.Length, 16) + "\r\n"));
        await stream.WriteAsync(bytes);
        await stream.WriteAsync(Encoding.ASCII.GetBytes("\r\n0\r\n\r\n"));
        await stream.FlushAsync();
        await Task.Delay(200); // hold until the client is done reading
    }

    private static List<AssistantMessageEvent> Collect(IReadOnlyList<object> raw) =>
        raw.Cast<AssistantMessageEvent>().ToList();

    // -----------------------------------------------------------------------
    // Acceptance 1: the fake server drops after 3 deltas; the client reconnects and the turn
    // completes once. No error turn, no resume, no duplicated text in the session.
    // -----------------------------------------------------------------------
    [Fact]
    public async Task ResetAfterPartialContent_ReconnectsAndCompletesOneTurn()
    {
        var attempt = 0;
        _handler = async client =>
        {
            var n = Interlocked.Increment(ref attempt);
            var stream = client.GetStream();
            if (n <= 3)
            {
                // Three resets after partial content.
                await PartialThenResetAsync(stream, ["Hel", "lo w", "orld"]);
                return;
            }
            await CompleteStreamAsync(stream, "Hello world again");
        };

        // No headers-leg retries, no body bound; the stream retry budget does the work and the
        // policy backoff (2/4/8s on Patient) is what the test measures.
        var client = new OpenAiCompletionsClient { Retry = RetryPolicy.None };

        var events = new List<AssistantMessageEvent>();
        await foreach (var e in client.Stream(Model(BaseUrl), Context(), new StreamOptions())) events.Add(e);

        Assert.Equal(4, attempt); // three dead streams + the completed one

        // The turn ends with a single AssistantDone and no AssistantError: no error turn, no
        // resume, and the agent loop sees exactly one completed assistant message.
        Assert.IsType<AssistantDone>(events[^1]);
        Assert.DoesNotContain(events, e => e is AssistantError);

        // Three discards, one per dropped partial, in order, before each retry's content.
        var discards = events.OfType<AssistantDiscardPartial>().ToList();
        Assert.Equal(3, discards.Count);
        var firstDiscard = events.IndexOf(discards[0]);
        Assert.True(firstDiscard > 0 && firstDiscard < events.Count - 1,
            "the discard must sit between streamed content and the retry's content");

        // No duplicated text: the text the caller saw across the whole stream is the partial
        // drops (discarded) plus the final answer, and the final message's text is exactly the
        // answer — the session JSONL would hold "Hello world again" once, not the drops too.
        var done = Assert.IsType<AssistantDone>(events[^1]);
        Assert.Equal("Hello world again", done.Message.Text);
        var allDeltas = events.OfType<TextDelta>().Select(d => d.Delta).Concatenate();
        Assert.Equal("Hello worldHello worldHello worldHello world again", allDeltas);
    }

    // -----------------------------------------------------------------------
    // Acceptance 4: after 3 failures the behaviour is today's: the error turn surfaces.
    // -----------------------------------------------------------------------
    [Fact]
    public async Task AfterThreeReconnectFailures_TheErrorTurnSurfaces()
    {
        var attempt = 0;
        _handler = async client =>
        {
            Interlocked.Increment(ref attempt);
            var stream = client.GetStream();
            await PartialThenResetAsync(stream, ["Hel"]);
        };

        var client = new OpenAiCompletionsClient { Retry = RetryPolicy.None };
        var events = new List<AssistantMessageEvent>();
        await foreach (var e in client.Stream(Model(BaseUrl), Context(), new StreamOptions())) events.Add(e);

        Assert.Equal(4, attempt); // one send + 3 reconnects; no 5th

        var error = Assert.IsType<AssistantError>(events[^1]);
        Assert.Equal(StopReason.Error, error.Reason);
        Assert.DoesNotContain(events, e => e is AssistantDone);
        Assert.Equal(3, events.OfType<AssistantDiscardPartial>().Count());
        // The stream text holds the first partial plus the last retry's partial: each discard
        // resets the state, so the error turn records the final partial only.
        Assert.Equal("Hel", error.Error.Text);
    }

    // -----------------------------------------------------------------------
    // No retry on a clean stop, however it ends: a stream that terminates its own body after
    // content (the assembler's clean Finish, or a bare end-of-stream that the client reads as an
    // empty read, not a transport error) is not a death. Only a transport-class failure (reset,
    // bound, 5xx mid-stream) reconnects. finish_reason=length is the item's named case: it
    // arrives as a clean completion, so no discard and no second send.
    // -----------------------------------------------------------------------
    [Fact]
    public async Task LengthStop_DoesNotReconnect()
    {
        var attempt = 0;
        _handler = async client =>
        {
            var n = Interlocked.Increment(ref attempt);
            var stream = client.GetStream();
            var buf = new byte[8192];
            _ = await stream.ReadAsync(buf.AsMemory(0, buf.Length));
            if (n == 1)
            {
                // Content, then a clean finish_reason=length and a normal end of body.
                await stream.WriteAsync(Encoding.ASCII.GetBytes(ChunkHeaders));
                var body = "data: {\"choices\":[{\"index\":0,\"delta\":{\"role\":\"assistant\",\"content\":\"too long\"}}]}\n\n"
                         + "data: {\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"length\"}]}\n\ndata: [DONE]\n\n";
                var bytes = Encoding.UTF8.GetBytes(body);
                await stream.WriteAsync(Encoding.ASCII.GetBytes(Convert.ToString(bytes.Length, 16) + "\r\n"));
                await stream.WriteAsync(bytes);
                await stream.WriteAsync(Encoding.ASCII.GetBytes("\r\n0\r\n\r\n"));
                await stream.FlushAsync();
                await Task.Delay(200);
                return;
            }
            await CompleteStreamAsync(stream, "second");
        };

        var client = new OpenAiCompletionsClient { Retry = RetryPolicy.None };
        var events = new List<AssistantMessageEvent>();
        await foreach (var e in client.Stream(Model(BaseUrl), Context(), new StreamOptions())) events.Add(e);

        Assert.Equal(1, attempt);
        var done = Assert.IsType<AssistantDone>(events[^1]);
        Assert.Equal(StopReason.Length, done.Reason);
        Assert.Equal("too long", done.Message.Text);
        Assert.DoesNotContain(events, e => e is AssistantDiscardPartial or AssistantError);
    }

    [Fact]
    public async Task CleanStreamEndAfterContent_DoesNotReconnect()
    {
        var attempt = 0;
        _handler = async client =>
        {
            var n = Interlocked.Increment(ref attempt);
            var stream = client.GetStream();
            var buf = new byte[8192];
            _ = await stream.ReadAsync(buf.AsMemory(0, buf.Length));
            if (n == 1)
            {
                // One delta, then a clean [DONE] and a normal end of body: the stream ends its
                // own stream. The assembler's Finish raises the clean-protocol error (no
                // finish_reason), which is not transport-class: no reconnect.
                await stream.WriteAsync(Encoding.ASCII.GetBytes(ChunkHeaders));
                var body = "data: {\"choices\":[{\"index\":0,\"delta\":{\"role\":\"assistant\",\"content\":\"hi\"}}]}\n\n"
                         + "data: [DONE]\n\n";
                var bytes = Encoding.UTF8.GetBytes(body);
                await stream.WriteAsync(Encoding.ASCII.GetBytes(Convert.ToString(bytes.Length, 16) + "\r\n"));
                await stream.WriteAsync(bytes);
                await stream.WriteAsync(Encoding.ASCII.GetBytes("\r\n0\r\n\r\n"));
                await stream.FlushAsync();
                await Task.Delay(200);
                return;
            }
            await CompleteStreamAsync(stream, "second");
        };

        var client = new OpenAiCompletionsClient { Retry = RetryPolicy.None };
        var events = new List<AssistantMessageEvent>();
        await foreach (var e in client.Stream(Model(BaseUrl), Context(), new StreamOptions())) events.Add(e);

        Assert.Equal(1, attempt);
        var error = Assert.IsType<AssistantError>(events[^1]);
        Assert.Contains("finish_reason", error.Error.ErrorMessage, StringComparison.Ordinal);
        Assert.DoesNotContain(events, e => e is AssistantDiscardPartial);
    }

    // -----------------------------------------------------------------------
    // The retry gate is the transport-class test: a protocol error (the stream ends its own body
    // after content, with no finish_reason) is not reconnected even though content had reached
    // the caller. The shape: one delta, a bare [DONE], then a clean end of body — the socket
    // stays alive until the client is done, so the failure is the assembler's clean error,
    // not a transport death.
    // -----------------------------------------------------------------------
    [Fact]
    public async Task ProtocolErrorAfterContent_DoesNotReconnect()
    {
        var attempt = 0;
        _handler = async client =>
        {
            var n = Interlocked.Increment(ref attempt);
            var stream = client.GetStream();
            var buf = new byte[8192];
            _ = await stream.ReadAsync(buf.AsMemory(0, buf.Length));
            if (n == 1)
            {
                await stream.WriteAsync(Encoding.ASCII.GetBytes(ChunkHeaders));
                var body = "data: {\"choices\":[{\"index\":0,\"delta\":{\"role\":\"assistant\",\"content\":\"ok\"}}]}\n\n"
                         + "data: [DONE]\n\n";
                var bytes = Encoding.UTF8.GetBytes(body);
                await stream.WriteAsync(Encoding.ASCII.GetBytes(Convert.ToString(bytes.Length, 16) + "\r\n"));
                await stream.WriteAsync(bytes);
                await stream.WriteAsync(Encoding.ASCII.GetBytes("\r\n0\r\n\r\n"));
                await stream.FlushAsync();
                await Task.Delay(500);
                return;
            }
            await CompleteStreamAsync(stream, "second");
        };

        var client = new OpenAiCompletionsClient { Retry = RetryPolicy.None };
        var events = new List<AssistantMessageEvent>();
        await foreach (var e in client.Stream(Model(BaseUrl), Context(), new StreamOptions())) events.Add(e);

        // No reconnect: the failure is not transport-class, so the budget is untouched and the
        // error turn surfaces after exactly one send.
        Assert.Equal(1, attempt);
        Assert.IsType<AssistantError>(events[^1]);
        Assert.DoesNotContain(events, e => e is AssistantDiscardPartial);
    }

    // -----------------------------------------------------------------------
    // BodyIdleTimeout: a stream that delivered one content delta and then only dribbles noise
    // (keep-alive comments) for the idle bound is the same dead stream the bound exists to kill.
    // It must die with the bound's retryable diagnosis (not the total-time bound, which the noise
    // re-arms) and the in-turn retry must reconnect: discard the partial, re-issue, complete.
    // -----------------------------------------------------------------------
    [Fact]
    public async Task NoiseAfterContent_DiesAtTheIdleBoundAndReconnects()
    {
        var attempt = 0;
        _handler = async client =>
        {
            var n = Interlocked.Increment(ref attempt);
            var stream = client.GetStream();
            if (n == 1)
            {
                // One content delta, then keep-alive comments only. The total-time bound is
                // re-armed by each comment; only the idle bound (no content) can fire. The
                // handler then holds the socket: the client's idle bound fires 1s after the
                // last content and the in-turn retry reconnects.
                var buf = new byte[8192];
                _ = await stream.ReadAsync(buf.AsMemory(0, buf.Length));
                await stream.WriteAsync(Encoding.ASCII.GetBytes(ChunkHeaders));
                var delta = "data: {\"choices\":[{\"index\":0,\"delta\":{\"role\":\"assistant\",\"content\":\"ok\"}}]}\n\n";
                var bytes = Encoding.UTF8.GetBytes(delta);
                await stream.WriteAsync(Encoding.ASCII.GetBytes(Convert.ToString(bytes.Length, 16) + "\r\n"));
                await stream.WriteAsync(bytes);
                await stream.WriteAsync(Encoding.ASCII.GetBytes("\r\n"));
                await stream.FlushAsync();
                var comment = ": keep-alive\n\n";
                var cbytes = Encoding.UTF8.GetBytes(comment);
                for (var i = 0; i < 5; i++)
                {
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(Convert.ToString(cbytes.Length, 16) + "\r\n"));
                    await stream.WriteAsync(cbytes);
                    await stream.WriteAsync(Encoding.ASCII.GetBytes("\r\n"));
                    await stream.FlushAsync();
                    await Task.Delay(300);
                }
                // Hold the socket long enough to outlive the 1s idle bound after the last
                // content (the comments re-arm the total bound but not the idle bound).
                await Task.Delay(3_000);
                return;
            }
            // CompleteStreamAsync reads the request itself.
            await CompleteStreamAsync(stream, "recovered");
        };

        var client = new OpenAiCompletionsClient
        {
            Retry = RetryPolicy.None,
            StreamRetries = 3,
            BodyIdleTimeout = TimeSpan.FromSeconds(1),
            BodyTimeout = TimeSpan.FromSeconds(30), // far past; it must not be the one that fires
        };

        var started = Environment.TickCount64;
        var events = new List<AssistantMessageEvent>();
        await foreach (var e in client.Stream(Model(BaseUrl), Context(), new StreamOptions())) events.Add(e);
        var elapsedMs = Environment.TickCount64 - started;

        Assert.Equal(2, attempt);
        var done = Assert.IsType<AssistantDone>(events[^1]);
        Assert.Equal("recovered", done.Message.Text);
        // The in-turn retry discarded the partial "ok" before reconnecting.
        Assert.Single(events.OfType<AssistantDiscardPartial>());
        Assert.DoesNotContain(events, e => e is AssistantError);
        // The idle bound fired ~1s after the last content delta, not at the 30s total bound.
        Assert.True(elapsedMs < 10_000, $"idle bound took {elapsedMs} ms — it must beat the total bound");
    }

    // -----------------------------------------------------------------------
    // SubscriptionRouter.IdleBoundFor: remote BaseUrls get the 3-minute bound; loopback and LAN
    // local servers get none (a local server that dribbles noise is a local bug, not a dead
    // remote stream).
    // -----------------------------------------------------------------------
    [Fact]
    public void IdleBoundFor_RemoteGetsThreeMinutes_LocalGetsNone()
    {
        Assert.Equal(TimeSpan.FromMinutes(3), SubscriptionRouter.IdleBoundFor("https://api.openai.com/v1"));
        Assert.Equal(TimeSpan.FromMinutes(3), SubscriptionRouter.IdleBoundFor("http://8.8.8.8:8080/v1"));
        Assert.Equal(TimeSpan.Zero, SubscriptionRouter.IdleBoundFor("http://localhost:8080/v1"));
        Assert.Equal(TimeSpan.Zero, SubscriptionRouter.IdleBoundFor("http://127.0.0.1:8080/v1"));
        Assert.Equal(TimeSpan.Zero, SubscriptionRouter.IdleBoundFor("http://[::1]:8080/v1"));
        Assert.Equal(TimeSpan.Zero, SubscriptionRouter.IdleBoundFor("http://10.0.0.5:8080/v1"));
        Assert.Equal(TimeSpan.Zero, SubscriptionRouter.IdleBoundFor("http://172.16.0.1:8080/v1"));
        Assert.Equal(TimeSpan.Zero, SubscriptionRouter.IdleBoundFor("http://192.168.1.1:8080/v1"));
    }

    // -----------------------------------------------------------------------
    // The discard event carries the partial with cleared content: the mirror's
    // UpdateAssistantMessage records the drop, not the bytes.
    // -----------------------------------------------------------------------
    [Fact]
    public async Task DiscardEvent_CarriesClearedPartial()
    {
        var attempt = 0;
        _handler = async client =>
        {
            var n = Interlocked.Increment(ref attempt);
            var stream = client.GetStream();
            if (n == 1)
            {
                await PartialThenResetAsync(stream, ["Hel"]);
                return;
            }
            await CompleteStreamAsync(stream, "done");
        };

        var client = new OpenAiCompletionsClient { Retry = RetryPolicy.None };
        var events = new List<AssistantMessageEvent>();
        await foreach (var e in client.Stream(Model(BaseUrl), Context(), new StreamOptions())) events.Add(e);

        var discard = events.OfType<AssistantDiscardPartial>().First();
        Assert.Empty(discard.Partial.Content);
        Assert.Equal(StopReason.Pending, discard.Partial.StopReason);
        Assert.Null(discard.Partial.ErrorMessage);
        Assert.IsType<AssistantDone>(events[^1]);
    }
}

internal static class ConcatExtensions
{
    public static string Concatenate(this IEnumerable<string> parts) => string.Concat(parts);
}
