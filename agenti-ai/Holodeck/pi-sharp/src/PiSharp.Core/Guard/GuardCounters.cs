using System.Text.Json.Nodes;
using PiSharp.Core.Notebook;

namespace PiSharp.Core.Guard;

/// <summary>Override / confirm / fire counters on one seat guard rule, and the deterministic
/// retirement rules built on them (design: guard-rules epic, doc <c>research</c>).
///
/// Counters live on a <c>guard-counters-&lt;id&gt;</c> notebook record (kind <c>guard</c>), so
/// <c>notebook get guard-counters-&lt;id&gt;</c> shows the rule's numbers, and every event also
/// appends a <c>guard-event-*</c> note (kind <c>note</c>, tag <c>guard</c>) so the history is
/// visible per event. A rule is <c>kept, not deleted</c>: retirement writes <c>retiredUtc</c>
/// onto the counters record; the rule keeps its record and its counters but never blocks and
/// never promotes again (the ToolGuard skips retired rules).
///
/// Retirement rules:
/// - <see cref="RecordOverride"/>: the rule retires within the very call where Overridden
///   reaches <see cref="RetireOverrides"/> (2), Confirmed has too, and Overridden exceeds
///   Confirmed. A rule with fewer than two confirms stays alive on overrides alone.
/// - <see cref="RecordFire"/> / <see cref="SweepStale"/>: a rule that was never confirmed and
///   has not fired within <see cref="RetireAfter"/> (30 days) retires; a rule that fired (or
///   was confirmed) stays.</summary>
public static class GuardCounters
{
    /// <summary>Overrides that, together with equally many confirms and more overrides than
    /// confirms, retire a rule (a rule with fewer than two confirms never retires on overrides
    /// alone - one confirm already keeps it alive).</summary>
    public const int RetireOverrides = 2;

    /// <summary>A never-confirmed rule retires when it has not fired within this window.</summary>
    public static readonly TimeSpan RetireAfter = TimeSpan.FromDays(30);

    public sealed record CounterState(int Confirmed, int Overridden, DateTimeOffset? LastFireUtc,
        DateTimeOffset? LastOverrideUtc, DateTimeOffset? RetiredUtc, string? RetireReason);

    public sealed record CountEvent(string RuleId, string Type, int Confirmed, int Overridden,
        bool Retired, string? RetireReason, DateTimeOffset AtUtc);

    private static string CountersKey(string id) => "guard-counters-" + id;

    /// <summary>Reads the counters for a rule (zeroed when none recorded yet).</summary>
    public static CounterState Counters(NotebookStore notebook, string id)
    {
        var row = notebook.Get(CountersKey(id));
        var text = row?["text"]?.GetValue<string>();
        if (text is null) return new CounterState(0, 0, null, null, null, null);
        JsonObject? body;
        try { body = JsonNode.Parse(text)?.AsObject(); }
        catch { return new CounterState(0, 0, null, null, null, null); }
        return new CounterState(
            body?["confirmed"] is JsonValue c ? c.GetValue<int>() : 0,
            body?["overridden"] is JsonValue o ? o.GetValue<int>() : 0,
            Parse(body?["lastFireUtc"]), Parse(body?["lastOverrideUtc"]),
            Parse(body?["retiredUtc"]), body?["retireReason"]?.GetValue<string>());
    }

    /// <summary>Whether the rule may still be evaluated or promoted at all.</summary>
    public static bool IsRetired(NotebookStore notebook, string id) =>
        Counters(notebook, id).RetiredUtc is not null;

    /// <summary>Records that the rule fired against one call and re-evaluates the stale rule
    /// (never confirmed + no fire in <see cref="RetireAfter"/>). Returns the event.</summary>
    public static CountEvent RecordFire(NotebookStore notebook, string id, string? task = null,
        DateTimeOffset? nowUtc = null)
    {
        var now = nowUtc ?? DateTimeOffset.UtcNow;
        var state = Counters(notebook, id);
        // A fire is a fresh observation: the stale rule (never confirmed + no fire within the
        // window) is evaluated against the new observation, not the old one. Retired rules stay
        // retired (retirement is one-way; they are skipped by the guard and the sweep).
        var updated = state with { LastFireUtc = now };
        if (state.RetiredUtc is not null)
            updated = updated with { RetiredUtc = state.RetiredUtc, RetireReason = state.RetireReason };
        Save(notebook, id, updated);
        Journal(notebook, id, task, "fire", updated);
        return new CountEvent(id, "fire", updated.Confirmed, updated.Overridden, updated.RetiredUtc is not null, updated.RetireReason, now);
    }

