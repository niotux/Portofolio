// MCP transports: stdio (newline JSON-RPC over a child process) and streamable HTTP
// (POST with Accept: application/json, text/event-stream and Mcp-Session-Id).
// Library map: docs/02-library-map.md. Spec: MCP 2025-06-18.
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace PiSharp.Core.Mcp;

public enum McpTransportKind
{
    Stdio,
    Http,
}

/// <summary>Transport over an existing <see cref="JsonRpcPeer"/> (in-process tests, pipe loopbacks).</summary>
public sealed class PeerTransport : IMcpTransport
{
    private readonly JsonRpcPeer _peer;

    public PeerTransport(JsonRpcPeer peer) => _peer = peer;

    public event Action<string, JsonNode?>? Notification
    {
        add => _peer.Notification += value;
        remove => _peer.Notification -= value;
    }

    public Task<JsonNode?> SendRequestAsync(string method, JsonNode? @params, CancellationToken cancellationToken) =>
        _peer.RequestAsync(method, @params, cancellationToken);

    public Task SendNotificationAsync(string method, JsonNode? @params, CancellationToken cancellationToken) =>
        _peer.NotifyAsync(method, @params, cancellationToken);

    public ValueTask DisposeAsync() => _peer.DisposeAsync();
}

/// <summary>Sends JSON-RPC requests and notifications to one MCP server.</summary>
public interface IMcpTransport : IAsyncDisposable
{
    Task<JsonNode?> SendRequestAsync(string method, JsonNode? @params, CancellationToken cancellationToken);
    Task SendNotificationAsync(string method, JsonNode? @params, CancellationToken cancellationToken);
    event Action<string, JsonNode?>? Notification;
}

/// <summary>Stdio transport: spawn <c>command args</c> and speak newline-delimited JSON-RPC on stdin/stdout.</summary>
public sealed class StdioTransport : IMcpTransport
{
    private readonly Process _process;
    private readonly JsonRpcPeer _peer;
    private readonly Task _stderrDrain;
    private readonly StringBuilder _stderr;

    public event Action<string, JsonNode?>? Notification
    {
        add => _peer.Notification += value;
        remove => _peer.Notification -= value;
    }

    /// <summary>Captured stderr (best-effort). Useful when initialize fails.</summary>
    public string StandardError
    {
        get { lock (_stderr) return _stderr.ToString(); }
    }

    public StdioTransport(Process process, JsonRpcPeer peer, Task stderrDrain, StringBuilder stderr)
    {
        _process = process;
        _peer = peer;
        _stderrDrain = stderrDrain;
        _stderr = stderr;
    }

    public static StdioTransport Start(McpServerConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.Command))
            throw new ArgumentException($"MCP server \"{config.Alias}\" has no command.", nameof(config));

        var psi = new ProcessStartInfo
        {
            FileName = config.Command,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };
        foreach (var arg in config.Args) psi.ArgumentList.Add(arg);
        if (!string.IsNullOrEmpty(config.Cwd)) psi.WorkingDirectory = config.Cwd;
        foreach (var (key, value) in config.Env)
            psi.Environment[key] = McpConfig.ExpandEnv(value);

        var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start MCP server \"{config.Alias}\" ({config.Command}).");

        var stderr = new StringBuilder();
        var drain = DrainStderrAsync(process, stderr);
        var peer = new JsonRpcPeer(process.StandardOutput.BaseStream, process.StandardInput.BaseStream);
        return new StdioTransport(process, peer, drain, stderr);
    }

    public Task<JsonNode?> SendRequestAsync(string method, JsonNode? @params, CancellationToken cancellationToken) =>
        _peer.RequestAsync(method, @params, cancellationToken);

    public Task SendNotificationAsync(string method, JsonNode? @params, CancellationToken cancellationToken) =>
        _peer.NotifyAsync(method, @params, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await _peer.DisposeAsync().ConfigureAwait(false);
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                _ = _process.WaitForExit(2000);
            }
        }
        catch { /* already gone */ }
        try { await _stderrDrain.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false); } catch { }
        _process.Dispose();
    }

    private static async Task DrainStderrAsync(Process process, StringBuilder sink)
    {
        try
        {
            var buf = new char[1024];
            while (true)
            {
                var n = await process.StandardError.ReadAsync(buf, 0, buf.Length).ConfigureAwait(false);
                if (n <= 0) break;
                lock (sink) sink.Append(buf, 0, n);
            }
        }
        catch { /* process ended */ }
    }
}

