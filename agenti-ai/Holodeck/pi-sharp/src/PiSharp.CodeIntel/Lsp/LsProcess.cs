// Language server process wrapper. One LS per workspace, csharp-ls by default.
//
// Client obligations (claude-code-plugins#3536): csharp-ls 0.27 stays empty when the client
// answers -32601 to the three server requests, so we answer them all with a JSON null result:
//   workspace/configuration  -> null            (accept server defaults; no workspace settings)
//   client/registerCapability -> null            (the server registers, no client capabilities)
//   window/workDoneProgress/create -> null       (we never call window/workDoneProgress/report)
// Analyzers are off and diagnostics are scoped to openFiles (initializationOptions).
// Ready means: the first $/progress notification whose value kind is "end", or a 90s timeout.
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PiSharp.CodeIntel.Lsp;

public sealed class LsOptions
{
    public required string Kind { get; init; }

    /// <summary>Factory that starts the LS process for a kind. Injected in tests with an in-process fake.</summary>
    public required Func<string, Process> StartProcess { get; init; }

    public string? WorkspaceUri { get; init; }

    public TimeSpan ReadyTimeout { get; init; } = TimeSpan.FromSeconds(90);
}

/// <summary>A live csharp-ls (or roslyn LS) process bound to one workspace. Not thread-safe for
/// concurrent requests: the LSP is request/reply with optional concurrent diagnostics.</summary>
public sealed class LsProcess : IAsyncDisposable
{
    private readonly LspPeer _peer;
    private readonly Process _process;
    private readonly TimeSpan _readyTimeout;
    private readonly Task _stderrDrain;
    private readonly StringBuilder _stderr = new();
    private readonly SemaphoreSlim _requestLock = new(1, 1);
    private TaskCompletionSource<bool>? _readyTcs;
    private long _syncVersion;
    private readonly object _docLock = new();
    private readonly HashSet<string> _openDocuments = new();
    private readonly Dictionary<string, JsonNode> _publishedDiagnostics = new();

    public string WorkspaceUri { get; }

    /// <summary>The LSP peer behind this process (used by the read tools for capability probing).</summary>
    public LspPeer Peer => _peer;

    /// <summary>URIs of the documents this client has sent (didOpen / didChange / sync).</summary>
    public IReadOnlyCollection<string> OpenDocumentUris()
    {
        lock (_docLock) return _openDocuments.ToList();
    }

    private void TrackDocument(string uri)
    {
        lock (_docLock) _openDocuments.Add(uri);
    }

    /// <summary>The last publishDiagnostics payload for a document, or null. The pull model
    /// (textDocument/diagnostic) is preferred; this keeps a fallback for servers that only push.</summary>
    public JsonNode? LastPublishedDiagnostics(string uri)
    {
        lock (_docLock) return _publishedDiagnostics.TryGetValue(uri, out var node) ? node : null;
    }

