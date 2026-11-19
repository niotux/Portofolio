// A local vLLM serving Qwen3.x-27B now and then answers every request with nothing but '!' until it is
// restarted. The client drops such a reply, probes the model with a tiny request, waits while the probe is
// degenerate too, and re-issues the request once the model answers sanely. A raw socket fakes the server:
// streaming requests get the scripted SSE body, non-streaming ones are the probe.
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using PiSharp.Core.Agent;
using PiSharp.Core.Messages;
using PiSharp.Core.Models;
using PiSharp.Llm.Http;
using PiSharp.Llm.OpenAi;

namespace PiSharp.Tests.Llm;

public sealed class DegenerateOutputTests : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private Func<int, string>? _stream; // send number -> SSE content of that streamed reply
    private Func<int, string>? _probe;  // probe number -> answer text
    private int _sends;
    private int _probes;

    public DegenerateOutputTests()
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

    private string BaseUrl => $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/v1";

    private static ModelConfig Model(string baseUrl) => new() { Id = "fake", BaseUrl = baseUrl, ApiKey = "dummy" };

    private static LlmContext Context() => new("system", [new UserMessage("hello")], []);

    private async Task AcceptLoopAsync()
    {
        while (true)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(); }
            catch (ObjectDisposedException) { return; }
            catch (SocketException) { return; }
            _ = Task.Run(async () =>
            {
                try { await ServeAsync(client.GetStream()); }
                catch (Exception) { /* the client hung up */ }
                finally { client.Dispose(); }
            });
        }
    }

    private async Task ServeAsync(NetworkStream stream)
    {
        var body = await ReadRequestBodyAsync(stream);
        var json = JsonNode.Parse(body)!.AsObject();
        if (json["stream"]!.GetValue<bool>())
        {
            var n = Interlocked.Increment(ref _sends);
            await WriteSseAsync(stream, _stream!(n));
            return;
        }
        var p = Interlocked.Increment(ref _probes);
        var answer = new JsonObject
        {
            ["choices"] = new JsonArray(new JsonObject
            {
                ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = _probe!(p) },
                ["finish_reason"] = "stop",
            }),
        }.ToJsonString();
        var bytes = Encoding.UTF8.GetBytes(answer);
        await stream.WriteAsync(Encoding.ASCII.GetBytes(
            $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n"));
        await stream.WriteAsync(bytes);
        await stream.FlushAsync();
    }

    private static async Task<string> ReadRequestBodyAsync(NetworkStream stream)
    {
        var buffer = new List<byte>();
        var chunk = new byte[8192];
        int headerEnd;
        while ((headerEnd = IndexOf(buffer, "\r\n\r\n"u8.ToArray())) < 0)
        {
            var read = await stream.ReadAsync(chunk);
            if (read == 0) throw new IOException("closed");
            buffer.AddRange(chunk.AsSpan(0, read).ToArray());
        }
        var head = Encoding.ASCII.GetString(buffer.ToArray(), 0, headerEnd);
        var length = int.Parse(head.Split("\r\n").First(l => l.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            .Split(':')[1].Trim());
        while (buffer.Count < headerEnd + 4 + length)
        {
            var read = await stream.ReadAsync(chunk);
            if (read == 0) throw new IOException("closed");
            buffer.AddRange(chunk.AsSpan(0, read).ToArray());
        }
        return Encoding.UTF8.GetString(buffer.ToArray(), headerEnd + 4, length);
    }

    private static int IndexOf(List<byte> haystack, byte[] needle)
    {
        for (var i = 0; i + needle.Length <= haystack.Count; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length && match; j++) match = haystack[i + j] == needle[j];
            if (match) return i;
        }
        return -1;
    }

    /// <summary>Writes <paramref name="text"/> as 8-character content deltas, then a clean finish.</summary>
    private static async Task WriteSseAsync(NetworkStream stream, string text)
    {
        await stream.WriteAsync(Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 OK\r\nContent-Type: text/event-stream\r\nConnection: close\r\nTransfer-Encoding: chunked\r\n\r\n"));
        var events = new List<string>();
        for (var i = 0; i < text.Length; i += 8)
        {
            var part = text.Substring(i, Math.Min(8, text.Length - i));
            events.Add("data: {\"choices\":[{\"index\":0,\"delta\":{\"role\":\"assistant\",\"content\":"
                + JsonValue.Create(part).ToJsonString() + "}}]}\n\n");
        }
        events.Add("data: {\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n");
        foreach (var e in events)
        {
            var bytes = Encoding.UTF8.GetBytes(e);
            await stream.WriteAsync(Encoding.ASCII.GetBytes(Convert.ToString(bytes.Length, 16) + "\r\n"));
            await stream.WriteAsync(bytes);
            await stream.WriteAsync("\r\n"u8.ToArray());
            await stream.FlushAsync();
        }
        await stream.WriteAsync("0\r\n\r\n"u8.ToArray());
        await stream.FlushAsync();
    }

    private static readonly string Bangs = new('!', 200);

    private async Task<List<AssistantMessageEvent>> RunAsync(OpenAiCompletionsClient client, CancellationToken ct = default)
    {
        var events = new List<AssistantMessageEvent>();
        await foreach (var e in client.Stream(Model(BaseUrl), Context(), new StreamOptions(), ct)) events.Add(e);
        return events;
    }

    [Fact]
    public async Task BrokenModel_WaitsUntilAProbeIsSane_ThenResendsAndCompletes()
    {
        _stream = n => n == 1 ? "Sure" + Bangs : "fine answer";
        _probe = p => p < 3 ? "!!!!!!!!!!!!!!!!" : "ready";
        var waits = new List<TimeSpan>();
        var health = new List<ModelHealthEvent>();
        var client = new OpenAiCompletionsClient
        {
            Retry = RetryPolicy.None,
            Delay = (d, _) => { waits.Add(d); return Task.CompletedTask; },
            OnModelHealth = health.Add,
        };

        var events = await RunAsync(client);

        var done = Assert.IsType<AssistantDone>(events[^1]);
        Assert.Equal("fine answer", done.Message.Text);
        Assert.Equal(2, _sends);
        Assert.Equal(3, _probes);
        Assert.Single(events.OfType<AssistantDiscardPartial>());
        // The caller never saw the run reach the limit: the tripping chunk was held back.
        var seen = string.Concat(events.TakeWhile(e => e is not AssistantDiscardPartial).OfType<TextDelta>().Select(d => d.Delta));
        Assert.True(seen.Count(c => c == '!') < client.DegenerateRunLimit, seen);
        Assert.Equal([TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60)], waits);
        Assert.Equal([false, true], health.Select(h => h.Healthy));
        Assert.Equal(health[0].Since, health[1].Since);
    }

    [Fact]
    public async Task ProbeBackoff_DoublesUpToTheCap()
    {
        _stream = n => n == 1 ? Bangs : "ok";
        _probe = p => p <= 6 ? "" : "ready";
        var waits = new List<TimeSpan>();
        var client = new OpenAiCompletionsClient
        {
            Retry = RetryPolicy.None,
            DegenerateProbeBackoff = TimeSpan.FromSeconds(1),
            DegenerateProbeMaxBackoff = TimeSpan.FromSeconds(5),
            Delay = (d, _) => { waits.Add(d); return Task.CompletedTask; },
        };

        var events = await RunAsync(client);

        Assert.IsType<AssistantDone>(events[^1]);
        Assert.Equal(new[] { 1, 2, 4, 5, 5, 5 }.Select(s => TimeSpan.FromSeconds(s)), waits);
    }

    [Fact]
    public async Task SaneProbeButTheSameRequestStaysDegenerate_FailsTheTurnAfterOneResend()
    {
        _stream = _ => Bangs;
        _probe = _ => "ready";
        var health = new List<ModelHealthEvent>();
        var client = new OpenAiCompletionsClient
        {
            Retry = RetryPolicy.None,
            Delay = (_, _) => throw new InvalidOperationException("no wait expected"),
            OnModelHealth = health.Add,
        };

        var events = await RunAsync(client);

        var error = Assert.IsType<AssistantError>(events[^1]);
        Assert.Equal(StopReason.Error, error.Reason);
        Assert.Contains("Degenerate output", error.Error.ErrorMessage, StringComparison.Ordinal);
        Assert.Equal("degenerate_output", error.Error.ErrorCode);
        Assert.True(error.Error.ProviderRequestFailed);
        Assert.Equal(2, _sends);
        Assert.Equal(2, _probes);
        Assert.Empty(health);
    }

    [Fact]
    public async Task CancellationDuringTheWait_AbortsTheTurn()
    {
        _stream = _ => Bangs;
        _probe = _ => Bangs;
        using var cts = new CancellationTokenSource();
        var client = new OpenAiCompletionsClient
        {
            Retry = RetryPolicy.None,
            Delay = (_, ct) => { cts.Cancel(); return Task.Delay(Timeout.Infinite, ct); },
        };

        var events = await RunAsync(client, cts.Token);

        var error = Assert.IsType<AssistantError>(events[^1]);
        Assert.Equal(StopReason.Aborted, error.Reason);
        Assert.Equal(1, _sends);
    }

    [Fact]
    public async Task ExclamationsBelowTheLimit_AndADisabledLimit_PassThrough()
    {
        _stream = _ => "Great!!! Done!" + new string('!', 40);
        _probe = _ => throw new InvalidOperationException("no probe expected");
        var events = await RunAsync(new OpenAiCompletionsClient { Retry = RetryPolicy.None });
        Assert.Equal("Great!!! Done!" + new string('!', 40), Assert.IsType<AssistantDone>(events[^1]).Message.Text);

        _stream = _ => Bangs;
        events = await RunAsync(new OpenAiCompletionsClient { Retry = RetryPolicy.None, DegenerateRunLimit = 0 });
        Assert.Equal(Bangs, Assert.IsType<AssistantDone>(events[^1]).Message.Text);
        Assert.Equal(0, _probes);
    }

    [Fact]
    public void Run_SpansDeltasAndResetsOnAnyOtherCharacter()
    {
        var run = new DegenerateRun();
        Assert.Equal(3, run.Feed("ab!!!"));
        Assert.Equal(7, run.Feed("!!!!"));
        Assert.Equal(0, run.Feed("! x"));
        Assert.Equal(2, run.Feed("!!"));
    }

    [Theory]
    [InlineData("ready", true)]
    [InlineData("Ready.", true)]
    [InlineData("!!!!!!!!", false)]
    [InlineData("ready!!!!!!!!!", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Probe_IsSane(string? answer, bool sane) => Assert.Equal(sane, DegenerateProbe.IsSane(answer));

    [Fact]
    public void Probe_ReadsContentAndReasoning()
    {
        Assert.Equal("ready", DegenerateProbe.ReadAnswer("{\"choices\":[{\"message\":{\"content\":\"ready\"}}]}"));
        Assert.Equal("hmm", DegenerateProbe.ReadAnswer("{\"choices\":[{\"message\":{\"content\":null,\"reasoning_content\":\"hmm\"}}]}"));
        Assert.Null(DegenerateProbe.ReadAnswer("{\"choices\":[]}"));
        Assert.Null(DegenerateProbe.ReadAnswer("not json"));
    }
}
