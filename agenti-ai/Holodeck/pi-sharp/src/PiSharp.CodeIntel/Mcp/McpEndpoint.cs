// Hand-written MCP streamable-HTTP endpoint (MCP 2025-06-18), in the pi-sharp style: no MCP SDK,
// newline/JSON-RPC semantics over HTTP POST. Accepts: application/json, text/event-stream.
// A session id is minted on initialize and required afterwards (the client sends Mcp-Session-Id).
//
// Slice-1 tools:
//   code_attach {repo, base_sha?, files?}  shadow-clone at base_sha, start the LS, wait ready
//   code_sync   {files}                    didChange full-text sync of the seat's changed files
//   code_detach {repo?, base_sha?}         drop the slot (kill LS, remove worktree)
// Slice-2 tools (read tools, symbol selectors with +/-2 context lines):
//   code_definition {path,line,character,symbol?}   up to 5 locations
//   code_references {path,line,character,symbol?}   max 30 locations
//   code_hover      {path,line,character}          markdown content
//   code_symbols    {path}                         up to 20 flat symbols
//   code_diagnostics{path?}                        errors only, delta, 20 per file / 5 files
//
// Line/character are 0-based (LSP); results carry 1-based lines for the agent.
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using PiSharp.CodeIntel.Auth;
using PiSharp.CodeIntel.Query;
using PiSharp.Core.Mcp;

namespace PiSharp.CodeIntel.Mcp;

public sealed class McpEndpoint : IAsyncDisposable
{
    private readonly WorkspaceManager _workspaces;
    private readonly Query.CodeIntelligence _query;
    private readonly IOptions<CodeIntelOptions> _options;
    private readonly ILogger _log;
    private readonly ConcurrentDictionary<string, SessionState> _sessions = new();

