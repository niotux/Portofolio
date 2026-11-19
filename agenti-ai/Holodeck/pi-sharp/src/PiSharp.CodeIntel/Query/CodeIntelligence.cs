// Read tools of the code-intel service (slice 2): code_definition, code_references, code_hover,
// code_symbols, code_diagnostics. Each is a thin mapping from LSP to the service's result
// shape, with the item's hard limits applied here so the MCP endpoint stays trivial.
//
//   code_definition    up to 5 locations, each with +/-2 context lines
//   code_references    max 30 locations, same format
//   code_hover         markdown content (first contents entry) + the position it was asked for
//   code_symbols       up to 20 flat document symbols with a line-range selector
//   code_diagnostics   errors only (severity 1), delta against the last call: only newly
//                      appeared entries are reported. At most 20 per file, 5 files.
//
// A location is reported as a selector: 1-based file + start_line/end_line plus up to two
// context lines before and after the symbol's range, read from the worktree file.
using System.Collections.Concurrent;
using System.Text.Json.Nodes;

namespace PiSharp.CodeIntel.Query;

public sealed class CodeIntelligence
{
    public const int MaxDefinitionLocations = 5;
    public const int MaxReferences = 30;
    public const int MaxSymbols = 20;
    public const int MaxDiagnosticsPerFile = 20;
    public const int MaxDiagnosticFiles = 5;
    public const int ContextLines = 2;

    private readonly WorkspaceManager _workspaces;
    private readonly ConcurrentDictionary<string, HashSet<string>> _diagnosticsSnapshot = new();

    public CodeIntelligence(WorkspaceManager workspaces)
    {
        _workspaces = workspaces;
    }

    private static string SlotKey(WorkspaceKey key) => $"{key.Seat}\t{key.Repo}\t{key.BaseSha}";

