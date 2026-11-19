// Workspace manager: one LS per shadow workspace, keyed by the seat's slug (the mirror is keyed
// by the seat per the design research). Max 3 live LS processes; LRU eviction and 15 min idle
// eviction; 429 with retry_after when full (the HTTP layer translates this to its response).
using System.Collections.Concurrent;
using System.Linq;
using Microsoft.Extensions.Options;
using PiSharp.CodeIntel.Git;

namespace PiSharp.CodeIntel;

public sealed class WorkspaceKey
{
    public required string Seat { get; init; }
    public required string Repo { get; init; }
    public required string BaseSha { get; init; }
}

public sealed class CodeIntelException(string message, int httpStatus) : Exception(message)
{
    public int HttpStatus { get; } = httpStatus;
    public static CodeIntelException Conflict(string message) => new(message, 409);
    public static CodeIntelException NotFound(string message) => new(message, 404);
    public static CodeIntelException TooMany(string message, int retryAfterSeconds)
    {
        var e = new CodeIntelException(message, 429);
        e.RetryAfterSeconds = retryAfterSeconds;
        return e;
    }
    public int RetryAfterSeconds { get; internal set; }
}

public sealed class LiveWorkspace
{
    public required WorkspaceKey Key { get; init; }
    public required string WorkspaceRoot { get; init; }
    public required string Mirror { get; init; }
    public required string Worktree { get; init; }
    public required string RepoUrl { get; init; }
    public required string BaseSha { get; init; }
    public required string WorkspaceUri { get; init; }
    public required Lsp.LsProcess Ls { get; init; }
    public DateTime AttachedAtUtc { get; init; } = DateTime.UtcNow;
    public DateTime LastUsedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>True workspace-lifetime cancellation: cancelled only by DetachAsync / EvictIdle,
    /// never by the HTTP request token. The post-commit warm-up waits on this token so a client
    /// abort after the fast attach cannot kill the committed workspace
    /// (pi-sharp-codeintel-mcp-async-attach-lifecycle).</summary>
    public CancellationTokenSource WorkspaceCts { get; } = new();

    /// <summary>Single-flight LS warm-up. Awaited by the MCP layer (prompt warming answer while
    /// it runs) and detached from the attach request's cancellation: after the workspace is
    /// committed to the slot table, the startup belongs to the workspace lifecycle, not the HTTP
    /// request (pi-sharp-codeintel-mcp-async-attach-lifecycle).</summary>
    public Task LsWarmUp { get; internal set; } = Task.CompletedTask;

    public void WriteFile(string relativePath, string content, CancellationToken ct = default)
    {
        var normalized = relativePath.Replace('\\', '/').TrimStart('/');
        var full = Path.GetFullPath(Path.Combine(Worktree, normalized));
        if (!full.StartsWith(Path.GetFullPath(Worktree), StringComparison.Ordinal))
            throw new ArgumentException($"unsafe path: {relativePath}");
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        System.IO.File.WriteAllText(full, content);
    }
}

public sealed class WorkspaceManager : IAsyncDisposable
{
    private readonly IOptions<CodeIntelOptions> _options;
    private readonly IOptions<CodeIntelServiceOptions> _serviceOptions;
    private readonly ILogger _log;
    private readonly ConcurrentDictionary<string, LiveWorkspace> _live = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeSpan _idleTtl;
    private readonly int _maxLive;
    private long _syncVersions;

    public const int DefaultMaxLive = 3;
    private static readonly TimeSpan DefaultIdleTtl = TimeSpan.FromMinutes(15);

    public WorkspaceManager(IOptions<CodeIntelOptions> options, IOptions<CodeIntelServiceOptions> serviceOptions, ILogger<WorkspaceManager> log)
    {
        _options = options;
        _serviceOptions = serviceOptions;
        _log = log;
        _idleTtl = _serviceOptions.Value.IdleTtl;
        _maxLive = _serviceOptions.Value.MaxLive <= 0 ? DefaultMaxLive : _serviceOptions.Value.MaxLive;
    }

    public int MaxLive => _maxLive;
    public int LiveCount => _live.Count;

    private string SlotKey(WorkspaceKey key) => $"{key.Seat}\t{key.Repo}\t{key.BaseSha}";

    public LiveWorkspace? Find(WorkspaceKey key)
    {
        if (_live.TryGetValue(SlotKey(key), out var ws))
        {
            ws.LastUsedUtc = DateTime.UtcNow;
            return ws;
        }
        return null;
    }

