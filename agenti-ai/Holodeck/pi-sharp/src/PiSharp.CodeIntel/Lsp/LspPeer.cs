// LSP JSON-RPC with Content-Length framing. LSP (and csharp-ls/roslyn) frame messages as
//   Content-Length: N\r\n\r\n<N bytes of UTF-8 JSON>
// which is different from MCP stdio (one JSON object per line): PiSharp's JsonRpcPeer is
// newline-only and cannot talk to a language server, so this framing lives here.
using System.Buffers;
using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PiSharp.CodeIntel.Lsp;

/// <summary>LSP protocol error (wire <c>error</c> object), LSP 3.16 §3.2.3.1.</summary>
public sealed class LspRpcException : Exception
{
    public LspRpcException(int code, string message, JsonNode? data = null) : base(message)
    {
        Code = code;
        ErrorData = data;
    }

    public int Code { get; }
    public JsonNode? ErrorData { get; }

    public const int ParseError = -32700;
    public const int InvalidRequest = -32600;
    public const int MethodNotFound = -32601;
    public const int InvalidParams = -32602;
    public const int InternalError = -32603;
}

/// <summary>
/// Bidirectional LSP JSON-RPC 2.0 over a pair of streams, Content-Length framed.
/// Messages may contain newlines; framing is by length header only.
/// </summary>
public sealed class LspPeer : IAsyncDisposable
{
    private readonly Stream _input;
    private readonly Stream _output;
    private readonly Func<string, JsonNode?, CancellationToken, Task<JsonNode?>>? _onRequest;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonNode?>> _pending = new();
    private readonly CancellationTokenSource _readCts = new();
    private readonly Task _readTask;
    private long _nextId;

    /// <summary>Raised for notifications (no <c>id</c>) from the other side.</summary>
    public event Action<string, JsonNode?>? Notification;

    /// <summary>Raised for incoming requests (have an <c>id</c> and expect a reply). The handler's
    /// return value is the result sent back; returning null sends a JSON null result.</summary>
    public event Func<string, JsonNode?, CancellationToken, Task<JsonNode?>>? Request;

    /// <summary>Completes when the read loop hits EOF or the peer is disposed.</summary>
    public Task Completion => _readTask;

    public LspPeer(
        Stream input,
        Stream output,
        Func<string, JsonNode?, CancellationToken, Task<JsonNode?>>? onRequest = null)
    {
        _input = input;
        _output = output;
        _onRequest = onRequest;
        if (onRequest is not null) Request += onRequest;
        _readTask = Task.Run(() => ReadLoopAsync(_readCts.Token));
    }