    /// <summary>Records that a task that hit a block by this rule later passed review
    /// (a confirm). Confirms keep a rule alive even if it never fires again.</summary>
    public static CountEvent RecordConfirm(NotebookStore notebook, string id, string? task = null,
        DateTimeOffset? nowUtc = null)
    {
        var now = nowUtc ?? DateTimeOffset.UtcNow;
        var state = Counters(notebook, id) with { Confirmed = Counters(notebook, id).Confirmed + 1 };
        Save(notebook, id, state);
        Journal(notebook, id, task, "confirm", state);
        return new CountEvent(id, "confirm", state.Confirmed, state.Overridden, false, null, now);
    }

    /// <summary>Records a <c>/guard override</c> or <c># guard-ok: &lt;reason&gt;</c> exemption.
    /// The override that reaches <see cref="RetireOverrides"/> while outnumbering the confirms
    /// retires the rule within this call; with fewer than two confirms the rule stays alive -
    /// retirement is the rule failing its own contract, not a single over-ride.</summary>
    public static CountEvent RecordOverride(NotebookStore notebook, string id, string reason,
        string? task = null, DateTimeOffset? nowUtc = null)
    {
        var now = nowUtc ?? DateTimeOffset.UtcNow;
        var before = Counters(notebook, id);
        var updated = before with { Overridden = before.Overridden + 1, LastOverrideUtc = now };
        bool retired = updated.Overridden >= RetireOverrides && updated.Overridden > updated.Confirmed
            && updated.RetiredUtc is null;
        if (!retired)
        {
            // The rule may have been retired already (retirement is one-way); keep it that way.
            var reloaded = Counters(notebook, id);
            updated = reloaded with { Overridden = reloaded.Overridden + 1, LastOverrideUtc = now };
        }
        if (retired)
            updated = updated with { RetiredUtc = now,
                RetireReason = $"overridden {updated.Overridden}x against {updated.Confirmed} confirms" };
        Save(notebook, id, updated);
        Journal(notebook, id, task, "override: " + reason, updated);
        return new CountEvent(id, "override", updated.Confirmed, updated.Overridden, retired, updated.RetireReason, now);
    }

    /// <summary>Sweep: every counters record of a non-retired rule that was never confirmed and
    /// has not fired within <see cref="RetireAfter"/> retires. Returns the rule ids retired.</summary>
    public static IReadOnlyList<string> SweepStale(NotebookStore notebook, DateTimeOffset? nowUtc = null)
    {
        var now = nowUtc ?? DateTimeOffset.UtcNow;
        var prefix = CountersKey("");
        var retired = new List<string>();
        foreach (var row in notebook.List(kind: "guard"))
        {
            var id = row["id"]?.GetValue<string>();
            if (id is null || !id.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var ruleId = id.Substring(prefix.Length);
            var state = Counters(notebook, ruleId);
            if (state.RetiredUtc is null && state.Confirmed == 0
                && (state.LastFireUtc is null || now - state.LastFireUtc >= RetireAfter))
            {
                var updated = state with { RetiredUtc = now, RetireReason = "never confirmed and no fire in 30 days" };
                Save(notebook, ruleId, updated);
                Journal(notebook, ruleId, null, "retire-sweep", updated);
                retired.Add(ruleId);
            }
        }
        return retired;
    }

    private static void Save(NotebookStore notebook, string id, CounterState state)
    {
        var body = new JsonObject
        {
            ["ruleId"] = id,
            ["confirmed"] = state.Confirmed,
            ["overridden"] = state.Overridden,
            ["lastFireUtc"] = state.LastFireUtc?.ToString("O"),
            ["lastOverrideUtc"] = state.LastOverrideUtc?.ToString("O"),
            ["retiredUtc"] = state.RetiredUtc?.ToString("O"),
            ["retireReason"] = state.RetireReason,
        };
        notebook.Write(CountersKey(id), body.ToJsonString(),
            title: "guard counters " + id, tags: ["guard"], kind: "guard");
    }

    private static void Journal(NotebookStore notebook, string id, string? task, string type, CounterState state)
    {
        var text = "rule " + id + " " + type + ": confirmed=" + state.Confirmed + " overridden=" + state.Overridden
            + (state.RetiredUtc is not null ? " RETIRED (" + state.RetireReason + ")" : "");
        notebook.Write("guard-event-" + id + "-" + DateTimeOffset.UtcNow.ToUnixTimeSeconds() + "-" +
                Math.Abs(text.GetHashCode()),
            text, title: "guard event " + id, tags: ["guard"], kind: "note", task: task);
    }

    private static DateTimeOffset? Parse(JsonNode? node)
    {
        var text = node?.GetValue<string>();
        return DateTime.TryParse(text, null,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var value)
            ? new DateTimeOffset(value, TimeSpan.Zero)
            : null;
    }
}