    public McpEndpoint(WorkspaceManager workspaces, Query.CodeIntelligence query, IOptions<CodeIntelOptions> options, ILogger<McpEndpoint> log)
    {
        _workspaces = workspaces;
        _query = query;
        _options = options;
        _log = log;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private sealed class SessionState
    {
        public required string ProtocolVersion { get; init; }
        public required LiveWorkspace? Live { get; set; }
        public string? WarmUpRepo { get; set; }
        public string? WarmUpBaseSha { get; set; }
    }

    /// <summary>Handles one POST at /mcp. The actor must already be resolved (auth middleware).</summary>
    public async Task<McpResponse> HandlePostAsync(string? sessionId, CodeIntelActor actor, string body, CancellationToken ct)
    {
        // Pre-dispatch rejection: an unknown/expired Mcp-Session-Id on a non-initialize call is
        // HTTP 404 (Streamable HTTP contract; clients recover by re-initializing once). A request
        // with no session id and a method other than initialize is a bare -32600 error, not a 404:
        // 404 means a session we had lost, not a client that never established one.
        if (sessionId is not null && !_sessions.ContainsKey(sessionId) && IsNotInitialize(body))
            return new McpResponse { Status = 404, Body = "", SessionId = null };

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        string? createdSessionId = null;

        // A batch is allowed by JSON-RPC; the MCP spec forbids it, but we answer each entry.
        if (root.ValueKind == JsonValueKind.Array)
        {
            var results = new JsonArray();
            foreach (var element in root.EnumerateArray())
            {
                var one = element.ToString();
                using var single = JsonDocument.Parse(one);
                results.Add(await DispatchAsync(sessionId, actor, single.RootElement, ct, id => createdSessionId = id));
            }
            return new McpResponse { Status = 200, ContentType = "application/json", Body = results.ToJsonString(), SessionId = createdSessionId };
        }

        var response = await DispatchAsync(sessionId, actor, root, ct, id => createdSessionId = id);
        if (response is null)
        {
            // A notification: 202 Accepted with an empty body (MCP 2025-06-18).
            return new McpResponse { Status = 202, Body = string.Empty, SessionId = createdSessionId };
        }
        return new McpResponse { Status = 200, ContentType = "application/json", Body = response.ToJsonString(), SessionId = createdSessionId };
    }

    /// <summary>GET /mcp with a session id: keep-alive ping (200, empty). The SDK client does not
    /// use it; answering 200 instead of 405 keeps a strict client from treating the server as broken.</summary>
    public async Task<McpResponse> HandleGetAsync(string? sessionId, CodeIntelActor actor, CancellationToken ct)
    {
        await Task.Yield();
        if (sessionId is not null && !_sessions.ContainsKey(sessionId))
            return new McpResponse { Status = 404, Body = "{ \"error\": \"unknown session\" }" };
        return new McpResponse { Status = 200, ContentType = "text/event-stream", Body = string.Empty };
    }

    public Task HandleDeleteAsync(string? sessionId, CodeIntelActor actor, CancellationToken ct)
    {
        if (sessionId is not null) _sessions.TryRemove(sessionId, out _);
        return Task.CompletedTask;
    }

    private async Task<JsonNode?> DispatchAsync(string? sessionId, CodeIntelActor actor, JsonElement root, CancellationToken ct,
        Action<string>? onSessionCreated = null)
    {
        if (!root.TryGetProperty("jsonrpc", out _))
            return ErrorNode(null, -32600, "not a JSON-RPC object");

        JsonNode? idNode = null;
        if (root.TryGetProperty("id", out var id) && id.ValueKind != JsonValueKind.Null)
            idNode = JsonNode.Parse(id.GetRawText());
        if (!root.TryGetProperty("method", out var methodProp))
        {
            return idNode is null ? null : ErrorNode(idNode, -32600, "missing method");
        }
        var method = methodProp.GetString() ?? throw new McpMethodNotFoundException("");
        var @params = root.TryGetProperty("params", out var p) ? p.Clone() : default;

        // initialize mints a session; everything else needs one.
        if (method == "initialize")
        {
            var session = Guid.NewGuid().ToString("N");
            string protocolVersion = "2025-06-18";
            _sessions[session] = new SessionState
            {
                ProtocolVersion = protocolVersion,
                Live = null,
            };
            var result = new JsonObject
            {
                ["protocolVersion"] = protocolVersion,
                ["capabilities"] = new JsonObject
                {
                    ["tools"] = new JsonObject(),
                },
                ["serverInfo"] = new JsonObject
                {
                    ["name"] = "pi-sharp-codeintel",
                    ["version"] = "0.1.0",
                },
            };
            onSessionCreated?.Invoke(session);
            return ResponseNode(idNode, result);
        }

        if (method is not ("notifications/initialized") && (sessionId is null || !_sessions.ContainsKey(sessionId)))
        {
            return idNode is null ? null : ErrorNode(idNode, -32600, "unknown or missing session");
        }

        try
        {
            JsonNode? result = method switch
            {
                "notifications/initialized" => null,
                "ping" => new JsonObject(),
                "tools/list" => ToolsList(),
                "tools/call" => await ToolsCallAsync(sessionId, actor, @params, ct),
                _ => throw new McpMethodNotFoundException(method),
            };
            return idNode is null ? null : ResponseNode(idNode, result);
        }
        catch (McpMethodNotFoundException e)
        {
            return idNode is null ? null : ErrorNode(idNode, JsonRpcException.MethodNotFound, e.Message);
        }
        catch (CodeIntelException e)
        {
            return ToolErrorNode(idNode, e);
        }
        catch (McpProtocolException e)
        {
            return idNode is null ? null : ErrorNode(idNode, e.Code, e.Message);
        }
    }

    private static JsonNode? ResponseNode(JsonNode? id, JsonNode? result) => new JsonObject
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id,
        ["result"] = result,
    };