    /// <summary>Attaches (or reuses) the shadow workspace for the seat. Applies the seat's overlay
    /// files and starts the LS when one is not already live for this (seat, repo, base) tuple.
    /// Throws 429 when the slot table is full and the workspace would be a new slot.</summary>
    public async Task<LiveWorkspace> AttachAsync(string seat, string repo, string baseRevision,
        IReadOnlyDictionary<string, string>? overlay, CancellationToken ct)
    {
        var options = _options.Value;
        Directory.CreateDirectory(options.WorkspaceRoot);
        var mirror = MirrorFor(seat, repo);
        EnsureMirror(mirror, repo, ct);

        var shadow = ShadowWorkspace.Create(
            Path.Combine(options.WorkspaceRoot, seat), mirror, repo, baseRevision, overlay, ct);

        var key = new WorkspaceKey { Seat = seat, Repo = repo, BaseSha = shadow.BaseSha };
        var slotKey = SlotKey(key);

        await _gate.WaitAsync(ct);
        try
        {
            EvictIdle();
            if (_live.TryGetValue(slotKey, out var existing))
            {
                existing.LastUsedUtc = DateTime.UtcNow;
                return existing;
            }
            if (_live.Count >= _maxLive)
            {
                var oldest = _live.Values.OrderBy(ws => ws.LastUsedUtc).First();
                var retryAfter = (int)Math.Ceiling((oldest.LastUsedUtc + _idleTtl - DateTime.UtcNow).TotalSeconds);
                throw CodeIntelException.TooMany(
                    $"all {_maxLive} language server slots are in use (oldest: {oldest.Key.Seat}/{oldest.Key.Repo} @ {oldest.BaseSha[..Math.Min(8, oldest.BaseSha.Length)]}); retry in {retryAfter}s",
                    Math.Max(1, retryAfter));
            }

            var ls = await Lsp.LsProcess.StartAsync(new Lsp.LsOptions
            {
                Kind = options.LsKind,
                StartProcess = _serviceOptions.Value.StartProcess ?? (kind => StartLsProcess(kind)),
                WorkspaceUri = shadow.WorkspaceUri,
                ReadyTimeout = options.ReadyTimeout,
            }, ct);

            var live = new LiveWorkspace
            {
                Key = key,
                WorkspaceRoot = shadow.Root,
                Mirror = shadow.Mirror,
                Worktree = shadow.Worktree,
                RepoUrl = shadow.RepoUrl,
                BaseSha = shadow.BaseSha,
                WorkspaceUri = shadow.WorkspaceUri,
                Ls = ls,
            };
            _live[slotKey] = live;
            _log.LogInformation("attached {Seat} -> {Repo} @ {Base} (slot {Slot}/{Max})", seat, repo, shadow.BaseSha[..8], _live.Count, _maxLive);
            return live;
        }
        catch
        {
            // On a failure after StartAsync the worktree was created but the LS is not live;
            // leave the worktree in place: it is idempotent to re-attach on top of it.
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Single-flight warm-up of a committed workspace's LS. The first waiter (the
    /// background attach task) observes the real outcome; every later waiter (a repeat attach,
    /// a retryable query) sees the already-resolved task. The warm-up is owned by the
    /// workspace/session lifecycle (live.WorkspaceCts), NOT the HTTP request token:
    /// after the workspace is committed, a client abort only ends the HTTP wait and does not
    /// kill the committed workspace (pi-sharp-codeintel-mcp-async-attach-lifecycle). A
    /// workspace-detach or faulted start tears down the committed slot so a repeat attach
    /// starts a fresh LS instead of answering from a dead one forever. A late (post-wait)
    /// fault leaves the workspace live: queries report it as faulted.</summary>
    public async Task<bool> RunWarmUpAsync(LiveWorkspace live)
    {
        try
        {
            var ready = await live.Ls.WaitReadyAsync(live.WorkspaceCts.Token);
            if (live.Ls.IsFaulted) throw new Lsp.LspSessionException("language server died during warm-up");
            return ready;
        }
        catch (OperationCanceledException) when (live.WorkspaceCts.IsCancellationRequested)
        {
            // The workspace was detached (session expiry, code_detach, eviction, shutdown):
            // the slot is already removed or being removed; just propagate the cancellation.
            throw;
        }
    }

    /// <summary>True when this workspace's LS died without ever reporting ready. Such a
    /// workspace is terminal: queries report it instead of waiting, and a repeat attach reuses
    /// the slot but starts a fresh LS on top (the warm-up re-runs and reuses mirror/worktree).</summary>
    public bool IsWorkspaceFaulted(LiveWorkspace ws) => ws.Ls.IsFaulted;

    /// <summary>Detaches a faulted slot (LS + worktree) and restarts the LS in its place, for a
    /// repeat attach against a dead workspace. The failed warm-up task has already resolved, so
    /// the single-flight contract holds. Returns the (new or concurrently-reused) live workspace
    /// with its warm-up bound, or null when the old workspace is gone (evicted/detached first).</summary>
    public async Task<LiveWorkspace?> RestartFaultedAsync(LiveWorkspace ws, CancellationToken ct)
    {
        if (!IsWorkspaceFaulted(ws)) return ws;
        await DetachAsync(ws.Key, removeWorktree: true, ct);
        var key = ws.Key;
        var options = _options.Value;
        Directory.CreateDirectory(options.WorkspaceRoot);
        var mirror = MirrorFor(key.Seat, key.Repo);
        var shadow = ShadowWorkspace.Create(
            Path.Combine(options.WorkspaceRoot, key.Seat), mirror, key.Repo, key.BaseSha, null, ct);
        var slotKey = SlotKey(key);
        await _gate.WaitAsync(ct);
        try
        {
            EvictIdle();
            if (_live.TryGetValue(slotKey, out var existing))
                return existing;
            if (_live.Count >= _maxLive)
                throw CodeIntelException.TooMany("all language server slots are in use; retry shortly", 5);
            var ls = await Lsp.LsProcess.StartAsync(new Lsp.LsOptions
            {
                Kind = options.LsKind,
                StartProcess = _serviceOptions.Value.StartProcess ?? (kind => StartLsProcess(kind)),
                WorkspaceUri = shadow.WorkspaceUri,
                ReadyTimeout = options.ReadyTimeout,
            }, ct);
            var live = new LiveWorkspace
            {
                Key = key,
                WorkspaceRoot = shadow.Root,
                Mirror = shadow.Mirror,
                Worktree = shadow.Worktree,
                RepoUrl = shadow.RepoUrl,
                BaseSha = shadow.BaseSha,
                WorkspaceUri = shadow.WorkspaceUri,
                Ls = ls,
            };
            _live[slotKey] = live;
            _log.LogInformation("restarted faulted {Seat} -> {Repo} @ {Base} (slot {Slot}/{Max})", key.Seat, key.Repo, shadow.BaseSha[..8], _live.Count, _maxLive);
            return live;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>code_sync: write the seat's changed files into the worktree and tell the LS via
    /// textDocument/didChange (full-text sync). Returns the number of files synced.</summary>
    public async Task<int> SyncAsync(WorkspaceKey key, IReadOnlyDictionary<string, string> files,
        CancellationToken ct)
    {
        var ws = Find(key) ?? throw CodeIntelException.NotFound($"workspace {key.Seat}/{key.Repo} @ {key.BaseSha[..8]} is not attached");
        var synced = 0;
        foreach (var (relativePath, content) in files)
        {
            var target = Path.Combine(ws.Worktree, relativePath.Replace('\\', '/').TrimStart('/'));
            ws.WriteFile(relativePath, content, ct);
            var uri = ShadowWorkspace.FileUriOf(target);
            var version = Interlocked.Increment(ref _syncVersions);
            var textDocument = new System.Text.Json.Nodes.JsonObject();
            textDocument["uri"] = uri;
            textDocument["version"] = version;
            var change = new System.Text.Json.Nodes.JsonObject();
            change["text"] = content;
            var changes = new System.Text.Json.Nodes.JsonArray();
            changes.Add(change);
            var payload = new System.Text.Json.Nodes.JsonObject();
            payload["textDocument"] = textDocument;
            payload["contentChanges"] = changes;
            await ws.Ls.NotifyAsync("textDocument/didChange", payload, ct);
            synced++;
        }
        return synced;
    }

    /// <summary>Drops the seat's slot: kills the LS and removes the worktree. The mirror survives
    /// (other seats or the same seat may reuse it); an explicit mirror: false keeps it too.</summary>
    public async Task DetachAsync(WorkspaceKey key, bool removeWorktree = true, CancellationToken ct = default)
    {
        var slotKey = SlotKey(key);
        await _gate.WaitAsync(ct);
        try
        {
            if (_live.TryRemove(slotKey, out var ws))
            {
                ws.WorkspaceCts.Cancel();
                await ws.Ls.DisposeAsync();
                if (removeWorktree)
                    ShadowWorkspace.Remove(ws.Mirror, ws.Worktree, ct);
                _log.LogInformation("detached {Seat} -> {Repo} @ {Base}", ws.Key.Seat, ws.Key.Repo, ws.Key.BaseSha[..8]);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private void EvictIdle()
    {
        var now = DateTime.UtcNow;
        foreach (var ws in _live.Values.Where(ws => now - ws.LastUsedUtc > _idleTtl).ToList())
        {
            if (_live.TryRemove(SlotKey(ws.Key), out _))
            {
                ws.WorkspaceCts.Cancel();
                _ = ws.Ls.DisposeAsync().AsTask();
                ShadowWorkspace.Remove(ws.Mirror, ws.Worktree);
                _log.LogInformation("evicted idle {Seat} -> {Repo} @ {Base}", ws.Key.Seat, ws.Key.Repo, ws.Key.BaseSha[..8]);
            }
        }
    }

    /// <summary>All live workspaces for a seat (used by code_sync/code_detach to find the slot).</summary>
    public IEnumerable<LiveWorkspace> EnumerateSeat(string seat)
    {
        return _live.Values.Where(ws => ws.Key.Seat == seat).ToList();
    }

    private string MirrorFor(string seat, string repo)
    {
        var mirrors = Path.Combine(_options.Value.WorkspaceRoot, "mirrors");
        Directory.CreateDirectory(mirrors);
        var name = $"{seat}-{repo}".Replace('/', '-');
        return Path.Combine(mirrors, name);
    }

    private void EnsureMirror(string mirror, string repo, CancellationToken ct)
    {
        if (System.IO.Directory.Exists(mirror) && IsGitRepository(mirror, ct))
        {
            RunGit(mirror, ct, "fetch", "--quiet", "origin");
            return;
        }

        // Clone beside the destination, then replace only an invalid/absent mirror after success.
        // Failed clones never poison the stable path, and a valid mirror (and its worktrees) is
        // never removed or replaced.
        var parent = Path.GetDirectoryName(mirror)!;
        System.IO.Directory.CreateDirectory(parent);
        var staging = mirror + ".clone-" + Guid.NewGuid().ToString("N");
        try
        {
            RunGit(parent, ct, "clone", "--quiet", repo, staging);
            if (System.IO.Directory.Exists(mirror))
                System.IO.Directory.Delete(mirror, recursive: true);
            System.IO.Directory.Move(staging, mirror);
        }
        finally
        {
            if (System.IO.Directory.Exists(staging))
                System.IO.Directory.Delete(staging, recursive: true);
        }
    }

    private static bool IsGitRepository(string directory, CancellationToken ct)
    {
        try
        {
            RunGit(directory, ct, "rev-parse", "--is-inside-work-tree");
            return true;
        }
        catch (Git.GitException)
        {
            return false;
        }
    }

    /// <summary>Default LS launcher: `dotnet tool run csharp-language-server`. Overridden in tests
    /// with an in-process fake so the test suite never needs the real csharp-ls binary.</summary>
    internal static System.Diagnostics.Process StartLsProcess(string kind)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        // csharp-ls is run as `dotnet tool run csharp-language-server` from the seat's tool store;
        // a per-kind override (CODEINTEL_LS_COMMAND) lets the deployment pin an absolute path.
        var command = Environment.GetEnvironmentVariable($"CODEINTEL_LS_{(kind == "roslyn" ? "ROSLYN" : "CSHARPLS")}_COMMAND");
        if (!string.IsNullOrWhiteSpace(command))
        {
            var parts = command.Split(' ', 2);
            psi.FileName = parts[0];
            if (parts.Length > 1) psi.ArgumentList.Add(parts[1]);
        }
        else
        {
            psi.ArgumentList.Add("tool");
            psi.ArgumentList.Add("run");
            psi.ArgumentList.Add(kind == "roslyn" ? "roslyn-language-server" : "csharp-language-server");
        }
        var process = new System.Diagnostics.Process { StartInfo = psi };
        process.Start();
        return process;
    }

    private static void RunGit(string dir, CancellationToken ct, params string[] args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = dir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        var result = PiSharp.Core.Processes.ProcessRunner.RunAsync(psi, TimeSpan.FromMinutes(5), ct).GetAwaiter().GetResult();
        if (result.ExitCode != 0)
            throw new Git.GitException($"git {string.Join(' ', args)} exited {result.ExitCode}", result.Error);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var ws in _live.Values)
        {
            ws.WorkspaceCts.Cancel();
            await ws.Ls.DisposeAsync();
        }
        _live.Clear();
        _gate.Dispose();
    }
}
