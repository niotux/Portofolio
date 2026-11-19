// codeintel epic slice 3: after a turn that edited files, sync the changed files to the
// self-hosted code-intel service and read the diagnostics delta. One Trailing note (1.5k chars or
// less) is appended to the next turn when the delta is non-empty; otherwise the conversation is
// untouched. Every failure is swallowed (fail open): a diagnostics note must never fail the run.
using System.Diagnostics;
using System.Text.Json;
using System.Text;
using System.Text.Json.Nodes;
using PiSharp.Core.Agent;
using PiSharp.Core.Config;
using PiSharp.Core.Mcp;
using PiSharp.Core.Messages;

namespace PiSharp.Core.CodeIntelFeedback;

/// <summary>One line of the diagnostics delta as reported by the code-intel <c>code_diagnostics</c> tool.</summary>
public sealed record CodeIntelDiag(string File, int? StartLine, string? Code, string Severity, string Message);

/// <summary>
/// The pi-sharp half of the codeintel epic: collect the paths touched by a turn (edit/write/
/// apply_patch results plus <c>git status --porcelain</c>), push them to the code-intel service with
/// <c>code_sync</c>, and ask for the <c>code_diagnostics</c> delta. Composed into
/// <see cref="AgentLoopConfig.PrepareNextTurn"/> like the verifier feedback; only a non-empty delta adds
/// a note, so unchanged errors are never re-announced. The diagnostics call is capped at 10 seconds
/// (the service's own diagnostic budget); the per-MCP-call timeout applies to everything else.
/// </summary>
public sealed class CodeIntelFeedback
{
    private const int DiagnosticsCapSeconds = 10;
    private const int NoteCap = 1500;
    private const int MaxFilesPerSync = 40;
    private const int MaxErrorLines = 12;
    private const string NotePrefix = "Code-intel diagnostics";

    private static readonly HashSet<string> MutatingToolNames =
        ["edit", "write", "apply_patch", "bash", "job"];

    private readonly McpClient _client;
    private readonly TimeSpan _diagnosticsDeadline;
    private readonly TimeSpan _callDeadline;
    private readonly string _cwd;
    private readonly CodeIntelSettings _settings;
    private readonly Func<TurnContext, ValueTask<TurnUpdate?>>? _inner;
    private readonly object _gate = new();
    private string? _deliveredNote;
    private readonly TimeSpan _diagnosticsCap;
    public CodeIntelFeedback(McpClient client, string cwd, CodeIntelSettings settings)
        : this(client, cwd, settings, null, TimeSpan.FromSeconds(DiagnosticsCapSeconds))
    {
    }

    /// <summary><paramref name="diagnosticsCap"/> is the diagnostics-call budget; it is injectable so tests can
    /// fail the budget fast. Production uses the <see cref="DiagnosticsCapSeconds"/> constant.</summary>
    public CodeIntelFeedback(
        McpClient client,
        string cwd,
        CodeIntelSettings settings,
        Func<TurnContext, ValueTask<TurnUpdate?>>? inner,
        TimeSpan? diagnosticsCap = null)
    {
        _client = client;
        _diagnosticsDeadline = TimeSpan.FromTicks(
            Math.Min(client.ToolCallTimeout.Ticks, (diagnosticsCap ?? TimeSpan.FromSeconds(DiagnosticsCapSeconds)).Ticks));
        _callDeadline = client.ToolCallTimeout;
        _cwd = cwd;
        _settings = settings;
        _inner = inner;
        _diagnosticsCap = diagnosticsCap ?? TimeSpan.FromSeconds(DiagnosticsCapSeconds);
    }

    /// <summary>The effective feedback mode: an unset setting behaves like <see cref="CodeIntelFeedbackMode.Errors"/>.</summary>
    public CodeIntelFeedbackMode Mode =>
        _settings.Feedback == CodeIntelFeedbackMode.Unset
            ? CodeIntelFeedbackMode.Errors
            : _settings.Feedback;

    /// <summary>Composes the loop's existing next-turn hook, the same way the verifier feedback composes:
    /// the previous note is dropped and only a fresh non-empty delta re-announces itself.</summary>
    public Func<TurnContext, ValueTask<TurnUpdate?>> ComposePrepareNextTurn(
        Func<TurnContext, ValueTask<TurnUpdate?>>? prepare)
    {
        var inner = prepare ?? _inner;
        return async turn =>
        {
            var update = inner is null ? null : await inner(turn).ConfigureAwait(false);
            if (Mode == CodeIntelFeedbackMode.Off)
                return update;
            if (!HasFileMutations(turn.ToolResults))
                return update;

            string? note;
            try
            {
                note = await BuildNoteAsync(turn.ToolResults).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                note = null; // fail open: a note must never fail the run
            }
            if (note is null) return update;
            lock (_gate) _deliveredNote = note;

            // The note goes in through Trailing, where the loop appends it after the turn's steering
            // messages: the last thing the model reads before answering.
            var trailing = new List<Message>(update?.Trailing ?? []);
            trailing.Add(new UserMessage(note));
            return (update ?? new TurnUpdate()) with { Trailing = trailing };
        };
    }