/// <summary>
/// Streamable HTTP transport: one POST per JSON-RPC message.
/// Honors <c>Mcp-Session-Id</c>, JSON bodies, and SSE (<c>text/event-stream</c>) bodies.
/// Server-initiated notifications over GET SSE are Phase 8.
///
/// Stale-session recovery (accepted ADR pi-sharp-codeintel-mcp-stale-session-recovery):
/// a 404 for a call sent with an existing session id, or a -32600 carrying the legacy
/// missing-session marker (pre-1.0.6 servers answered 200 + -32600), clears the session,
/// re-initializes once, and replays the call once. A second stale response surfaces; 500s
/// and generic errors never trigger recovery.
/// </summary>
public sealed class StreamableHttpTransport : IMcpTransport
{
    public const string ProtocolVersion = McpClient.ProtocolVersion;
    private const string MissingSessionMarker = "unknown or missing session";

    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly Uri _url;
    private readonly IReadOnlyDictionary<string, string> _headers;
    private readonly SemaphoreSlim _recoverLock = new(1, 1);
    private long _nextId;
    private string? _sessionId;

    public event Action<string, JsonNode?>? Notification;

    public string? SessionId => _sessionId;

    public StreamableHttpTransport(
        string url,
        IReadOnlyDictionary<string, string>? headers = null,
        HttpClient? http = null)
    {
        _url = new Uri(url);
        _headers = headers ?? new Dictionary<string, string>();
        if (http is null)
        {
            _http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            _ownsHttp = true;
        }
        else
        {
            _http = http;
            _ownsHttp = false;
        }
    }

