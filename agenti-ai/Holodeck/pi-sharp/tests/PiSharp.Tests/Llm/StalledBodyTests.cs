// Regression for the 2026-09-15 21:00Z freeze (wi-30): the provider accepted the connection,
// sent response headers, and then never produced a body byte. Nothing bounded the body read, so
// the seat hung on an ESTABLISHED socket forever. These tests fake that shape with a raw socket
// that writes the headers of a chunked SSE response and then stays silent.
using System.Net;
using System.Net.Sockets;
using System.Text;
using PiSharp.Core.Agent;
using PiSharp.Core.Messages;
using PiSharp.Core.Models;
using PiSharp.Llm.Http;
using PiSharp.Llm.OpenAi;

namespace PiSharp.Tests.Llm;

public sealed class StalledBodyTests : IAsyncDisposable
{
    // KeepAlive: true, so a client that reuses its pooled connection and abandons a stalled
    // response lands on this same listener again instead of racing a fresh connection.
    private const string ChunkHeaders =
        "HTTP/1.1 200 OK\r\nContent-Type: text/event-stream\r\nKeep-Alive: timeout=600\r\nTransfer-Encoding: chunked\r\n\r\n";

    private readonly TcpListener _listener;
    private Func<TcpClient, Task>? _handler;

    private static ModelConfig Model(string baseUrl) => new() { Id = "fake", BaseUrl = baseUrl, ApiKey = "dummy" };

    private static LlmContext Context() =>
        new("test system prompt", [new UserMessage("hello")], []);

