using System.Text.Json.Nodes;

namespace PiSharp.Core.Verification;

/// <summary>What a done-check executes. Command is run by the harness; the file/JSON/KB kinds are
/// harness-side probes the model does not control.</summary>
public enum DoneCheckKind
{
    Command,
    FileExists,
    Grep,
    JsonPath,
    KbState,
}

/// <summary>One done-check declared by the work-item spec (a ```done-check fence) or .pi/done-checks.json
/// in the repo. Declared by the spec, never by the model: the harness runs it, so a pass signal cannot
/// be authored or edited by the maker (the check files themselves are hash-pinned via PinnedPaths).</summary>
public sealed record DoneCheck(
    string Id,
    DoneCheckKind Kind,
    string Spec,
    string? Cwd = null,
    int TimeoutSeconds = 600,
    int ExpectedExit = 0,
    string? Filter = null,
    string? DbState = null,
    string[]? PinnedPaths = null)
{
    public const int MaxTimeoutSeconds = 3600;

    /// <summary>Parses one check from a fence body or a done-checks.json entry.
    /// JSON shape: {"id":..., "kind":"command", "spec":..., "cwd":..., "timeoutSeconds":..., "expectedExit":...,
    /// "filter":..., "dbState":..., "pinnedPaths":[...]}. Returns null for entries that are not a check.</summary>
    public static DoneCheck? Parse(JsonObject o)
    {
        var id = o["id"]?.GetValue<string>();
        var kind = o["kind"]?.GetValue<string>();
        var spec = o["spec"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(spec) || kind is null || !Enum.TryParse(kind, true, out DoneCheckKind parsed))
            return null;
        var pinned = o["pinnedPaths"] is JsonArray array ? array.Select(n => n?.GetValue<string>() ?? "").Where(s => !string.IsNullOrWhiteSpace(s)).ToArray() : null;
        return new DoneCheck(
            id,
            parsed,
            spec.Trim(),
            o["cwd"]?.GetValue<string>(),
            Math.Clamp(o["timeoutSeconds"]?.GetValue<int>() ?? 600, 1, MaxTimeoutSeconds),
            o["expectedExit"]?.GetValue<int>() ?? 0,
            o["filter"]?.GetValue<string>(),
            o["dbState"]?.GetValue<string>(),
            pinned is { Length: > 0 } ? pinned : null);
    }
}

/// <summary>Outcome of one check plus the workspace state before and after the run: this is the evidence a
/// review is graded from (SHA + DB state + filter, per the evidence-hygiene rule).</summary>
public sealed record VerificationResult(
    string CheckId,
    bool Passed,
    int? ExitCode,
    string OutputTail,
    JsonObject Before,
    JsonObject After,
    bool PinnedUnchanged,
    string Head,
    DateTimeOffset At)
{
    /// <summary>One line per check: "ok dc-1 (exit 0, head 3f1)" / "FAIL dc-2 (exit 1: ...)".</summary>
    public string SummaryLine()
    {
        var exit = ExitCode is { } code ? $"exit {code}" : "probe";
        var tail = OutputTail.Length > 0 ? ": " + OutputTail.Trim() : "";
        return (Passed ? "ok   " : "FAIL ") + CheckId + " (" + exit + (Head.Length == 40 ? ", head " + Head[..7] : "") + ")" + tail;
    }
}

/// <summary>Where the checks for a task come from. The task key is the work item (or chat) the task works on.
/// Implementations read the spec or the repo, never the model's notebook.</summary>
public interface IDoneCheckSource
{
    IReadOnlyList<DoneCheck> For(string task);
}