    private async Task<string?> BuildNoteAsync(IReadOnlyList<ToolResultMessage> toolResults)
    {
        var paths = await CollectChangedPathsAsync(toolResults).ConfigureAwait(false);
        if (paths.Count == 0) return null;

        // Lazily attach the workspace; the repo URL comes from the git remote. An unattached
        // repository (no remote, no work tree) means no note.
        var repo = RunGit(_cwd, "remote get-url origin")?.Trim();
        if (string.IsNullOrEmpty(repo)) return null;
        var attachOk = await CallOkAsync("code_attach", new JsonObject { ["repo"] = repo }).ConfigureAwait(false);
        if (!attachOk)
            return null;

        var files = new JsonObject();
        foreach (var path in paths.Take(MaxFilesPerSync))
        {
            var full = Path.GetFullPath(Path.Combine(_cwd, path));
            if (!File.Exists(full)) continue; // untracked-but-deleted, or outside the tree
            var text = await File.ReadAllTextAsync(full).ConfigureAwait(false);
            files[path] = text;
        }
        if (files.Count == 0) return null;
        var syncOk = await CallOkAsync("code_sync", new JsonObject { ["files"] = files }).ConfigureAwait(false);
        if (!syncOk)
            return null;

        var diag = await CallAsync("code_diagnostics", new JsonObject(), diagnosticsCall: true).ConfigureAwait(false);
        return DiagToNote(diag);
    }

    private static bool HasFileMutations(IReadOnlyList<ToolResultMessage> toolResults)
    {
        return toolResults.Any(r => MutatingToolNames.Contains(r.ToolName) && !r.IsError);
    }

    /// <summary>Changed paths: the tools' own results (edit/write/apply_patch) plus the work tree's
    /// modified and untracked files, relative to the working directory.</summary>
    private Task<IReadOnlyList<string>> CollectChangedPathsAsync(IReadOnlyList<ToolResultMessage> toolResults)
    {
        var paths = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var result in toolResults)
        {
            if (!MutatingToolNames.Contains(result.ToolName) || result.IsError) continue;
            foreach (var path in PathsFromToolResult(result))
            {
                var rel = RelativeToCwd(path);
                if (rel is not null) paths.Add(rel);
            }
        }