    public async Task<JsonNode?> RequestAsync(string method, JsonNode? @params, CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _nextId).ToString();
        var tcs = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;
        try
        {
            await SendAsync(new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id,
                ["method"] = method,
                ["params"] = @params,
            }, cancellationToken);
            using var _ = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
            return await tcs.Task;
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    public Task NotifyAsync(string method, JsonNode? @params, CancellationToken cancellationToken) =>
        SendAsync(new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = method,
            ["params"] = @params,
        }, cancellationToken);

    private async Task SendAsync(JsonObject message, CancellationToken cancellationToken)
    {
        // Single-line JSON; the frame length is measured on the exact UTF-8 bytes written.
        var json = Encoding.UTF8.GetBytes(message.ToJsonString());
        var header = Encoding.ASCII.GetBytes($"Content-Length: {json.Length}\r\n\r\n");
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await _output.WriteAsync(header, cancellationToken);
            await _output.WriteAsync(json, cancellationToken);
            await _output.FlushAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task ReadLoopAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var length = await ReadContentLengthAsync(token);
                if (length < 0) break;
                var body = new byte[length];
                var read = 0;
                while (read < length)
                {
                    var n = await _input.ReadAsync(body.AsMemory(read, length - read), token);
                    if (n == 0)
                    {
                        // Frame shorter than promised (EOF before the promised bytes): the
                        // connection is broken; stop cleanly.
                        read = -1;
                        break;
                    }
                    read += n;
                }
                if (read < length) break;
                JsonNode? message;
                try
                {
                    message = JsonNode.Parse(Encoding.UTF8.GetString(body));
                }
                catch (JsonException)
                {
                    continue; // A malformed frame must not kill the session (LSP §3.1).
                }
                if (message is JsonObject obj)
                    await HandleAsync(obj, token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (EndOfStreamException)
        {
        }
        catch (IOException)
        {
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[LspPeer] ReadLoop crashed: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
        }
        finally
        {
            foreach (var pending in _pending)
                pending.Value.TrySetCanceled(_readCts.Token);
        }
    }

    /// <summary>Parses a Content-Length header from the stream; -1 on clean EOF.</summary>
    private async Task<int> ReadContentLengthAsync(CancellationToken token)
    {
        // Header area is small; read byte by byte until the blank line of the header block.
        var header = new byte[4096];
        int pos = 0;
        while (true)
        {
            var i = await _input.ReadAsync(header.AsMemory(pos, 1), token);
            if (i == 0) return -1;
            pos++;
            if (pos >= header.Length)
                throw new InvalidOperationException("LSP header block exceeded 4096 bytes.");
            if (pos >= 4 && header.AsSpan(pos - 4, 4).SequenceEqual(new byte[] { 0x0D, 0x0A, 0x0D, 0x0A }))
                break;
        }
        var text = Encoding.ASCII.GetString(header, 0, pos);
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim('\r', ' ');
            if (trimmed.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            {
                var value = trimmed["Content-Length:".Length..].Trim();
                if (long.TryParse(value, out var length) && length >= 0)
                    return (int)length;
            }
        }
        throw new InvalidOperationException("LSP header block has no Content-Length.");
    }

    private async Task HandleAsync(JsonObject message, CancellationToken token)
    {
        var method = message["method"]?.GetValue<string>();
        var idNode = message["id"];
        // hasId: the message has an "id" field that is not null. LSP ids can be strings or numbers.
        var hasId = idNode is not null && !(idNode is JsonValue v && v.TryGetValue<string>(out _) && v.ToJsonString() == "null");
        if (method is null)
        {
            // A response to one of our own requests. LSP ids can be strings or numbers;
            // our own requests always use string ids, so normalise to string for the lookup.
            string? id = null;
            if (idNode is JsonValue jv)
            {
                if (jv.TryGetValue<string>(out var s)) id = s;
                else if (jv.TryGetValue<long>(out var l)) id = l.ToString();
                else if (jv.TryGetValue<int>(out var i)) id = i.ToString();
            }
            if (id is not null && _pending.TryRemove(id, out var tcs))
            {
                if (message["error"] is JsonObject err && err["code"] is { } code && err["message"] is { } msg)
                    tcs.TrySetException(new LspRpcException(code.GetValue<int>(), msg.GetValue<string>(), err["data"]));
                else
                    tcs.TrySetResult(message["result"]);
            }
            return;
        }

        var @params = message["params"];
        if (hasId)
        {
            var handler = Request;
            JsonNode? result = null;
            LspRpcException? failure = null;
            if (handler is null)
            {
                // No handler: -32601, and we keep running (LSP §3.8: a request with an unknown
                // method MUST be answered, not dropped).
                failure = new LspRpcException(LspRpcException.MethodNotFound, $"Method not found: {method}");
            }
            else
            {
                try
                {
                    result = await handler(method, @params, token);
                }
                catch (OperationCanceledException)
                {
                    // The session is going away; no reply is possible.
                    return;
                }
                catch (LspRpcException e)
                {
                    failure = e;
                }
                catch (Exception e)
                {
                    failure = new LspRpcException(LspRpcException.InternalError, e.Message);
                }
            }
            var reply = new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = idNode!,
            };
            if (failure is not null)
            {
                reply["error"] = new JsonObject
                {
                    ["code"] = failure.Code,
                    ["message"] = failure.Message,
                };
                if (failure.ErrorData is not null)
                    reply["error"]!.AsObject()["data"] = failure.ErrorData;
            }
            else
            {
                // A null result must still be present on the wire: some servers treat a missing
                // "result" key as an error response.
                reply["result"] = result;
            }
            await SendAsync(reply, CancellationToken.None);
        }
        else
        {
            Notification?.Invoke(method, @params);
        }
    }

    public ValueTask DisposeAsync()
    {
        _readCts.Cancel();
        try
        {
            _output.Flush();
        }
        catch
        {
            // The stream is already gone; that is fine.
        }
        _writeLock.Dispose();
        _readCts.Dispose();
        return ValueTask.CompletedTask;
    }
}
