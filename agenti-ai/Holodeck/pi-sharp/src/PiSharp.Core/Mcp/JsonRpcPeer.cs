// Hand-written JSON-RPC 2.0 peer. Newline-delimited JSON (MCP stdio transport).
// Spec: https://www.jsonrpc.org/specification and MCP 2025-06-18 stdio.
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PiSharp.Core.Mcp;

/// <summary>JSON-RPC 2.0 error (wire <c>error</c> object).</summary>
public sealed class JsonRpcException : Exception
{
    public JsonRpcException(int code, string message, JsonNode? data = null) : base(message)
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
/// Bidirectional JSON-RPC 2.0 over a pair of streams, one JSON object per line.
/// Messages MUST NOT contain embedded newlines (MCP stdio).
/// </summary>
public sealed class JsonRpcPeer : IAsyncDisposable
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

    /// <summary>Completes when the read loop hits EOF or is disposed.</summary>
    public Task Completion => _readTask;

    public JsonRpcPeer(
        Stream input,
        Stream output,
        Func<string, JsonNode?, CancellationToken, Task<JsonNode?>>? onRequest = null)
    {
        _input = input;
        _output = output;
        _onRequest = onRequest;
        _readTask = Task.Run(() => ReadLoopAsync(_readCts.Token));
    }

    public async Task<JsonNode?> RequestAsync(string method, JsonNode? @params, CancellationToken cancellationToken = default)
    {
        var id = Interlocked.Increment(ref _nextId);
        var idKey = id.ToString();
        var tcs = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[idKey] = tcs;

        using var reg = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));

        var msg = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["method"] = method,
        };
        if (@params is not null) msg["params"] = @params.DeepClone();

        try
        {
            await WriteLineAsync(msg.ToJsonString(), cancellationToken).ConfigureAwait(false);
            return await tcs.Task.ConfigureAwait(false);
        }
        catch
        {
            _pending.TryRemove(idKey, out _);
            throw;
        }
    }

    public Task NotifyAsync(string method, JsonNode? @params, CancellationToken cancellationToken = default)
    {
        var msg = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = method,
        };
        if (@params is not null) msg["params"] = @params.DeepClone();
        return WriteLineAsync(msg.ToJsonString(), cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        try { _readCts.Cancel(); } catch (ObjectDisposedException) { }
        FailPending(new IOException("JSON-RPC peer closed"));
        try { await _readTask.ConfigureAwait(false); } catch { /* cancelled or IO */ }
        _readCts.Dispose();
        _writeLock.Dispose();
    }

    private async Task WriteLineAsync(string json, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(json + "\n");
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _output.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            await _output.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            var lines = new BoundedLineReader(_input);

            while (!cancellationToken.IsCancellationRequested)
            {
                string? line;
                try
                {
                    line = await lines.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (IOException)
                {
                    break;
                }

                if (line is null) break;
                if (line.Length == 0) continue;

                try
                {
                    Dispatch(line, cancellationToken);
                }
                catch
                {
                    // A bad frame must not kill the peer; the other side may still be healthy.
                }
            }
        }
        finally
        {
            FailPending(new IOException("JSON-RPC connection closed"));
        }
    }

    private void Dispatch(string line, CancellationToken cancellationToken)
    {
        JsonObject obj;
        try
        {
            obj = JsonNode.Parse(line) as JsonObject
                ?? throw new JsonRpcException(JsonRpcException.ParseError, "JSON-RPC frame is not an object");
        }
        catch (JsonException ex)
        {
            throw new JsonRpcException(JsonRpcException.ParseError, ex.Message);
        }

        var id = obj["id"];
        var method = obj["method"]?.GetValue<string>();
        var hasError = obj.ContainsKey("error");
        var hasResult = obj.ContainsKey("result");

        if (method is not null && id is not null)
        {
            _ = HandleRequestAsync(method, obj["params"], id, cancellationToken);
            return;
        }

        if (method is not null)
        {
            Notification?.Invoke(method, obj["params"]);
            return;
        }

        if (id is null) return;

        var key = IdKey(id);
        if (!_pending.TryRemove(key, out var tcs)) return;

        if (hasError)
        {
            var err = obj["error"] as JsonObject;
            var code = err?["code"]?.GetValue<int>() ?? JsonRpcException.InternalError;
            var message = err?["message"]?.GetValue<string>() ?? "JSON-RPC error";
            tcs.TrySetException(new JsonRpcException(code, message, err?["data"]));
            return;
        }

        tcs.TrySetResult(hasResult ? obj["result"] : null);
    }

    private async Task HandleRequestAsync(string method, JsonNode? @params, JsonNode id, CancellationToken cancellationToken)
    {
        JsonObject response = new()
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id.DeepClone(),
        };

        try
        {
            if (_onRequest is null)
                throw new JsonRpcException(JsonRpcException.MethodNotFound, $"Method not found: {method}");

            var result = await _onRequest(method, @params, cancellationToken).ConfigureAwait(false);
            response["result"] = result?.DeepClone() ?? JsonNode.Parse("null");
        }
        catch (JsonRpcException ex)
        {
            response["error"] = new JsonObject
            {
                ["code"] = ex.Code,
                ["message"] = ex.Message,
            };
            if (ex.ErrorData is not null) ((JsonObject)response["error"]!)["data"] = ex.ErrorData.DeepClone();
        }
        catch (Exception ex)
        {
            response["error"] = new JsonObject
            {
                ["code"] = JsonRpcException.InternalError,
                ["message"] = ex.Message,
            };
        }

        try
        {
            await WriteLineAsync(response.ToJsonString(), cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Peer is going away.
        }
    }

    private void FailPending(Exception ex)
    {
        foreach (var kv in _pending)
        {
            if (_pending.TryRemove(kv.Key, out var tcs))
                tcs.TrySetException(ex);
        }
    }

    internal static string IdKey(JsonNode id) => id.GetValueKind() switch
    {
        JsonValueKind.String => id.GetValue<string>(),
        JsonValueKind.Number => id.ToJsonString(),
        _ => id.ToJsonString(),
    };
}