    private static JsonNode? ErrorNode(JsonNode? id, int code, string message) => new JsonObject
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id,
        ["error"] = new JsonObject { ["code"] = code, ["message"] = message },
    };

    private static JsonNode? ToolErrorNode(JsonNode? id, CodeIntelException e) => new JsonObject
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id,
        ["result"] = new JsonObject
        {
            ["content"] = new JsonArray(new JsonObject
            {
                ["type"] = "text",
                ["text"] = e.Message,
            }),
            ["isError"] = true,
        },
    };

    private static JsonNode ToolsList()
    {
        JsonNode Tool(string name, string description, JsonObject input) => new JsonObject
        {
            ["name"] = name,
            ["description"] = description,
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = input,
                ["additionalProperties"] = false,
            },
        };
        var tools = new JsonArray
        {
            Tool("code_attach",
                "Attach a shadow workspace of a repo at a base commit and start its language server. " +
                "Pass your locally-changed files (relative path -> full content) to overlay them on the base.",
                new()
                {
                    ["repo"] = new JsonObject { ["type"] = "string", ["description"] = "Git repo URL or a local mirror path." },
                    ["base_sha"] = new JsonObject { ["type"] = "string", ["description"] = "Commit to attach; defaults to origin/<default branch>." },
                    ["files"] = new JsonObject { ["type"] = "object", ["description"] = "Overlay: relative path -> full file content." },
                }),
            Tool("code_sync",
                "Push the seat's changed files into the attached workspace; each file is sent as a full-text didChange.",
                new()
                {
                    ["files"] = new JsonObject { ["type"] = "object", ["description"] = "relative path -> full file content." },
                }),
            Tool("code_detach",
                "Detach the workspace: stop the language server and remove the shadow worktree.",
                new()
                {
                    ["repo"] = new JsonObject { ["type"] = "string", ["description"] = "Optional: the repo attached; defaults to the only live one." },
                }),
            Tool("code_definition",
                "Jump to the definition of the symbol at (line, character) in path (0-based, like the file). " +
                "Returns up to 5 locations, each with the file, its 1-based line range and +/-2 context lines.",
                new()
                {
                    ["path"] = new JsonObject { ["type"] = "string", ["description"] = "File path relative to the workspace root." },
                    ["line"] = new JsonObject { ["type"] = "integer", ["description"] = "0-based line of the symbol." },
                    ["character"] = new JsonObject { ["type"] = "integer", ["description"] = "0-based character of the symbol." },
                    ["symbol"] = new JsonObject { ["type"] = "string", ["description"] = "Optional: the symbol name, echoed in the result." },
                }),
            Tool("code_references",
                "Find all references of the symbol at (line, character) in path (0-based, like the file). " +
                "Returns up to 30 locations with +/-2 context lines each.",
                new()
                {
                    ["path"] = new JsonObject { ["type"] = "string", ["description"] = "File path relative to the workspace root." },
                    ["line"] = new JsonObject { ["type"] = "integer", ["description"] = "0-based line of the symbol." },
                    ["character"] = new JsonObject { ["type"] = "integer", ["description"] = "0-based character of the symbol." },
                    ["symbol"] = new JsonObject { ["type"] = "string", ["description"] = "Optional: the symbol name, echoed in the result." },
                }),
            Tool("code_hover",
                "Hover information (documentation / type) of the symbol at (line, character) in path (0-based, like the file).",
                new()
                {
                    ["path"] = new JsonObject { ["type"] = "string", ["description"] = "File path relative to the workspace root." },
                    ["line"] = new JsonObject { ["type"] = "integer", ["description"] = "0-based line of the symbol." },
                    ["character"] = new JsonObject { ["type"] = "integer", ["description"] = "0-based character of the symbol." },
                }),
            Tool("code_symbols",
                "List up to 20 document symbols of a file with their 1-based line ranges (a selector to jump to one).",
                new()
                {
                    ["path"] = new JsonObject { ["type"] = "string", ["description"] = "File path relative to the workspace root." },
                }),
            Tool("code_diagnostics",
                "Compiler errors of the open files, delta against the previous call: only newly appeared errors are reported. " +
                "Warnings and hints are never included. At most 20 errors per file, 5 files. " +
                "Pass path to open and check one specific file first.",
                new()
                {
                    ["path"] = new JsonObject { ["type"] = "string", ["description"] = "Optional: file (workspace-relative) to open before checking." },
                }),
        };
        return new JsonObject { ["tools"] = tools };
    }

    private async Task<JsonNode> ToolsCallAsync(string? sessionId, CodeIntelActor actor, JsonElement? @params, CancellationToken ct)
    {
        if (@params is not { } p) throw new McpProtocolException(JsonRpcException.InvalidParams, "params required");
        var name = p.TryGetProperty("name", out var n) ? n.GetString() : throw new McpProtocolException(JsonRpcException.InvalidParams, "params.name required");
        JsonElement? args = p.TryGetProperty("arguments", out var a) && a.ValueKind != JsonValueKind.Null ? a : null;

        var result = name switch
        {
            "code_attach" => await CodeAttachAsync(sessionId, actor, args, ct),
            "code_sync" => await CodeSyncAsync(sessionId, actor, args, ct),
            "code_detach" => await CodeDetachAsync(sessionId, actor, args, ct),
            "code_definition" => await CodeDefinitionAsync(sessionId, actor, args, ct),
            "code_references" => await CodeReferencesAsync(sessionId, actor, args, ct),
            "code_hover" => await CodeHoverAsync(sessionId, actor, args, ct),
            "code_symbols" => await CodeSymbolsAsync(sessionId, actor, args, ct),
            "code_diagnostics" => await CodeDiagnosticsAsync(sessionId, actor, args, ct),
            _ => throw new McpProtocolException(JsonRpcException.InvalidParams, $"unknown tool {name}"),
        };
        return new JsonObject
        {
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = JsonSerializer.Serialize(result, Options) }),
            ["isError"] = false,
        };
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private async Task<JsonNode> CodeAttachAsync(string? sessionId, CodeIntelActor actor, JsonElement? args, CancellationToken ct)
    {
        var repo = Arg(args, "repo")!;
        string? baseSha = null;
        if (args is not null && args.Value.TryGetProperty("base_sha", out var b) && b.ValueKind == JsonValueKind.String)
            baseSha = b.GetString();
        var overlay = ReadFiles(args);

        var baseRevision = baseSha ?? "origin/main"; // unknown base falls back to origin/<branch>; main is the pi-sharp default
        var live = await _workspaces.AttachAsync(actor.Slug, repo, baseRevision, overlay, ct);

        // Async attach contract (pi-sharp-codeintel-mcp-async-attach-lifecycle): the workspace is
        // committed and session-owned; the HTTP request must NOT await LS-ready. Report the
        // current readiness and let the warm-up continue under the session lifecycle. A repeat
        // attach is idempotent: it reuses the same warm-up task, never a second LS. A warm LS
        // resolves the warm-up in milliseconds, so a warm attach still answers with ls_ready=true
        // promptly (<2s). A cancel or LS fault tears the slot down in RunWarmUpAsync so the next
        // attach starts fresh; a faulted repeat attach restarts the LS through the same path.
        if (sessionId is not null && _sessions.TryGetValue(sessionId, out var session))
        {
            session.Live = live;
            session.WarmUpRepo = repo;
            session.WarmUpBaseSha = live.BaseSha;
        }
        if (_workspaces.IsWorkspaceFaulted(live))
            live = (await _workspaces.RestartFaultedAsync(live, ct)) ?? live;
        // The warm-up is owned by the workspace/session lifecycle (live.Ls.Cancellation), not
        // the HTTP request token: a client abort after the fast attach does not kill the
        // committed workspace (pi-sharp-codeintel-mcp-async-attach-lifecycle).
        live.LsWarmUp = _workspaces.RunWarmUpAsync(live);
        if (sessionId is not null && _sessions.TryGetValue(sessionId, out var session2))
            session2.Live = live;

        return new JsonObject
        {
            ["attached"] = true,
            ["seat"] = actor.Slug,
            ["repo"] = repo,
            ["base_sha"] = live.BaseSha,
            ["workspace"] = live.Worktree,
            ["workspace_uri"] = live.WorkspaceUri,
            ["ls_ready"] = live.Ls.IsReady,
            ["ls_kind"] = _options.Value.LsKind,
        };
    }

    private async Task<JsonNode> CodeSyncAsync(string? sessionId, CodeIntelActor actor, JsonElement? args, CancellationToken ct)
    {
        var files = ReadFiles(args, required: true) ?? throw new McpProtocolException(JsonRpcException.InvalidParams, "code_sync needs params.arguments.files");
        var (key, ws) = CurrentLive(sessionId, actor, args);
        var gate = await GateAsync(ws, ct);
        if (gate != null) return gate;
        var synced = await _workspaces.SyncAsync(key, files, ct);
        return new JsonObject
        {
            ["synced"] = synced,
            ["files"] = string.Join(", ", files.Keys),
        };
    }

    private async Task<JsonNode> CodeDetachAsync(string? sessionId, CodeIntelActor actor, JsonElement? args, CancellationToken ct)
    {
        var (key, _) = CurrentLive(sessionId, actor, args);
        await _workspaces.DetachAsync(key, removeWorktree: true, ct);
        _query.Forget(key);
        if (sessionId is not null && _sessions.TryGetValue(sessionId, out var session))
        {
            session.Live = null;
            session.WarmUpRepo = null;
            session.WarmUpBaseSha = null;
        }
        return new JsonObject { ["detached"] = true };
    }

    /// <summary>Query gate for the async attach contract: while the session's LS is warming, a
    /// query returns a prompt, retryable <c>warming</c> result (never a transport failure, and
    /// never a wait that can hit the client's 60s 499). A warming warm-up is bounded by the
    /// request token and WarmingPollBudget, so no query can outlive the existing client call
    /// bound. A faulted workspace answers <c>faulted</c> so the caller re-attaches to restart it
    /// (code_attach restarts a faulted LS through the same single-flight path).</summary>
    private static readonly TimeSpan WarmingPollBudget = TimeSpan.FromSeconds(5);

    private async Task<JsonNode?> GateAsync(LiveWorkspace ws, CancellationToken ct)
    {
        if (ws.Ls.IsReady) return null; // ready: the query proceeds normally
        if (_workspaces.IsWorkspaceFaulted(ws)) return FaultedNode(ws);
        // Wait up to WarmingPollBudget on the single-flight warm-up (not the full ready timeout):
        // a query that arrives while the LS is still up answers promptly with 'warming' and the
        // caller retries, instead of holding the request toward the 60s client deadline.
        try
        {
            await ws.LsWarmUp.WaitAsync(WarmingPollBudget, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (TimeoutException) { } // the poll budget ran out while the LS is still up: answer 'warming' below
        if (ws.Ls.IsReady) return null; // became ready inside the poll budget: proceed
        return _workspaces.IsWorkspaceFaulted(ws) ? FaultedNode(ws) : WarmingNode(ws);
    }

    private static JsonNode WarmingNode(LiveWorkspace ws) => new JsonObject
    {
        ["warming"] = true,
        ["ls_ready"] = false,
        ["message"] = $"language server for {ws.Key.Repo} @ {ws.Key.BaseSha[..8]} is still warming up; this is not a successful result - repeat code_attach to poll ls_ready, then issue the query again",
    };

    private static JsonNode FaultedNode(LiveWorkspace ws) => new JsonObject
    {
        ["warming"] = false,
        ["ls_ready"] = false,
        ["faulted"] = true,
        ["message"] = $"language server for {ws.Key.Repo} @ {ws.Key.BaseSha[..8]} failed to start; this is not a successful result - call code_attach again to restart it, then retry",
    };

    private async Task<JsonNode> CodeDefinitionAsync(string? sessionId, CodeIntelActor actor, JsonElement? args, CancellationToken ct)
    {
        var (_, ws) = CurrentLive(sessionId, actor, args);
        var gate = await GateAsync(ws, ct);
        if (gate != null) return gate;
        return await _query.DefinitionAsync(ws, Arg(args, "path")!, Line(args), Character(args), Arg(args, "symbol", required: false), ct);
    }

    private async Task<JsonNode> CodeReferencesAsync(string? sessionId, CodeIntelActor actor, JsonElement? args, CancellationToken ct)
    {
        var (_, ws) = CurrentLive(sessionId, actor, args);
        var gate = await GateAsync(ws, ct);
        if (gate != null) return gate;
        return await _query.ReferencesAsync(ws, Arg(args, "path")!, Line(args), Character(args), Arg(args, "symbol", required: false), ct);
    }

    private async Task<JsonNode> CodeHoverAsync(string? sessionId, CodeIntelActor actor, JsonElement? args, CancellationToken ct)
    {
        var (_, ws) = CurrentLive(sessionId, actor, args);
        var gate = await GateAsync(ws, ct);
        if (gate != null) return gate;
        return await _query.HoverAsync(ws, Arg(args, "path")!, Line(args), Character(args), ct);
    }

    private async Task<JsonNode> CodeSymbolsAsync(string? sessionId, CodeIntelActor actor, JsonElement? args, CancellationToken ct)
    {
        var (_, ws) = CurrentLive(sessionId, actor, args);
        var gate = await GateAsync(ws, ct);
        if (gate != null) return gate;
        return await _query.SymbolsAsync(ws, Arg(args, "path")!, ct);
    }

    private async Task<JsonNode> CodeDiagnosticsAsync(string? sessionId, CodeIntelActor actor, JsonElement? args, CancellationToken ct)
    {
        var (key, ws) = CurrentLive(sessionId, actor, args);
        var gate = await GateAsync(ws, ct);
        if (gate != null) return gate;
        var path = Arg(args, "path", required: false);
        if (path is not null)
        {
            // csharp-ls scopes diagnostics to the openFiles set; open the requested file on demand
            // so a check of one file does not require the seat to have synced it.
            var uri = Git.ShadowWorkspace.FileUriOf(System.IO.Path.GetFullPath(
                System.IO.Path.Combine(ws.Worktree, path.Replace('\\', '/').TrimStart('/'))));
            await ws.Ls.OpenFileAsync(uri, ct);
        }
        return await _query.DiagnosticsAsync(ws, ct);
    }

    private static int Line(JsonElement? args)
    {
        if (args is not null && args.Value.TryGetProperty("line", out var v) && v.ValueKind == JsonValueKind.Number)
            return v.GetInt32();
        throw new McpProtocolException(JsonRpcException.InvalidParams, "argument line required");
    }

    private static int Character(JsonElement? args)
    {
        if (args is not null && args.Value.TryGetProperty("character", out var v) && v.ValueKind == JsonValueKind.Number)
            return v.GetInt32();
        throw new McpProtocolException(JsonRpcException.InvalidParams, "argument character required");
    }

    private (WorkspaceKey, LiveWorkspace) CurrentLive(string? sessionId, CodeIntelActor actor, JsonElement? args)
    {
        // Prefer the session's live workspace; fall back to the seat's only live one.
        if (sessionId is not null && _sessions.TryGetValue(sessionId, out var s) && s.Live is not null)
            return (s.Live.Key, s.Live);
        var repo = Arg(args, "repo", required: false);
        foreach (var live in _workspaces.EnumerateSeat(actor.Slug))
        {
            if (repo is null || live.Key.Repo == repo)
                return (live.Key, live);
        }
        throw CodeIntelException.NotFound($"no workspace attached for {actor.Slug}" + (repo is null ? "" : $" of {repo}"));
    }

    private static string? Arg(JsonElement? args, string name, bool required = true)
    {
        if (args is not null && args.Value.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String)
            return v.GetString();
        if (required)
            throw new McpProtocolException(JsonRpcException.InvalidParams, $"argument {name} required");
        return null;
    }

    /// <summary>True when the POST body is not an <c>initialize</c> request (single or batched).
    /// Used to reject unknown sessions before parsing; a body that fails to parse is not a 404 case.</summary>
    private static bool IsNotInitialize(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Array)
                return root.EnumerateArray().All(IsNotInitializeElement);
            return IsNotInitializeElement(root);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsNotInitializeElement(JsonElement element)
    {
        if (!element.ValueKind.Equals(JsonValueKind.Object)) return false;
        return element.TryGetProperty("method", out var m) &&
               m.ValueKind == JsonValueKind.String &&
               m.GetString() != "initialize";
    }

    /// <summary>Reads an object argument of {relative path: content} into a dictionary.</summary>
    private static Dictionary<string, string>? ReadFiles(JsonElement? args, bool required = false)
    {
        if (args is not { } p || !p.TryGetProperty("files", out var f) || f.ValueKind != JsonValueKind.Object)
        {
            if (required) throw new McpProtocolException(JsonRpcException.InvalidParams, "files must be an object of relative path -> content");
            return null;
        }
        var dict = new Dictionary<string, string>();
        foreach (var prop in f.EnumerateObject())
            dict[prop.Name] = prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString()! : prop.Value.ToString();
        return dict;
    }
}

public sealed class McpResponse
{
    public int Status { get; init; }
    public string? ContentType { get; init; }
    public string Body { get; init; } = string.Empty;
    public string? SessionId { get; init; }

    public void ApplyHeaders(HttpResponse response)
    {
        if (!string.IsNullOrWhiteSpace(SessionId))
            response.Headers["Mcp-Session-Id"] = SessionId;
    }
}

internal sealed class McpProtocolException : Exception
{
    public McpProtocolException(int code, string message) : base(message) => Code = code;
    public int Code { get; }
}

internal sealed class McpMethodNotFoundException : Exception
{
    public McpMethodNotFoundException(string method) : base($"method not found: {method}") { }
}
