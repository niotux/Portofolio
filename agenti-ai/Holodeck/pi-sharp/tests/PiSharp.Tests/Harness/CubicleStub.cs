// In-process stand-in for the chat server's /cubicle endpoints. Records every request; the handler decides
// the answer (202 / 404 / connection reset / delay). Uses HttpListener so the real HttpClient path is exercised.
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;

namespace PiSharp.Tests.Harness;

internal sealed record StubRequest(string Method, string PathAndQuery, string? Authorization, string Body)
{
    public JsonArray Events => Body.Length > 0 && JsonNode.Parse(Body) is JsonArray arr ? arr : [];
    public long Since => long.Parse(Query("since") ?? "-1", System.Globalization.CultureInfo.InvariantCulture);
    public int Wait => int.Parse(Query("wait") ?? "-1", System.Globalization.CultureInfo.InvariantCulture);

    public string? Query(string key)
    {
        var q = PathAndQuery.IndexOf('?', StringComparison.Ordinal);
        if (q < 0) return null;
        foreach (var pair in PathAndQuery[(q + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=', StringComparison.Ordinal);
            if (eq > 0 && pair[..eq] == key) return Uri.UnescapeDataString(pair[(eq + 1)..]);
        }
        return null;
    }
}

/// <summary>Status -1 means "reset the connection without answering".</summary>
internal sealed record StubResponse(int Status, string Body = "", TimeSpan Delay = default)
{
    public static readonly StubResponse Reset = new(-1);
    public static StubResponse Accepted(long last = 1) => new(202, new JsonObject { ["room"] = "cubicle-test", ["last"] = last }.ToJsonString());
    public static StubResponse Steering(long last, params (long Id, string From, string Text)[] messages)
    {
        var arr = new JsonArray();
        foreach (var (id, from, text) in messages)
            arr.Add((JsonNode)new JsonObject { ["id"] = id, ["from"] = from, ["at"] = "2026-09-13T10:00:00Z", ["text"] = text });
        return new StubResponse(200, new JsonObject { ["messages"] = arr, ["last"] = last }.ToJsonString());
    }
}

internal sealed class CubicleStub : IDisposable
{
    private readonly HttpListener _listener;
    private readonly Task _loop;
    private readonly SemaphoreSlim _arrived = new(0);

    public CubicleStub()
    {
        // FreePort releases the port before the listener binds it, so a parallel test can take it in between
        // (coder-06 gate, 2026-10-02: "Address already in use"). Take another port when that happens.
        for (var attempt = 1; ; attempt++)
        {
            Url = $"http://127.0.0.1:{FreePort()}";
            _listener = new HttpListener();
            _listener.Prefixes.Add(Url + "/");
            try { _listener.Start(); break; }
            catch (HttpListenerException) when (attempt < 10) { _listener.Close(); }
        }
        _loop = Task.Run(LoopAsync);
    }

    public string Url { get; }

    public ConcurrentQueue<StubRequest> Requests { get; } = new();

    public IEnumerable<StubRequest> Posts => Requests.Where(r => r.Method == "POST");

    public IEnumerable<StubRequest> Gets => Requests.Where(r => r.Method == "GET");

    /// <summary>Decides the answer. Default: 202 for POST, 200 with no messages for GET.</summary>
    public Func<StubRequest, Task<StubResponse>> Handler { get; set; } = req => Task.FromResult(
        req.Method == "POST" ? StubResponse.Accepted() : StubResponse.Steering(req.Since));

    /// <summary>Waits until at least <paramref name="count"/> requests have been recorded.</summary>
    public async Task<IReadOnlyList<StubRequest>> WaitForRequestsAsync(int count, TimeSpan? timeout = null)
    {
        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(10));
        while (Requests.Count < count)
        {
            if (!await _arrived.WaitAsync(timeout ?? TimeSpan.FromSeconds(10), cts.Token).ConfigureAwait(false))
                throw new TimeoutException($"Expected {count} requests, got {Requests.Count}");
        }
        return Requests.ToArray();
    }

    private async Task LoopAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync().ConfigureAwait(false); }
            catch (HttpListenerException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (InvalidOperationException) { return; }
            _ = Task.Run(() => HandleAsync(ctx));
        }
    }

    private async Task HandleAsync(HttpListenerContext ctx)
    {
        string body;
        using (var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
            body = await reader.ReadToEndAsync().ConfigureAwait(false);
        var recorded = new StubRequest(
            ctx.Request.HttpMethod,
            ctx.Request.Url?.PathAndQuery ?? "",
            ctx.Request.Headers["Authorization"],
            body);
        Requests.Enqueue(recorded);
        _arrived.Release();
        StubResponse response;
        try { response = await Handler(recorded).ConfigureAwait(false); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { response = new StubResponse(500, ex.Message); }
        if (response.Delay > TimeSpan.Zero) await Task.Delay(response.Delay).ConfigureAwait(false);
        if (response.Status < 0)
        {
            // The managed HttpListener on Linux can still flush a status line on Abort (Windows drops the
            // socket). Stage a 5xx first so the client sees "unavailable" on both platforms.
            ctx.Response.StatusCode = 503;
            ctx.Response.KeepAlive = false;
            ctx.Response.Abort();
            return;
        }
        try
        {
            var bytes = Encoding.UTF8.GetBytes(response.Body);
            ctx.Response.StatusCode = response.Status;
            ctx.Response.ContentType = "application/json";
            ctx.Response.ContentLength64 = bytes.Length;
            await ctx.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
            ctx.Response.Close();
        }
        catch (HttpListenerException)
        {
            // Client went away (timeout tests); nothing to do.
        }
    }

    private static int FreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }

    public void Dispose()
    {
        try { _listener.Stop(); _listener.Close(); }
        catch (ObjectDisposedException) { }
        catch (HttpListenerException) when (OperatingSystem.IsLinux()) { /* managed HttpListener can race endpoint teardown after an aborted response */ }
        _arrived.Dispose();
    }
}