    /// <summary>code_definition: LSP textDocument/definition -> up to 5 locations with context.</summary>
    public async Task<JsonNode> DefinitionAsync(LiveWorkspace ws, string path, int line, int character, string? symbol, CancellationToken ct)
    {
        var uri = UriOf(ws, path);
        var result = await NavigationAsync(ws.Ls, "textDocument/definition", new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = uri },
            ["position"] = new JsonObject { ["line"] = line, ["character"] = character },
        }, ct);

        var locations = DecodeLocations(result);
        bool truncated = locations.Count > MaxDefinitionLocations;
        if (truncated) locations = locations.Take(MaxDefinitionLocations).ToList();
        return new JsonObject
        {
            ["count"] = locations.Count,
            ["limit"] = MaxDefinitionLocations,
            ["truncated"] = truncated,
            ["symbol"] = symbol,
            ["locations"] = BuildSelectors(ws, locations, symbol),
        };
    }

    /// <summary>code_references: LSP textDocument/references -> max 30 locations with context.
    /// includeDeclaration=true: the item wants every reference, the position itself included.</summary>
    public async Task<JsonNode> ReferencesAsync(LiveWorkspace ws, string path, int line, int character, string? symbol, CancellationToken ct)
    {
        var uri = UriOf(ws, path);
        var result = await NavigationAsync(ws.Ls, "textDocument/references", new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = uri },
            ["position"] = new JsonObject { ["line"] = line, ["character"] = character },
            ["context"] = new JsonObject { ["includeDeclaration"] = true },
        }, ct);

        var locations = DecodeLocations(result);
        bool truncated = locations.Count > MaxReferences;
        if (truncated) locations = locations.Take(MaxReferences).ToList();
        return new JsonObject
        {
            ["count"] = locations.Count,
            ["limit"] = MaxReferences,
            ["truncated"] = truncated,
            ["symbol"] = symbol,
            ["locations"] = BuildSelectors(ws, locations, symbol),
        };
    }

    /// <summary>Builds the selector array for a location list in a workspace (public so tests
    /// can exercise it without a live LS).</summary>
    public JsonArray BuildSelectors(LiveWorkspace ws, List<(string uri, int startLine, int endLine)> locations, string? symbol)
    {
        var result = new JsonArray();
        var fileCache = new Dictionary<string, string[]?>();
        foreach (var (uri, startLine, endLine) in locations)
        {
            var filePath = UriToWorktreePath(ws, uri);
            string[]? lines = null;
            if (filePath is not null)
            {
                if (!fileCache.TryGetValue(filePath, out lines))
                {
                    try
                    {
                        lines = System.IO.File.Exists(System.IO.Path.Combine(ws.Worktree, filePath))
                            ? System.IO.File.ReadAllLines(System.IO.Path.Combine(ws.Worktree, filePath))
                            : Array.Empty<string>();
                    }
                    catch
                    {
                        lines = Array.Empty<string>();
                    }
                    fileCache[filePath] = lines;
                }
            }

            var ctxLines = new JsonArray();
            if (lines is not null && lines.Length > 0)
            {
                var from = Math.Max(0, startLine - ContextLines);
                var to = Math.Min(lines.Length - 1, endLine + ContextLines);
                for (var i = from; i <= to; i++)
                {
                    ctxLines.Add(new JsonObject
                    {
                        ["line"] = i + 1, // 1-based, what an agent reads
                        ["text"] = lines[i],
                        ["in_symbol"] = i >= startLine && i <= endLine,
                    });
                }
            }

            result.Add(new JsonObject
            {
                ["file"] = filePath ?? AbsolutePath(uri),
                ["start_line"] = startLine + 1,
                ["end_line"] = endLine + 1,
                ["symbol"] = symbol,
                ["context"] = ctxLines,
            });
        }
        return result;
    }

    /// <summary>code_hover: LSP textDocument/hover -> the first markdown contents entry.</summary>
    public async Task<JsonNode> HoverAsync(LiveWorkspace ws, string path, int line, int character, CancellationToken ct)
    {
        var uri = UriOf(ws, path);
        var result = await NavigationAsync(ws.Ls, "textDocument/hover", new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = uri },
            ["position"] = new JsonObject { ["line"] = line, ["character"] = character },
        }, ct);

        var contents = result?["contents"] is JsonArray a ? a : null;
        var text = contents is not null ? FlattenContents(contents) : null;
        return new JsonObject
        {
            ["path"] = path,
            ["line"] = line,
            ["character"] = character,
            ["content"] = text,
            ["empty"] = text is null,
        };
    }

    /// <summary>code_symbols: LSP textDocument/documentSymbol -> up to 20 flat entries.</summary>
    public async Task<JsonNode> SymbolsAsync(LiveWorkspace ws, string path, CancellationToken ct)
    {
        var uri = UriOf(ws, path);
        var result = await NavigationAsync(ws.Ls, "textDocument/documentSymbol", new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = uri },
        }, ct);

        var entries = new JsonArray();
        JsonNode? list = result;
        if (result is JsonObject top && top["symbols"] is JsonArray wrapped)
            list = wrapped;

        void Add(JsonNode? symbol, string container)
        {
            if (symbol is not JsonObject s) return;
            int start = (int)(s["range"]?["start"]?["line"]?.GetValue<int>() ?? 0);
            int end = (int)(s["range"]?["end"]?["line"]?.GetValue<int>() ?? start);
            entries.Add(new JsonObject
            {
                ["name"] = s["name"]?.GetValue<string>() ?? "?",
                ["kind"] = s["kind"]?.GetValue<int>() ?? 0,
                ["container"] = container,
                ["path"] = path,
                ["start_line"] = start + 1,
                ["end_line"] = end + 1,
            });
            if (entries.Count >= MaxSymbols) return;
            if (s["children"] is JsonArray children)
            {
                foreach (var child in children)
                {
                    if (entries.Count >= MaxSymbols) break;
                    Add(child, s["name"]?.GetValue<string>() ?? container);
                }
            }
        }

        if (list is JsonArray array)
        {
            foreach (var item in array)
            {
                if (entries.Count >= MaxSymbols) break;
                Add(item, "");
            }
        }

        bool truncated = entries.Count >= MaxSymbols;
        if (truncated)
        {
            var trimmed = new JsonArray();
            for (var i = 0; i < MaxSymbols && i < entries.Count; i++)
                trimmed.Add(((JsonNode)entries[i]!).DeepClone());
            entries = trimmed;
        }
        return new JsonObject
        {
            ["count"] = entries.Count,
            ["limit"] = MaxSymbols,
            ["truncated"] = truncated,
            ["path"] = path,
            ["symbols"] = entries,
        };
    }

    /// <summary>code_diagnostics: pull model (textDocument/diagnostic) for every open document,
    /// errors only, delta against the last call, at most 20 per file and 5 files. Falls back to
    /// the last publishDiagnostics push when the server lacks the pull model.</summary>
    public async Task<JsonNode> DiagnosticsAsync(LiveWorkspace ws, CancellationToken ct)
    {
        var slotKey = SlotKey(ws.Key);
        var files = new List<(string uri, JsonArray errors)>();

        foreach (var uri in ws.Ls.OpenDocumentUris())
        {
            if (files.Count >= MaxDiagnosticFiles) break;

            JsonNode? items = null;
            try
            {
                var resp = await ws.Ls.RequestAsync("textDocument/diagnostic", new JsonObject
                {
                    ["textDocument"] = new JsonObject { ["uri"] = uri },
                }, ct);
                items = resp?["items"];
            }
            catch (Lsp.LspRpcException e) when (e.Code == Lsp.LspRpcException.MethodNotFound)
            {
                items = ws.Ls.LastPublishedDiagnostics(uri);
            }

            if (items is not JsonArray array) continue;
            var errors = new JsonArray();
            foreach (var item in array)
            {
                if (item is not JsonObject diag) continue;
                // severity 1 = Error; 2 Warning, 3 Information, 4 Hint are dropped.
                if (diag["severity"]?.GetValue<int>() is not 1) continue;
                var range = diag["range"];
                var code = diag["code"]?.ToJsonString();
                var message = diag["message"]?.GetValue<string>() ?? "";
                // Stable key: location + code + message. (A code-less error still keys on location+message.)
                var key = $"{range?.ToJsonString()}\u0000{code}\u0000{message}";
                errors.Add(new JsonObject
                {
                    ["file"] = AbsolutePath(uri),
                    ["start_line"] = (int)((range?["start"]?["line"]?.GetValue<int>() ?? 0)) + 1,
                    ["end_line"] = (int)((range?["end"]?["line"]?.GetValue<int>() ?? 0)) + 1,
                    ["code"] = code == "null" ? null : code,
                    ["severity"] = "error",
                    ["message"] = message,
                    ["_key"] = key,
                });
            }
            if (errors.Count == 0) continue;
            files.Add((uri, errors));
        }

        var previous = _diagnosticsSnapshot.GetOrAdd(slotKey, _ => new HashSet<string>());
        var current = new HashSet<string>();
        var result = new JsonArray();
        foreach (var (uri, errors) in files)
        {
            var kept = new JsonArray();
            for (var i = 0; i < errors.Count && i < MaxDiagnosticsPerFile; i++)
            {
                var key = errors[i]?["_key"]?.GetValue<string>();
                if (key is null) continue;
                current.Add(key); // an unchanged error stays in the snapshot so it is not re-reported
                if (previous.Contains(key)) continue;
                var copy = (JsonObject)errors[i]!.DeepClone();
                copy.Remove("_key");
                kept.Add(copy);
            }
            if (kept.Count == 0) continue;
            result.Add(new JsonObject
            {
                ["file"] = AbsolutePath(uri),
                ["errors"] = kept,
                ["truncated"] = errors.Count > MaxDiagnosticsPerFile,
            });
        }
        previous.Clear();
        foreach (var key in current) previous.Add(key);

        var totalDelta = 0;
        for (var i = 0; i < result.Count; i++)
        {
            var file = result[i] as JsonObject;
            var errors = file?["errors"];
            if (errors is JsonArray errorsArray) totalDelta += errorsArray.Count;
        }
        return new JsonObject
        {
            ["delta"] = totalDelta,
            ["files"] = result,
            ["files_limit"] = MaxDiagnosticFiles,
            ["per_file_limit"] = MaxDiagnosticsPerFile,
        };
    }

    /// <summary>Clears the diagnostics delta snapshot for a slot (code_detach / eviction).</summary>
    public void Forget(WorkspaceKey key)
    {
        _diagnosticsSnapshot.TryRemove(SlotKey(key), out _);
    }

    // ── internals ────────────────────────────────────────────────────────────

    private static async Task<JsonNode?> NavigationAsync(Lsp.LsProcess ls, string method, JsonNode? @params, CancellationToken ct)
    {
        try
        {
            return await ls.RequestAsync(method, @params, ct);
        }
        catch (Lsp.LspRpcException e) when (e.Code == Lsp.LspRpcException.MethodNotFound)
        {
            return null; // the server lacks the capability: an empty result, not an error
        }
    }

    /// <summary>LSP location: a Location {uri,range}, a LocationLink, or an array of either.</summary>
    public static List<(string uri, int startLine, int endLine)> DecodeLocations(JsonNode? result)
    {
        var list = new List<(string uri, int startLine, int endLine)>();
        void AddOne(JsonNode? node)
        {
            if (node is not JsonObject loc) return;
            var uri = loc["uri"]?.GetValue<string>() ?? loc["targetUri"]?.GetValue<string>() ?? "";
            var range = loc["range"] ?? loc["targetSelectionRange"];
            int start = (int)(range?["start"]?["line"]?.GetValue<int>() ?? 0);
            int end = (int)(range?["end"]?["line"]?.GetValue<int>() ?? start);
            if (uri.Length > 0) list.Add((uri, start, end));
        }
        if (result is JsonArray array)
        {
            foreach (var item in array)
            {
                if (item is JsonArray nested)
                    foreach (var n in nested) AddOne(n);
                else
                    AddOne(item);
            }
        }
        else
        {
            AddOne(result);
        }
        return list;
    }

    public static string AbsolutePath(string uri)
    {
        try
        {
            return Uri.UnescapeDataString(new Uri(uri).AbsolutePath);
        }
        catch
        {
            return uri;
        }
    }

    private static string UriOf(LiveWorkspace ws, string path)
    {
        var normalized = path.Replace('\\', '/').TrimStart('/');
        var full = System.IO.Path.GetFullPath(System.IO.Path.Combine(ws.Worktree, normalized));
        return Git.ShadowWorkspace.FileUriOf(full);
    }

    private static string? UriToWorktreePath(LiveWorkspace ws, string uri)
    {
        var absolute = AbsolutePath(uri);
        try
        {
            var full = System.IO.Path.GetFullPath(absolute);
            if (!full.StartsWith(System.IO.Path.GetFullPath(ws.Worktree), System.StringComparison.Ordinal))
                return null;
            return System.IO.Path.GetRelativePath(ws.Worktree, full).Replace('\\', '/');
        }
        catch
        {
            return null;
        }
    }

    private static string? FlattenContents(JsonArray contents)
    {
        // Hover contents: a single MarkedString (string) or an array of MarkupContent/MarkedString.
        var first = contents.FirstOrDefault();
        if (first is null) return null;
        if (first is JsonValue v && v.TryGetValue<string>(out var s)) return s;
        if (first is JsonObject obj && obj["value"] is JsonValue vv && vv.TryGetValue<string>(out var sv)) return sv;
        return first.ToJsonString();
    }
}