    public StalledBodyTests()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        _ = Task.Run(AcceptLoopAsync);
    }

    public async ValueTask DisposeAsync()
    {
        _listener.Stop();
        // Give accept/handler tasks a beat to unwind before sockets vanish underneath them.
        await Task.Delay(50);
    }

    private async Task AcceptLoopAsync()
    {
        while (true)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(); }
            catch (ObjectDisposedException) { return; } // Dispose stopped us.
            var handler = _handler;
            if (handler is null) { client.Dispose(); continue; }
            _ = Task.Run(() => RunHandlerAsync(client, handler));
        }
    }

    private static async Task RunHandlerAsync(TcpClient client, Func<TcpClient, Task> handler)
    {
        try { await handler(client); }
        catch (Exception) { /* the socket dies; the client sees the stall or the reset */ }
        finally { client.Dispose(); }
    }

    private string BaseUrl => $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/v1";

    /// <summary>
    /// Writes <paramref name="bytes"/> after reading some request bytes, then stays silent for
    /// <paramref name="holdMs"/> before the socket dies. The hold is the stall; the close matters
    /// too: a socket that hangs forever pins the client's connection pool, and then a resend can
    /// never land anywhere but the same dead stream.
    /// </summary>
    private async Task WriteAndStallAsync(Stream stream, byte[] bytes, int holdMs = 5_000)
    {
        var buf = new byte[8192];
        // Read (and discard) one buffer of the request head; whether the head is complete does not
        // matter — the fake answers with headers and silence regardless.
        _ = await stream.ReadAsync(buf.AsMemory(0, buf.Length));
        await stream.WriteAsync(bytes);
        await stream.FlushAsync();
        // Silence: the stall. Then die, so the pool recycles.
        await Task.Delay(holdMs);
    }

    private Task StallAfterHeadersAsync(TcpClient client) =>
        WriteAndStallAsync(client.GetStream(), Encoding.ASCII.GetBytes(ChunkHeaders), _stallHoldMs);

    /// <summary>
    /// One connection per test, served in isolation. A stalled socket that outlives its test is
    /// held against the listener; if a later test shared it, its request would be swallowed by the
    /// first handler's silent hold and the test would fail for the wrong reason.
    /// </summary>
    private string ServedBaseUrl => BaseUrl;

    /// <summary>How long stall sockets stay open. Long enough to outlast the bound in each test.</summary>
    private int _stallHoldMs = 5_000;

    [Fact]
    public async Task HeadersThenSilence_DiesAtTheBodyBoundAndSurfacesAnError()
    {
        _stallHoldMs = 30_000; // the bound fires first; the socket lives well past it
        _handler = StallAfterHeadersAsync;
        var client = new OpenAiCompletionsClient { BodyTimeout = TimeSpan.FromSeconds(1), Retry = RetryPolicy.None };

        var events = new List<AssistantMessageEvent>();
        await foreach (var e in client.Stream(Model(BaseUrl), Context(), new StreamOptions())) events.Add(e);

        var error = Assert.IsType<AssistantError>(events[^1]);
        Assert.Equal(StopReason.Error, error.Reason);
        Assert.Contains("No SSE progress within 1s", error.Error.ErrorMessage, StringComparison.Ordinal);
        Assert.DoesNotContain(events, e => e is AssistantDone);
    }

    [Fact]
    public async Task CallerCancellationStillBeatsTheBodyBound()
    {
        // The abort is the user's and must stay "aborted" — never be rewritten into a timeout.
        // The stall outlasts both the bound and the caller's cancel, so the abort is the only
        // cancellation that can fire at all.
        _stallHoldMs = 30_000;
        _handler = StallAfterHeadersAsync;
        var client = new OpenAiCompletionsClient { BodyTimeout = TimeSpan.FromSeconds(30), Retry = RetryPolicy.None };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var events = new List<AssistantMessageEvent>();
        await foreach (var e in client.Stream(Model(BaseUrl), Context(), new StreamOptions(), cts.Token)) events.Add(e);

        Assert.DoesNotContain(events, e => e is AssistantDone);
        var error = Assert.IsType<AssistantError>(events[^1]);
        Assert.Equal(StopReason.Aborted, error.Reason);
        Assert.Equal("Request was aborted", error.Error.ErrorMessage);
    }

    [Fact]
    public async Task HeadersThenSilenceIsResentWhileNothingOfTheResponseExists()
    {
        // The stall diagnosis must be retryable in RetryPolicy's view: that is what lets a lost
        // response be resent. First connection answers headers then stalls forever; the retry
        // lands on a fresh connection that answers with a complete one-shot stream.
        var attempt = 0;
        _handler = async client =>
        {
            var n = Interlocked.Increment(ref attempt);
            var stream = client.GetStream();
            if (n == 1)
            {
                // Headers, then silence: the client's bound fires at 1s, the socket dies at 5s so
                // the pooled connection is released and the resend can reach the next accept.
                await WriteAndStallAsync(stream, Encoding.ASCII.GetBytes(ChunkHeaders), holdMs: 5_000);
                return;
            }

            var buf = new byte[8192];
            _ = await stream.ReadAsync(buf.AsMemory(0, buf.Length));
            await stream.WriteAsync(Encoding.ASCII.GetBytes(ChunkHeaders));
            // finish_reason must arrive before [DONE] or the assembler rejects the stream.
            var chunk1 = "data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"PONG\"}}]}\n\n";
            var chunk2 = "data: {\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"stop\"}]}\n\n";
            var body = chunk1 + chunk2 + "data: [DONE]\n\n";
            var bytes = Encoding.UTF8.GetBytes(body);
            await stream.WriteAsync(Encoding.ASCII.GetBytes(Convert.ToString(bytes.Length, 16) + "\r\n"));
            await stream.WriteAsync(bytes);
            await stream.WriteAsync(Encoding.ASCII.GetBytes("\r\n0\r\n\r\n"));
            await stream.FlushAsync();
            // Hold the socket until the client has read everything it wants; closing earlier would
            // turn a healthy completion into a premature end.
            await Task.Delay(1_000);
        };

        var client = new OpenAiCompletionsClient
        {
            BodyTimeout = TimeSpan.FromSeconds(1),
            Retry = new RetryPolicy { MaxRetries = 1, MaxBackoff = TimeSpan.FromMilliseconds(10) },
        };

        var events = new List<AssistantMessageEvent>();
        await foreach (var e in client.Stream(Model(BaseUrl), Context(), new StreamOptions())) events.Add(e);

        if (events[^1] is not AssistantDone done)
        {
            var err = Assert.IsType<AssistantError>(events[^1]);
            throw new Xunit.Sdk.XunitException($"stream ended in error: {err.Error.ErrorMessage} (attempts: {Volatile.Read(ref attempt)})");
        }
        Assert.Equal(StopReason.Stop, done.Reason);
        Assert.Equal("PONG", done.Message.Text);
        Assert.Equal(2, Volatile.Read(ref attempt));
    }

    [Fact]
    public async Task AStallAfterContentAlreadyStreamedDiesAndIsNeverResent()
    {
        // The chosen gate (wi-30/slice-a-numbers): mid-body always dies, headers-only retries.
        // The first chunk of an answer arrives, then silence; the partial already handed to the
        // caller must not be followed by a resent whole answer.
        var attempt = 0;
        _handler = async client =>
        {
            Interlocked.Increment(ref attempt);
            var stream = client.GetStream();
            var buf = new byte[8192];
            _ = await stream.ReadAsync(buf.AsMemory(0, buf.Length));
            await stream.WriteAsync(Encoding.ASCII.GetBytes(ChunkHeaders));

            // One complete chunk with content, so the reset moves the deadline past the stall...
            var chunk = "data: {\"choices\":[{\"delta\":{\"content\":\"HALF\"}}]}\n\n";
            var bytes = Encoding.UTF8.GetBytes(chunk);
            await stream.WriteAsync(Encoding.ASCII.GetBytes(Convert.ToString(bytes.Length, 16) + "\r\n"));
            await stream.WriteAsync(bytes);
            await stream.WriteAsync(Encoding.ASCII.GetBytes("\r\n"));
            await stream.FlushAsync();

            // The second chunk announces one byte and never delivers it: silence follows. On
            // attempt 1 the bound must kill the stream; the gate must not resend, so attempt 2
            // never happens.
            await stream.WriteAsync(Encoding.ASCII.GetBytes("1"));
            await stream.FlushAsync();
            await Task.Delay(5_000);
        };

        var client = new OpenAiCompletionsClient
        {
            BodyTimeout = TimeSpan.FromSeconds(1),
            // Patient would resend; if the gate works the run ends after one send.
            Retry = new RetryPolicy { MaxRetries = 4, MaxBackoff = TimeSpan.FromMilliseconds(10) },
            // The in-turn retry (pi-sharp-hh-7) would reconnect here; this test predates it and
            // exercises the total-time bound's "never resend after content" gate, so disable it.
            StreamRetries = 0,
        };

        var events = new List<AssistantMessageEvent>();
        await foreach (var e in client.Stream(Model(BaseUrl), Context(), new StreamOptions())) events.Add(e);

        var error = Assert.IsType<AssistantError>(events[^1]);
        Assert.Equal(StopReason.Error, error.Reason);
        Assert.Contains("No SSE progress within 1s", error.Error.ErrorMessage, StringComparison.Ordinal);
        Assert.Equal(1, Volatile.Read(ref attempt));
        // The partial that was already streamed is kept on the error message, not discarded.
        Assert.Contains("HALF", error.Error.Text);
    }

    [Fact]
    public async Task PooledDeadSocketFailsFastAsARetryableTransportError()
    {
        // The pooled-dead-socket case from the design acceptance bar: a connection that completed
        // one exchange and whose peer then died (RST) must make the *next* request fail as an
        // ordinary, retryable transport error — immediately, at the transport layer, and never as
        // the body bound. The client's TCP keepalive (10 min ping / 15 s timeout, D2) is what
        // bounds the pooled-idle case in production; in-process the kernel's answer to writing
        // into a socket whose peer vanished surfaces the same class of error. The assertion is
        // about the diagnosis (transport, not bound) and its speed (far below the bound).
        //
        // The listener accepts exactly one connection and then stops answering; the accept queue
        // is empty afterwards, so the client cannot learn from the listener that the peer is gone
        // and must ride the corpse sitting in its own connection pool.
        var attempt = 0;
        _handler = async client =>
        {
            var n = Interlocked.Increment(ref attempt);
            if (n == 1)
            {
                // RST on close: a dead peer, not a graceful FIN the pool could mistake for idle.
                client.Client.LingerState = new LingerOption(true, 0);
                var stream = client.GetStream();

                // Read the full request (head + body) before answering, as a real server must.
                var buf = new byte[8192];
                int total = 0;
                while (true)
                {
                    var r = await stream.ReadAsync(buf.AsMemory(total, buf.Length - total));
                    if (r == 0) break;
                    total += r;
                    var text = Encoding.ASCII.GetString(buf, 0, total);
                    var idx = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                    if (idx < 0) continue;
                    var hs = text.IndexOf("Content-Length:", StringComparison.Ordinal);
                    if (hs < 0) break;
                    var len = int.Parse(text[(hs + 15)..].Split("\r\n")[0].Trim());
                    if (total >= idx + 4 + len) break;
                }

                // One complete, valid exchange. Then the socket dies; the pool holds a corpse.
                // finish_reason arrives before [DONE] so the assembler accepts the stream.
                await stream.WriteAsync(Encoding.ASCII.GetBytes(ChunkHeaders));
                var chunk1 = "data: {\"choices\":[{\"index\":0,\"delta\":{\"role\":\"assistant\",\"content\":\"WARM\"}}]}\n\n";
                var chunk2 = "data: {\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n";
                var bytes = Encoding.UTF8.GetBytes(chunk1 + chunk2);
                await stream.WriteAsync(Encoding.ASCII.GetBytes(Convert.ToString(bytes.Length, 16) + "\r\n"));
                await stream.WriteAsync(bytes);
                await stream.WriteAsync(Encoding.ASCII.GetBytes("\r\n0\r\n\r\n"));
                await stream.FlushAsync();
                await Task.Delay(200);
            }
            // No further connection is served; the listener stays open, the queue stays empty.
        };

        var client = new OpenAiCompletionsClient { BodyTimeout = TimeSpan.FromSeconds(10), Retry = RetryPolicy.None };

        // Warm the pool: a completed exchange on connection A.
        var warm = new List<AssistantMessageEvent>();
        await foreach (var e in client.Stream(Model(BaseUrl), Context(), new StreamOptions())) warm.Add(e);
        if (warm[^1] is not AssistantDone warmDone)
        {
            var err = Assert.IsType<AssistantError>(warm[^1]);
            throw new Xunit.Sdk.XunitException($"warm exchange failed: {err.Error.ErrorMessage}");
        }
        Assert.Equal("WARM", warmDone.Message.Text);
        await Task.Delay(500); // let the corpse settle in the pool

        // Request 2 rides the dead pooled socket: no retry policy, so the failure surfaces as the
        // client's own diagnosis. It must be the transport error, not the body bound, and it must
        // arrive long before the 10 s bound could fire.
        var started = Environment.TickCount64;
        var events = new List<AssistantMessageEvent>();
        Exception? thrown = null;
        try
        {
            await foreach (var e in client.Stream(Model(BaseUrl), Context(), new StreamOptions())) events.Add(e);
        }
        catch (Exception ex)
        {
            thrown = ex;
        }
        var elapsedMs = Environment.TickCount64 - started;

        // Whether the diagnosis arrives as an event or escapes as an exception depends on where the
        // send failed; both carry the same message. What matters: transport, not bound, and fast.
        var message = thrown is not null
            ? thrown.GetBaseException().Message
            : Assert.IsType<AssistantError>(events[^1]).Error.ErrorMessage;
        Assert.DoesNotContain("No SSE progress", message, StringComparison.Ordinal);
        Assert.True(elapsedMs < 5_000, $"dead pooled socket took {elapsedMs} ms — transport must beat the body bound");
    }

    [Fact]
    public void TheDefaultBodyBoundIsTenMinutes_AndTheDiagnosisIsRetryable()
    {
        Assert.Equal(TimeSpan.FromMinutes(10), new OpenAiCompletionsClient().BodyTimeout);
        Assert.True(RetryPolicy.IsRetryableException(new HttpRequestException("No SSE progress within 600s from http://x")));
    }
}