    public async Task<JsonNode?> SendRequestAsync(string method, JsonNode? @params, CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _nextId);
        var msg = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["method"] = method,
        };
        if (@params is not null) msg["params"] = @params.DeepClone();

        var (response, httpStatus) = await SendWithSessionAsync(msg, cancellationToken).ConfigureAwait(false);
        try
        {
            // Happy path: not a 404.
            if (httpStatus != 404)
            {
                JsonNode? frame = response is null
                    ? null
                    : await ReadResponseAsync(response, cancellationToken).ConfigureAwait(false);
                // Legacy missing-session check: -32600 with the known marker.
                if (IsMissingSessionFrame(frame))
                {
                    if (_sessionId is null)
                        throw new JsonRpcException(JsonRpcException.InvalidRequest, MissingSessionMarker);
                    await ReinitializeOnceAsync(cancellationToken).ConfigureAwait(false);
                    var legacyReplayId = Interlocked.Increment(ref _nextId);
                    msg["id"] = legacyReplayId;
                    var (legacyReplay, legacyReplayStatus) = await SendWithSessionAsync(msg, cancellationToken).ConfigureAwait(false);
                    try
                    {
                        if (legacyReplayStatus == 404)
                            throw new JsonRpcException(
                                JsonRpcException.InvalidRequest,
                                $"MCP session still stale after one re-initialization: HTTP 404");
                        JsonNode? legacyReplayFrame = legacyReplay is null
                            ? null
                            : await ReadResponseAsync(legacyReplay, cancellationToken).ConfigureAwait(false);
                        return ReadRpcResult(legacyReplayFrame, legacyReplayId.ToString());
                    }
                    finally
                    {
                        legacyReplay?.Dispose();
                    }
                }
                return ReadRpcResult(frame, id.ToString());
            }

            // 404: stale session. Recover exactly once per logical call, then replay exactly once.
            if (_sessionId is null)
                throw new JsonRpcException(JsonRpcException.InvalidRequest, MissingSessionMarker);
            await ReinitializeOnceAsync(cancellationToken).ConfigureAwait(false);

            var replayId = Interlocked.Increment(ref _nextId);
            msg["id"] = replayId;
            var (replay, replayStatus) = await SendWithSessionAsync(msg, cancellationToken).ConfigureAwait(false);
            try
            {
                if (replayStatus == 404)
                    throw new JsonRpcException(
                        JsonRpcException.InvalidRequest,
                        $"MCP session still stale after one re-initialization: HTTP 404");
                JsonNode? replayFrame = replay is null
                    ? null
                    : await ReadResponseAsync(replay, cancellationToken).ConfigureAwait(false);
                return ReadRpcResult(replayFrame, replayId.ToString());
            }
            finally
            {
                replay?.Dispose();
            }
        }
        finally
        {
            response?.Dispose();
        }
    }

    /// <summary>Checks whether a parsed JSON-RPC frame is the legacy missing-session error: -32600 with the known marker.</summary>
    private static bool IsMissingSessionFrame(JsonNode? frame)
    {
        if (frame is not JsonObject obj) return false;
        if (obj["error"] is not JsonObject err) return false;
        if (err["code"]?.GetValue<int>() != -32600) return false;
        var msg = err["message"]?.GetValue<string>();
        return msg is not null && msg.Contains(MissingSessionMarker, StringComparison.Ordinal);
    }

    /// <summary>POSTs one message; returns the open response (caller disposes) and the HTTP status.
    /// For 404 (unknown/expired session) returns (null, 404) so the caller can recover.
    /// Transient 500s propagate via EnsureSuccessStatusCode: no session reset, no re-init.</summary>
    private async Task<(HttpResponseMessage? response, int httpStatus)> SendWithSessionAsync(
        JsonObject message, CancellationToken cancellationToken)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, _url)
        {
            Content = new StringContent(message.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        req.Headers.TryAddWithoutValidation("MCP-Protocol-Version", ProtocolVersion);
        if (_sessionId is not null)
            req.Headers.TryAddWithoutValidation("Mcp-Session-Id", _sessionId);
        foreach (var (key, value) in _headers)
            req.Headers.TryAddWithoutValidation(key, McpConfig.ExpandEnv(value));

        var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (resp.Headers.TryGetValues("Mcp-Session-Id", out var ids))
            _sessionId = ids.FirstOrDefault() ?? _sessionId;

        // A 404 (unknown/expired session) must not raise a HttpRequestException before recovery can look at it.
        if ((int)resp.StatusCode == 404)
            return (null, 404);

        resp.EnsureSuccessStatusCode(); // transient 500s etc. propagate untouched: no session reset, no re-init

        if ((int)resp.StatusCode == 202) return (null, 202);

        return (resp, (int)resp.StatusCode);
    }

    private async Task<JsonNode?> ReadResponseAsync(HttpResponseMessage resp, CancellationToken cancellationToken)
    {
        var media = resp.Content.Headers.ContentType?.MediaType ?? "application/json";
        await using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        if (media.Contains("event-stream", StringComparison.OrdinalIgnoreCase))
            return await ReadSseJsonAsync(stream, cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var body = await BoundedReader.ReadToEndAsync(reader, cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(body) ? null : JsonNode.Parse(body);
    }

    /// <summary>The lists a client caches from initialize onward. A new session is a server that may have restarted
    /// on a new release, so all three are reported changed.</summary>
    private static readonly string[] ListChangedNotifications =
        ["notifications/tools/list_changed", "notifications/prompts/list_changed", "notifications/resources/list_changed"];

    private async Task ReinitializeOnceAsync(CancellationToken cancellationToken)
    {
        if (await TryReinitializeAsync(cancellationToken).ConfigureAwait(false))
        {
            // Team watch 2026-10-02: kb 1.0.65 added kb_transition wait_until, and every seat kept the old schema after
            // the KB restart because the session was renewed here silently while the tool list was only refetched on a
            // server list_changed, which a restarted server never sends to the old session. Raised outside the lock:
            // the refresh calls back into this transport.
            foreach (var method in ListChangedNotifications)
                Notification?.Invoke(method, null);
        }
    }

    private async Task<bool> TryReinitializeAsync(CancellationToken cancellationToken)
    {
        // Bounded once per call, and once across callers: the lock serializes concurrent recovery and the
        // fresh-session check makes a second attempt a no-op (no re-init storm).
        await _recoverLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_sessionId is null) return false;
            _sessionId = null;
            var init = new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = Interlocked.Increment(ref _nextId),
                ["method"] = "initialize",
                ["params"] = new JsonObject
                {
                    ["protocolVersion"] = ProtocolVersion,
                    ["clientInfo"] = new JsonObject
                    {
                        ["name"] = McpClient.ClientName,
                        ["version"] = McpClient.ClientVersion,
                    },
                    ["capabilities"] = new JsonObject(),
                },
            };
            var resp = await _http.PostAsync(_url, new StringContent(init.ToJsonString(), Encoding.UTF8, "application/json"), cancellationToken).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            if (resp.Headers.TryGetValues("Mcp-Session-Id", out var ids))
                _sessionId = ids.FirstOrDefault();
            if (_sessionId is null)
                throw new JsonRpcException(JsonRpcException.InternalError, "re-initialization did not return a Mcp-Session-Id");
            using var notif = new HttpRequestMessage(HttpMethod.Post, _url)
            {
                Content = new StringContent(
                    $"{{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}}", Encoding.UTF8, "application/json"),
            };
            notif.Headers.TryAddWithoutValidation("Mcp-Session-Id", _sessionId);
            using var nresp = await _http.SendAsync(notif, cancellationToken).ConfigureAwait(false);
            nresp.EnsureSuccessStatusCode();
            return true;
        }
        finally
        {
            _recoverLock.Release();
        }
    }

    public async Task SendNotificationAsync(string method, JsonNode? @params, CancellationToken cancellationToken)
    {
        var msg = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = method,
        };
        if (@params is not null) msg["params"] = @params.DeepClone();
        var (response, _) = await SendWithSessionAsync(msg, cancellationToken).ConfigureAwait(false);
        response?.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        if (_ownsHttp) _http.Dispose();
        return ValueTask.CompletedTask;
    }



    private JsonNode? ReadRpcResult(JsonNode? frame, string idKey)
    {
        if (frame is null) return null;
        if (frame is not JsonObject obj) return frame;

        var method = obj["method"]?.GetValue<string>();
        if (method is not null && obj["id"] is null)
        {
            Notification?.Invoke(method, obj["params"]);
            return null;
        }

        if (obj.ContainsKey("error"))
        {
            var err = obj["error"] as JsonObject;
            throw new JsonRpcException(
                err?["code"]?.GetValue<int>() ?? JsonRpcException.InternalError,
                err?["message"]?.GetValue<string>() ?? "JSON-RPC error",
                err?["data"]);
        }

        if (obj["id"] is JsonNode id && JsonRpcPeer.IdKey(id) != idKey)
            throw new JsonRpcException(JsonRpcException.InternalError, $"HTTP MCP response id mismatch: expected {idKey}");

        return obj.ContainsKey("result") ? obj["result"] : null;
    }

    /// <summary>Yields the first JSON-RPC response from an SSE body; extra events are raised as notifications.</summary>
    private async Task<JsonNode?> ReadSseJsonAsync(Stream stream, CancellationToken cancellationToken)
    {
        JsonNode? first = null;
        await foreach (var data in McpSse.ReadDataAsync(stream, cancellationToken).ConfigureAwait(false))
        {
            if (string.IsNullOrWhiteSpace(data) || data == "[DONE]") continue;
            JsonNode? node;
            try { node = JsonNode.Parse(data); }
            catch { continue; }
            if (node is JsonObject obj && obj["method"] is not null && obj["id"] is null)
            {
                Notification?.Invoke(obj["method"]!.GetValue<string>(), obj["params"]);
                continue;
            }
            first ??= node;
        }
        return first;
    }
}