        // The repo's working tree is the source of truth for git status: a repo rooted above the
        // working directory (a monorepo checkout) reports paths relative to the repo root, so the
        // set must be read there and mapped back with <see cref="RelativeToCwd"/>.
        var gitTop = RunGit(_cwd, "rev-parse --show-toplevel");
        var top = gitTop is null ? _cwd : Path.GetFullPath(Path.Combine(_cwd, gitTop.Trim()));
        foreach (var line in RunGit(top, "status --porcelain")?.Split('\n') ?? [])
        {
            if (line.Length < 4) continue;
            // XY PATH — X is the index, Y the work tree; both being ' ' is impossible in --porcelain.
            // '??' is untracked; everything else has a modified component.
            var index = line[0];
            var work = line[1];
            if (index == ' ' && work == ' ') continue;
            var name = line[3..];
            if (index == 'R' || index == 'C')
            {
                // renamed/copied: only the destination side is live now
                var arrow = name.IndexOf(" -> ");
                if (arrow >= 0) name = name[(arrow + 4)..];
            }
            name = name.Trim();
            if (name.StartsWith("/", StringComparison.Ordinal)) name = name[1..];
            // Porcelain paths are relative to the repo root, not to the working directory.
            var rel = RelativeToCwd(Path.Combine(top, name));
            if (rel is not null) paths.Add(rel);
        }
        return Task.FromResult<IReadOnlyList<string>>(paths.ToList());
    }

    private static IEnumerable<string> PathsFromToolResult(ToolResultMessage result)
    {
        switch (result.ToolName)
        {
            case "edit":
            case "write":
                yield return result.Details?["path"]?.GetValue<string>()
                    ?? FirstPathInText(ContentText.Of(result.Content)) ?? string.Empty;
                yield break;
            case "apply_patch":
                // The patch tool reports each file it touched; the git status fallback covers the rest.
                foreach (var line in ContentText.Of(result.Content).Split('\n'))
                {
                    var m = System.Text.RegularExpressions.Regex.Match(line, @"(?:Add File|Update File|Move to|Delete File):\s*(\S+)");
                    if (m.Success) yield return m.Groups[1].Value;
                }
                yield break;
            default:
                yield break; // bash/job: git status is the source of truth
        }
    }

    private static string? FirstPathInText(string text)
    {
        foreach (var line in text.Split('\n'))
        {
            var m = System.Text.RegularExpressions.Regex.Match(line, @"(?i)^(?:wrote|edited|updated|applied)\s+(?:a file )?([^\s:]+)");
            if (m.Success && !m.Groups[1].Value.Contains('/')) return null;
            if (m.Success) return m.Groups[1].Value;
            if (line.Length > 0 && (line[0] == '/' || line.Contains('/')))
            {
                var first = line.Split(' ')[0];
                if (first.Contains('/')) return first;
            }
        }
        return null;
    }

    /// <summary>Map a path from a tool result to a relative path inside the work tree; null when it is
    /// not one (absolute path outside the tree, no path at all).</summary>
    private string? RelativeToCwd(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var full = Path.GetFullPath(Path.Combine(_cwd, path));
        var root = Path.GetFullPath(_cwd).TrimEnd(Path.DirectorySeparatorChar);
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !full.Equals(root, StringComparison.Ordinal))
            return null;
        return Path.GetRelativePath(root, full);
    }

    private async Task<bool> CallOkAsync(string name, JsonObject args)
    {
        var result = await CallAsync(name, args).ConfigureAwait(false);
        return result is not null && !result.IsError;
    }

    private async Task<McpCallToolResult?> CallAsync(string name, JsonObject args, bool diagnosticsCall = false)
    {
        try
        {
            return await BoundedAsync(name, args,
                diagnosticsCall ? _diagnosticsDeadline : _callDeadline).ConfigureAwait(false);
        }
        catch (TaskCanceledException)
        {
            return null; // the per-call deadline or the diagnostics budget ran out: fail open
        }
        catch (OperationCanceledException)
        {
            return null; // the per-call deadline or the diagnostics budget ran out: fail open
        }
        catch (TimeoutException)
        {
            return null; // the per-call deadline or the diagnostics budget ran out: fail open
        }
    }

    /// <summary>A bounded request with its own per-call deadline: a token canceled at
    /// <paramref name="deadline"/> is layered on the client's per-call timeout, so the shorter of the two
    /// cancels the transport's task. A timeout of this call is caught by <see cref="CallAsync"/> and
    /// fails open; a hard cancellation of the run (no budget) rethrows instead.</summary>
    private async Task<McpCallToolResult> BoundedAsync(string name, JsonObject args, TimeSpan deadline)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken.None);
        cts.CancelAfter(deadline);
        var request = _client.CallToolAsync(name, args, cts.Token);
        var delay = Task.Delay(deadline + TimeSpan.FromMilliseconds(1), CancellationToken.None);
        var finished = await Task.WhenAny(request, delay).ConfigureAwait(false);
        if (finished != request) throw new TimeoutException();
        return await request.ConfigureAwait(false);
    }

    private string? DiagToNote(McpCallToolResult? result)
    {
        if (result is null) return null;
        var text = result.Content is { Count: > 0 } content
            ? string.Join("\n", content.OfType<McpTextPart>().Select(p => p.Text))
            : "";
        if (string.IsNullOrWhiteSpace(text)) return null;
        JsonObject diag;
        try
        {
            diag = JsonNode.Parse(text)?.AsObject() ?? new JsonObject();
        }
        catch (JsonException)
        {
            return null;
        }
        var delta = diag["delta"]?.GetValue<int>() ?? 0;
        if (delta <= 0) return null;
        var files = diag["files"] as JsonArray;
        if (files is null || files.Count == 0) return null;

        var sb = new StringBuilder();
        sb.Append(NotePrefix).Append(": ").Append(delta).Append(" new diagnostic").Append(delta == 1 ? "" : "s");
        sb.Append(Mode == CodeIntelFeedbackMode.ErrorsAndWarnings ? " (errors + warnings)" : " (errors)");
        sb.Append(" since the last check — fix the new errors before moving on:\n");
        var shown = 0;
        foreach (var fileNode in files)
        {
            if (shown >= MaxErrorLines) break;
            if (fileNode is not JsonObject file || file["errors"] is not JsonArray errors || errors.Count == 0)
                continue;
            sb.Append(file["file"]?.GetValue<string>() ?? "?").Append(':');
            foreach (var err in errors)
            {
                if (err is not JsonObject e || shown >= MaxErrorLines) break;
                var line = e["start_line"]?.GetValue<int>() ?? 0;
                var code = e["code"]?.GetValue<string>();
                var message = e["message"]?.GetValue<string>() ?? "";
                var severity = e["severity"]?.GetValue<int>() ?? 1;
                sb.Append('\n').Append("  ").Append(line > 0 ? line + ":" : "");
                if (!string.IsNullOrEmpty(code)) sb.Append('[').Append(code).Append(']');
                sb.Append(' ');
                if (message.Length > 160) message = message[..157] + "...";
                sb.Append(message);
                if (Mode == CodeIntelFeedbackMode.ErrorsAndWarnings && severity > 1)
                    sb.Append(" (warning)");
                shown++;
            }
        }
        var note = sb.ToString();
        if (note.Length > NoteCap)
            note = note.Substring(0, NoteCap - 1) + "…";
        return note;
    }

    /// <summary>Runs git in <paramref name="workingDirectory"/> (the agent's work tree), never in the
    /// process's own current directory, which belongs to whoever launched pi-sharp.</summary>
    private static string? RunGit(string workingDirectory, string args)
    {
        try
        {
            var psi = new ProcessStartInfo("git", args)
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            var result = PiSharp.Core.Processes.ProcessRunner.RunAsync(psi, TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
            return result.ExitCode == 0 ? result.Output : null;
        }
        catch
        {
            return null;
        }
    }
}
