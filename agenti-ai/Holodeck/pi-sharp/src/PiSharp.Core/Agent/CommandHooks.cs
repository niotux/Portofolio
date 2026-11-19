using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using PiSharp.Core.Config;

namespace PiSharp.Core.Agent;

/// <summary>The hook points (Codex hooks; plan-pi-sharp-codex item 4): before and after each tool call, when the run would
/// stop, before and after a compaction, when the session starts, and when a user or inbox message arrives.</summary>
public enum CommandHookEvent { PreToolUse, Stop, PreCompact, SessionStart, UserPromptSubmit, PostToolUse, PostCompact, SubagentStart, SubagentStop }
public enum HookFailMode { Open, Closed }

public sealed record CommandHook(string Command, string Version, TimeSpan Timeout, HookFailMode FailMode = HookFailMode.Open,
    string? Matcher = null);
public sealed record CommandHookResult(bool Block = false, string? Reason = null, string? AdditionalContext = null);

public static class CommandHookRuleParser
{
    private static readonly Regex FencedHook = new("```hook\\s*\\r?\\n(?<json>[\\s\\S]*?)\\r?\\n```", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Reads JSON hook declarations from librarian-authored fenced <c>hook</c> blocks in active KB rule documents.</summary>
    public static IReadOnlyList<CommandHookSetting> ParseDocuments(IEnumerable<string> documents)
    {
        var hooks = new List<CommandHookSetting>();
        foreach (var document in documents)
        foreach (Match match in FencedHook.Matches(document))
        {
            try
            {
                if (JsonNode.Parse(match.Groups["json"].Value) is not JsonObject node
                    || !Enum.TryParse<CommandHookEvent>(node["event"]?.GetValue<string>(), true, out var hookEvent)
                    || !Enum.IsDefined(hookEvent)
                    || node["command"]?.GetValue<string>() is not { Length: > 0 } command) continue;
                var timeout = node["timeoutSeconds"]?.GetValue<int>() ?? 5;
                var failMode = string.Equals(node["failMode"]?.GetValue<string>(), "closed", StringComparison.OrdinalIgnoreCase)
                    ? HookFailMode.Closed : HookFailMode.Open;
                hooks.Add(new CommandHookSetting(hookEvent, command,
                    node["version"]?.GetValue<string>() ?? "1",
                    TimeSpan.FromSeconds(Math.Clamp(timeout, 1, 300)), failMode, node["matcher"]?.GetValue<string>()));
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException) { }
        }
        return hooks;
    }
}

/// <summary>Runs explicitly configured seat-local hook programs with JSON stdin and bounded execution.</summary>
public sealed class CommandHookRunner
{
    private readonly IReadOnlyDictionary<CommandHookEvent, IReadOnlyList<CommandHook>> _handlers;
    private readonly Action<string>? _log;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<CommandHookEvent, int> _timeouts = new();

    public CommandHookRunner(IReadOnlyDictionary<CommandHookEvent, CommandHook> hooks, Action<string>? log = null)
        : this(hooks.ToDictionary(p => p.Key, p => (IReadOnlyList<CommandHook>)[p.Value]), log)
    {
    }

    /// <summary>Several handlers per event, run in order; any block wins (its reason first), added contexts are joined.</summary>
    public CommandHookRunner(IReadOnlyDictionary<CommandHookEvent, IReadOnlyList<CommandHook>> handlers, Action<string>? log = null)
    {
        _handlers = handlers;
        _log = log;
    }

    /// <summary>The first handler of each event (kept for callers that ask whether an event has a hook).</summary>
    public IReadOnlyDictionary<CommandHookEvent, CommandHook> Hooks =>
        _handlers.Where(p => p.Value.Count > 0).ToDictionary(p => p.Key, p => p.Value[0]);
    public bool Has(CommandHookEvent hook) => _handlers.TryGetValue(hook, out var list) && list.Count > 0;
    public int TimeoutCount(CommandHookEvent hook) => _timeouts.GetValueOrDefault(hook);