/// <summary>Minimal SSE data-frame reader (WHATWG subset: <c>data:</c> lines, blank-line separator).</summary>
internal static class McpSse
{
    public static async IAsyncEnumerable<string> ReadDataAsync(
        Stream stream,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var lines = new BoundedLineReader(stream);
        var data = new StringBuilder();
        var hasData = false;

        while (true)
        {
            var line = await lines.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                if (hasData) yield return data.ToString();
                yield break;
            }

            if (line.Length == 0)
            {
                if (hasData)
                {
                    var payload = data.ToString();
                    data.Clear();
                    hasData = false;
                    if (payload == "[DONE]") yield break;
                    yield return payload;
                }
                continue;
            }

            if (line[0] == ':') continue;

            string field, value;
            var colon = line.IndexOf(':');
            if (colon < 0)
            {
                field = line;
                value = "";
            }
            else
            {
                field = line[..colon];
                value = line[(colon + 1)..];
                if (value.Length > 0 && value[0] == ' ') value = value[1..];
            }

            if (field == "data")
            {
                if (data.Length + value.Length > BoundedReader.MaxCharacters) throw new IOException("MCP SSE frame exceeds the response budget.");
                if (hasData) data.Append('\n');
                data.Append(value);
                hasData = true;
            }
        }
    }
}