    /// <summary>Sends textDocument/didOpen for a file that exists in the worktree but was not
    /// synced by the seat (the worktree clone is the source of truth for untouched files).
    /// csharp-ls scopes diagnostics to openFiles, so the read tools open files on demand.</summary>
    public async Task OpenFileAsync(string fileUri, CancellationToken ct)
    {
        TrackDocument(fileUri);
        var path = System.IO.Path.GetFullPath(new Uri(fileUri).AbsolutePath);
        string text;
        try
        {
            text = System.IO.File.ReadAllText(path);
        }
        catch (Exception e) when (e is System.IO.IOException or FileNotFoundException)
        {
            throw new LspSessionException($"cannot open {fileUri} for the language server: {e.Message}");
        }
        await _peer.NotifyAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = fileUri, ["version"] = 1, ["languageId"] = "csharp", ["text"] = text },
        }, ct);
    }

    /// <summary>True once the server reported its $/progress "end" (or we gave up and accepted it as ready anyway).</summary>
    public bool IsReady { get; private set; }

    /// <summary>True once the LS process exited without ever reporting ready. Queries should not
    /// wait for such a server; the workspace is terminal and its cleanup is owned by the manager.</summary>
    public bool IsFaulted => _process.HasExited && !IsReady;

    public string StandardError
    {
        get { lock (_stderr) return _stderr.ToString(); }
    }

    public LsProcess(Process process, LspPeer peer, string workspaceUri, CancellationToken ct)
        : this(process, peer, workspaceUri, TimeSpan.FromSeconds(90), ct)
    {
    }

    /// <summary>The request-scoped token used for warm-up: the request's cancellation is linked
    /// in only for the duration of the warm-up wait, so a caller that cancels (or times out its
    /// HTTP call) never cancels the background warm-up of a committed workspace.</summary>
    public CancellationToken Cancellation { get; }

    internal LsProcess(Process process, LspPeer peer, string workspaceUri, TimeSpan readyTimeout, CancellationToken ct)
    {
        _process = process;
        _peer = peer;
        Cancellation = ct;
        _readyTimeout = readyTimeout;
        WorkspaceUri = workspaceUri;
        _readyTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        process.Exited += (_, _) => OnExit(process.ExitCode);
        _stderrDrain = Task.Run(() => DrainStderrAsync(ct));
        _peer.Notification += OnNotification;
        _peer.Request += OnServerRequest;
    }

    private async Task DrainStderrAsync(CancellationToken ct)
    {
        try
        {
            string? line;
            while ((line = await _process.StandardError.ReadLineAsync(ct)) is not null)
            {
                lock (_stderr)
                {
                    _stderr.AppendLine(line);
                    if (_stderr.Length > 64 * 1024) break; // never grow unbounded
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
    }

    /// <summary>Starts the process, sends <c>initialize</c>, then <c>initialized</c>. Ready means the
    /// first $/progress "end", or the ReadyTimeout elapsing. Returns as soon as the <c>initialize</c>
    /// reply arrives; callers should await <see cref="WaitReadyAsync"/> to gate on the ready signal.
    /// A server that never reports is still returned (best effort): navigation works even while the
    /// server finishes warming up, and the ready flag is what the tools report.</summary>
    public static async Task<LsProcess> StartAsync(LsOptions options, CancellationToken ct)
    {
        var process = options.StartProcess(options.Kind);
        var peer = new LspPeer(
            process.StandardOutput.BaseStream,  // we read the LS's responses from stdout
            process.StandardInput.BaseStream,   // we write our requests to stdin
            onRequest: null);
        var ls = new LsProcess(process, peer, options.WorkspaceUri ?? "file:///", options.ReadyTimeout, ct);

        var clientInfo = new JsonObject
        {
            ["name"] = "pi-sharp-codeintel",
            ["version"] = "0.1.0",
        };
        var capabilities = new JsonObject(); // an honest empty capability set; the three server requests get a null answer
        var init = new JsonObject
        {
            ["processId"] = Environment.ProcessId,
            ["rootUri"] = options.WorkspaceUri,
            ["capabilities"] = capabilities,
            ["clientInfo"] = clientInfo,
        };
        // csharp-ls specific: analyzers off, diagnostics only for open files.
        init["initializationOptions"] = new JsonObject
        {
            ["analyzerDiagnosticsScope"] = "openFiles",
            ["analyzerEnabled"] = false,
            ["enableHotReload"] = false,
        };

        try
        {
            var result = await peer.RequestAsync("initialize", init, ct);
            await peer.NotifyAsync("initialized", null, ct);
            return ls;
        }
        catch
        {
            await ls.DisposeAsync();
            throw;
        }
    }

    /// <summary>Waits for the first $/progress "end" notification or the timeout elapsing. The LS is
    /// treated as ready either way: a cold start that never reports is still better than no service.
    /// Returns true when a real "end" was observed, false when the timeout fired first.</summary>
    public async Task<bool> WaitReadyAsync(CancellationToken timeoutToken)
    {
        if (IsReady) return true;
        try
        {
            // The request's cancellation token must not leak into the background warm-up:
            // a cancelled caller only ends its own wait (LsWarmUp uses a linked token and
            // detaches), never the warm-up task itself.
            return await _readyTcs!.Task.WaitAsync(_readyTimeout, timeoutToken);
        }
        catch (TimeoutException)
        {
            // Timed out: accept the server as best-effort ready so navigation can still run.
            if (!IsReady) MarkReady();
            return false;
        }
        catch (LspSessionException e)
        {
            // The LS died before reporting ready: surface the fault instead of pretending ready.
            throw new LspSessionException($"language server not ready: {e.Message}", e);
        }
    }

    public async Task<JsonNode?> RequestAsync(string method, JsonNode? @params, CancellationToken cancellationToken)
    {
        // The LSP tolerates a single in-flight request per client reliably (csharp-ls serializes
        // some work); serialize here so the caller's code is simple.
        await _requestLock.WaitAsync(cancellationToken);
        try
        {
            if (_process.HasExited)
                throw new LspSessionException($"language server for {WorkspaceUri} exited: {StandardError}");
            return await _peer.RequestAsync(method, @params, cancellationToken);
        }
        finally
        {
            _requestLock.Release();
        }
    }

    public Task NotifyAsync(string method, JsonNode? @params, CancellationToken cancellationToken)
    {
        // Track the client-side open set so the diagnostics pull knows which documents are
        // part of the openFiles scope (the service is the LSP client here).
        if (method == "textDocument/didOpen")
        {
            var uri = @params?["textDocument"]?["uri"]?.GetValue<string>();
            if (uri is not null) TrackDocument(uri);
        }
        return _peer.NotifyAsync(method, @params, cancellationToken);
    }

    /// <summary>Sends textDocument/didChange for one file with a full-text sync kind (2) and the
    /// next sync kind version, so the LS re-parses instead of diffing. A monotonic per-workspace
    /// version counter lives here so the LSP invariant (never decreasing) holds per textDocument.</summary>
    public Task SyncFileAsync(string fileUri, string text, CancellationToken cancellationToken)
    {
        var version = (int)Interlocked.Increment(ref _syncVersion);
        TrackDocument(fileUri);
        return _peer.NotifyAsync("textDocument/didChange", new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = fileUri, ["version"] = version },
            ["contentChanges"] = new JsonArray(new JsonObject { ["text"] = text }),
        }, cancellationToken);
    }

    private void OnNotification(string method, JsonNode? @params)
    {
        if (method == "textDocument/publishDiagnostics")
        {
            // Push-model fallback: keep the last full payload per document (the read tools
            // prefer the pull model and only use this when the server lacks it).
            var uri = @params?["uri"]?.GetValue<string>();
            if (uri is not null && @params is JsonObject diagObj)
            {
                lock (_docLock)
                    _publishedDiagnostics[uri] = diagObj["diagnostics"]?.DeepClone() ?? new JsonArray();
            }
            return;
        }
        if (method != "$/progress") return;
        if (@params is not JsonObject prog) return;
        var value = prog["value"];
        // A $/progress value with kind "end" is the ready signal per the design research.
        if (value is JsonObject v && v["kind"]?.GetValue<string>() == "end")
            MarkReady();
    }

    private void MarkReady()
    {
        if (IsReady) return;
        IsReady = true;
        _readyTcs?.TrySetResult(true);
    }

    /// <summary>Answers the three LSP server requests with a JSON null result. The csharp-ls
    /// client obligation from claude-code-plugins#3536: answering -32601 leaves it empty.</summary>
    private Task<JsonNode?> OnServerRequest(string method, JsonNode? @params, CancellationToken ct)
    {
        // The three LSP server requests all get a JSON null result (csharp-ls 0.27 stays empty
        // when the client answers -32601): workspace/configuration, client/registerCapability,
        // window/workDoneProgress/create. Unknown requests also get null, never an error.
        return Task.FromResult<JsonNode?>(null);
    }

    private void OnExit(int code)
    {
        // A dead LS must not report as ready: a cold start that dies before its first $/progress
        // "end" is a fault, not readiness (it must free the slot and not answer warm).
        if (IsReady) return;
        _readyTcs?.TrySetException(new LspSessionException($"language server exited before ready (exit code {code})"));
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _peer.DisposeAsync();
        }
        catch
        {
            // shutting down a dead process: fine
        }
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
        catch
        {
            // best effort
        }
        finally
        {
            _process.Dispose();
            _requestLock.Dispose();
        }
    }
}

/// <summary>Raised by <see cref="LsProcess"/> when a request cannot be made (process gone, broken pipe).</summary>
public sealed class LspSessionException : Exception
{
    public LspSessionException(string message) : base(message) { }
    public LspSessionException(string message, Exception inner) : base(message, inner) { }
}