    /// <summary>Runs every handler of <paramref name="hook"/> whose matcher fits <paramref name="toolName"/> (a handler without
    /// a matcher runs for every call).</summary>
    public async ValueTask<CommandHookResult> InvokeAsync(CommandHookEvent hook, JsonNode input, CancellationToken cancellationToken = default,
        string? toolName = null)
    {
        if (!_handlers.TryGetValue(hook, out var list) || list.Count == 0) return new CommandHookResult();
        string? blockReason = null;
        var blocked = false;
        var contexts = new List<string>();
        foreach (var spec in list)
        {
            if (spec.Matcher is { Length: > 0 } matcher && !Matches(matcher, toolName)) continue;
            var result = await InvokeOneAsync(hook, spec, input.DeepClone(), cancellationToken).ConfigureAwait(false);
            if (result.Block && !blocked) { blocked = true; blockReason = result.Reason; }
            if (result.AdditionalContext is { Length: > 0 } context) contexts.Add(context);
        }
        return new CommandHookResult(blocked, blockReason, contexts.Count == 0 ? null : string.Join("\n", contexts));
    }

    private bool Matches(string matcher, string? toolName)
    {
        try { return toolName is not null && Regex.IsMatch(toolName, matcher, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)); }
        catch (Exception ex) when (ex is ArgumentException or RegexMatchTimeoutException) { _log?.Invoke($"hook matcher '{matcher}' is not usable: {ex.Message}"); return false; }
    }

    private async ValueTask<CommandHookResult> InvokeOneAsync(CommandHookEvent hook, CommandHook spec, JsonNode input, CancellationToken cancellationToken)
    {
        try
        {
            var info = new ProcessStartInfo(OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh");
            info.ArgumentList.Add(OperatingSystem.IsWindows() ? "/d" : "-c");
            if (OperatingSystem.IsWindows())
            {
                info.ArgumentList.Add("/s");
                info.ArgumentList.Add("/c");
            }
            info.ArgumentList.Add(spec.Command);
            info.UseShellExecute = false;
            info.RedirectStandardInput = true;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            using var process = new Process { StartInfo = info };
            process.Start();
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(spec.Timeout <= TimeSpan.Zero ? TimeSpan.FromSeconds(5) : spec.Timeout);
            try
            {
                var stderr = process.StandardError.ReadToEndAsync(deadline.Token);
                var stdoutTask = process.StandardOutput.ReadToEndAsync(deadline.Token);
                await process.StandardInput.WriteAsync(input.ToJsonString().AsMemory(), deadline.Token).ConfigureAwait(false);
                await process.StandardInput.FlushAsync(deadline.Token).ConfigureAwait(false);
                process.StandardInput.Close();
                var stdout = await stdoutTask.ConfigureAwait(false);
                await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
                _ = await stderr.ConfigureAwait(false);
                if (process.ExitCode != 0) return Failure(hook, spec, $"exit {process.ExitCode}");
                using var json = JsonDocument.Parse(stdout);
                var root = json.RootElement;
                return new CommandHookResult(
                    root.TryGetProperty("block", out var block) && block.ValueKind == JsonValueKind.True,
                    root.TryGetProperty("reason", out var reason) ? reason.GetString() : null,
                    root.TryGetProperty("additional_context", out var context) ? context.GetString()
                        : root.TryGetProperty("additionalContext", out context) ? context.GetString() : null);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                if (cancellationToken.IsCancellationRequested) throw;
                var count = _timeouts.AddOrUpdate(hook, 1, (_, current) => current + 1);
                _log?.Invoke($"hook {hook} timed out ({count} total)");
                return Failure(hook, spec, "timeout");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log?.Invoke($"hook {hook} failed: {ex.Message}");
            return Failure(hook, spec, ex.Message);
        }
    }

    private static CommandHookResult Failure(CommandHookEvent hook, CommandHook spec, string reason) =>
        spec.FailMode == HookFailMode.Closed
            ? new CommandHookResult(true, $"{hook} hook failed ({reason}); blocked by fail-closed policy.")
            : new CommandHookResult(false, $"{hook} hook failed ({reason}); continuing (fail-open).");

}
