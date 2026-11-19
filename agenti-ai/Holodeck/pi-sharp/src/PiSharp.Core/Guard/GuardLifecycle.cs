using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using PiSharp.Core.Notebook;

namespace PiSharp.Core.Guard;

public enum GuardStatus { Proposed, Shadow, Active, Retired }

/// <summary>What a candidate rule resolved to against the rules already in use.</summary>
public enum GuardResolution { Noop, New, Supersede }

/// <summary>One recorded tool invocation (serialized as <c>name + " " + arguments</c>, the same text
/// <see cref="ToolGuard.Check"/> evaluates) plus the git state when it ran, captured by correction
/// capture (guard-rules-2) or replayed from a notebook <c>correction</c> record.</summary>
public sealed record RecordedCall(string Name, string Arguments, string? Branch, string? Sha);

/// <summary>One call from a <c>correction-{messageId}</c> notebook record.</summary>
public sealed record CorrectionEntry(RecordedCall Call, bool Offending);

/// <summary>A <c>correction-{messageId}</c> notebook record as parsed by the distil pipeline.</summary>
public sealed record CorrectionRecord(string Id, string? Task, IReadOnlyList<CorrectionEntry> Calls)
{
    public IReadOnlyList<RecordedCall> OffendingCalls => Calls.Where(c => c.Offending).Select(c => c.Call).ToArray();
    public IReadOnlyList<RecordedCall> GoodCalls => Calls.Where(c => !c.Offending).Select(c => c.Call).ToArray();
    public bool HasOffendingCall => OffendingCalls.Count > 0;
}

/// <summary>The replay gate: a candidate must block at least one recorded offending call and none
/// of the seat's last <see cref="GoodCallWindow"/> successful calls. Returns the offending ids
/// blocked, the good ids it would wrongly block, and a reason when it fails.</summary>
public static class GuardReplay
{
    /// <summary>How many recent successful calls are replayed against a candidate. Design value.</summary>
    public const int GoodCallWindow = 200;

    public sealed record Outcome(bool Passed, IReadOnlyList<string> BlockedOffending, IReadOnlyList<string> BlockedGood, string? FailureReason);

    /// <summary>Evaluates one compiled pattern against the offending and good calls.</summary>
    public static Outcome Evaluate(string id, string pattern, IEnumerable<RecordedCall> offending, IEnumerable<RecordedCall> good)
    {
        Regex regex;
        try { regex = ToolGuard.CompileForPattern(pattern); }
        catch (Exception ex) { return new Outcome(false, [], [], "regex does not compile: " + ex.Message); }

        var blockedOffending = offending.Where(call => LocalMatch(regex, call)).Select(call => $"{call.Name}:{call.Arguments}").ToArray();
        var blockedGood = good.Where(call => LocalMatch(regex, call)).Select(call => $"{call.Name}:{call.Arguments}").ToArray();
        if (blockedOffending.Length == 0)
            return new Outcome(false, [], blockedGood, "blocks none of the recorded offending calls");
        if (blockedGood.Length > 0)
            return new Outcome(false, blockedOffending, blockedGood, "blocks " + blockedGood.Length + " recent successful call(s): "
                + string.Join(", ", blockedGood.Take(3)));
        return new Outcome(true, blockedOffending, [], null);
    }

    private static bool LocalMatch(Regex regex, RecordedCall call)
    {
        try { return regex.IsMatch(call.Name + " " + call.Arguments); }
        catch { return false; }
    }

    /// <summary>The last <see cref="GoodCallWindow"/> successful calls from a notebook store
    /// (<c>guard-call-*</c> records, newest first in the listing; the first recorded call of a
    /// <c>correction-*</c> record is the offending one and never counts as good).</summary>
    public static IReadOnlyList<RecordedCall> GoodCallsFromNotebook(NotebookStore notebook)
    {
        // The replay baseline is the seat's recent successful calls (guard-call records).
        // Correction records provide the OFFENDING calls, not good ones.
        var calls = new List<RecordedCall>();
        foreach (var row in notebook.List(kind: "guard-call"))
        {
            calls.Add(CallFrom(row));
        }
        // Newest first: the journal is append-only, List is in insertion order.
        return calls.TakeLast(GoodCallWindow).ToArray();
    }

    private static RecordedCall CallFrom(JsonObject row)
    {
        // The call data is serialized as JSON in the notebook record's text field.
        var text = row["text"]?.GetValue<string>();
        if (text is null) return new RecordedCall("unknown", "", null, null);
        JsonObject? body;
        try { body = JsonNode.Parse(text)?.AsObject(); } catch { body = null; }
        if (body is null) return new RecordedCall("unknown", "", null, null);
        var name = body["name"]?.GetValue<string>() ?? "unknown";
        var args = body["arguments"]?.GetValue<string>() ?? "";
        var branch = body["branch"] is JsonValue b ? b.GetValue<string>() : null;
        var sha = body["sha"] is JsonValue s ? s.GetValue<string>() : null;
        return new RecordedCall(name, args, branch, sha);
    }

    private static RecordedCall? FirstCallFrom(JsonObject row)
    {
        // Correction records store a JSON array of calls in the text field; the first entry is the offending call.
        var text = row["text"]?.GetValue<string>();
        if (text is null) return null;
        JsonArray? calls;
        try { calls = JsonNode.Parse(text)?.AsArray(); } catch { return null; }
        if (calls is null || calls.Count == 0) return null;
        var first = calls[0] as JsonObject;
        if (first is null) return null;
        var name = first["name"]?.GetValue<string>() ?? "unknown";
        var args = first["arguments"]?.GetValue<string>() ?? "";
        var branch = first["branch"] is JsonValue b ? b.GetValue<string>() : null;
        var sha = first["sha"] is JsonValue s ? s.GetValue<string>() : null;
        return new RecordedCall(name, args, branch, sha);
    }
}

public sealed class ShadowState
{
    public required string RuleId { get; init; }
    public int Evaluations { get; set; }
    public int FalsePositives { get; set; }
    public DateTimeOffset? StartedUtc { get; set; }
    public DateTimeOffset? EndedUtc { get; set; }
    public DateTimeOffset? LastEvaluationUtc { get; set; }
}

/// <summary>The proposed/shadow lifecycle of a seat rule. A rule enters shadow with <see cref="BeginShadow"/>;
/// every evaluation is counted with <see cref="RecordEvaluation"/>; it promotes only on
/// <see cref="EligibleForActive"/>, which requires the full window and zero false positives.</summary>
public sealed class GuardLifecycle
{
    public const int RequiredShadowEvaluations = 20;
    public static readonly TimeSpan ShadowWindow = TimeSpan.FromHours(48);

    private readonly NotebookStore _notebook;
    private readonly ShadowState _state;

    internal GuardLifecycle(NotebookStore notebook, ShadowState state)
    {
        _notebook = notebook;
        _state = state;
    }

    public string RuleId => _state.RuleId;
    public int Evaluations => _state.Evaluations;
    public int FalsePositives => _state.FalsePositives;
    public GuardStatus Status =>
        _state.EndedUtc is not null ? GuardStatus.Retired
        : _state.Evaluations >= RequiredShadowEvaluations && _state.FalsePositives == 0 ? GuardStatus.Active
        : GuardStatus.Shadow;

    private static string Key(string id) => "guard-" + id;

    /// <summary>Starts a proposed rule in shadow. Returns null when the rule already has a live or
    /// retired shadow record.</summary>
    public static GuardLifecycle? BeginShadow(NotebookStore notebook, string id)
    {
        var existing = notebook.Get(Key(id));
        if (existing is not null && existing["status"]?.GetValue<string>() is "open" or "blocked")
            return null; // live shadow (or the rule is in use); the distil turn must not overwrite it
        var state = new ShadowState { RuleId = id, StartedUtc = DateTimeOffset.UtcNow };
        SaveState(notebook, state, status: "open");
        return new GuardLifecycle(notebook, state);
    }

    /// <summary>Resumes the lifecycle of an existing shadow/active rule, or null when none exists.</summary>
    public static GuardLifecycle? Resume(NotebookStore notebook, string id)
    {
        var row = notebook.Get(Key(id));
        if (row is null) return null;
        var state = LoadState(row);
        if (state is null) return null;
        return new GuardLifecycle(notebook, state);
    }

    /// <summary>Counts one evaluation. <paramref name="blockedGoodCall"/> is a false positive: the rule
    /// matched a call the seat had previously succeeded with.</summary>
    public void RecordEvaluation(bool wouldBlock, bool blockedGoodCall = false)
    {
        if (!_state.EndedUtc.HasValue)
        {
            _state.Evaluations++;
            _state.LastEvaluationUtc = DateTimeOffset.UtcNow;
            if (wouldBlock && blockedGoodCall) _state.FalsePositives++;
        }
        Save();
    }

    /// <summary>Whether shadow has satisfied its window: 20 evaluations or 48 h, and zero false
    /// positives.</summary>
    public bool EligibleForActive() =>
        _state.EndedUtc is null
        && _state.FalsePositives == 0
        && (_state.Evaluations >= RequiredShadowEvaluations
            || (_state.StartedUtc is { } start && DateTimeOffset.UtcNow - start >= ShadowWindow));

    /// <summary>Retires the rule (it may no longer be evaluated or promoted). Idempotent.</summary>
    public void Retire()
    {
        if (_state.EndedUtc.HasValue) return;
        _state.EndedUtc = DateTimeOffset.UtcNow;
        Save();
    }

    /// <summary>Replaces the rule's stored pattern when a candidate supersedes it. The rule keeps its
    /// shadow state (evaluations and false positives) until it retires.</summary>
    public void UpdatePattern(string pattern)
    {
        SaveState(_notebook, _state, _state.EndedUtc is null ? "open" : "completed", pattern);
    }

    private void Save() => SaveState(_notebook, _state, _state.EndedUtc is null ? "open" : "completed");

    private static void SaveState(NotebookStore notebook, ShadowState state, string status, string? pattern = null)
    {
        var body = new JsonObject
        {
            ["ruleId"] = state.RuleId,
            ["evaluations"] = state.Evaluations,
            ["falsePositives"] = state.FalsePositives,
            ["startedUtc"] = state.StartedUtc?.ToString("O") ?? "",
            ["endedUtc"] = state.EndedUtc?.ToString("O"),
            ["lastEvaluationUtc"] = state.LastEvaluationUtc?.ToString("O"),
        };
        if (pattern is not null) body["pattern"] = pattern;
        notebook.Write(Key(state.RuleId), body.ToJsonString(), title: "guard rule lifecycle",
            tags: ["guard"], kind: "guard", status: status);
    }

    private static ShadowState? LoadState(JsonObject row)
    {
        if (row["kind"]?.GetValue<string>() != "guard") return null;
        var text = row["text"]?.GetValue<string>();
        if (text is null) return null;
        JsonObject? body;
        try { body = JsonNode.Parse(text)?.AsObject(); }
        catch { return null; }
        if (body is null) return null;
        var id = body["ruleId"]?.GetValue<string>();
        if (id is null) return null;
        return new ShadowState
        {
            RuleId = id,
            Evaluations = body["evaluations"] is JsonValue e ? e.GetValue<int>() : 0,
            FalsePositives = body["falsePositives"] is JsonValue f ? f.GetValue<int>() : 0,
            StartedUtc = ParseUtc(body["startedUtc"]),
            EndedUtc = ParseUtc(body["endedUtc"]),
            LastEvaluationUtc = ParseUtc(body["lastEvaluationUtc"]),
        };
    }

    private static DateTimeOffset? ParseUtc(JsonNode? node)
    {
        var text = node?.GetValue<string>();
        return DateTime.TryParse(text, null, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var value)
            ? new DateTimeOffset(value, TimeSpan.Zero)
            : null;
    }
}

/// <summary>The idle distil pipeline: read the seat's <c>correction</c> records, gate a proposed
/// rule (parse, compile, replay, resolve) and move it to shadow. Failing candidates stay as
/// <c>guard-note</c> records; nothing mid-task ever runs this.</summary>
public sealed class GuardDistil
{
    public sealed record Candidate(string Id, string Pattern, string Message);
    public sealed record GatedResult(Candidate Candidate, bool Passed, GuardResolution Resolution,
        string? FailureReason, GuardLifecycle? Lifecycle);

    private readonly NotebookStore _notebook;

    public GuardDistil(NotebookStore notebook) => _notebook = notebook;

    public IReadOnlyList<CorrectionRecord> Corrections() =>
        _notebook.List(kind: "correction").Where(r => r["id"]?.GetValue<string>() is not null)
            .Select(Read).Where(r => r is not null).Cast<CorrectionRecord>().ToArray();

    /// <summary>The full gate chain for one candidate.</summary>
    public GatedResult Gate(Candidate candidate)
    {
        if (candidate is null) throw new ArgumentNullException(nameof(candidate));
        var corrections = Corrections();
        if (!corrections.Any(c => c.HasOffendingCall))
            return Fail(candidate, "no recorded correction with an offending call; candidate stays a note");

        var offending = corrections.Where(c => c.HasOffendingCall).SelectMany(c => c.OffendingCalls).ToArray();
        var good = GuardReplay.GoodCallsFromNotebook(_notebook);

        // 1. It parses (the candidate's own fields). 2. The regex compiles. 3. Replay.
        var replay = GuardReplay.Evaluate(candidate.Id, candidate.Pattern, offending, good);
        if (!replay.Passed)
        {
            Note(candidate, "replay gate failed: " + replay.FailureReason);
            return new GatedResult(candidate, false, GuardResolution.Noop, replay.FailureReason, null);
        }

        // 4. Resolve against existing rules: Noop if an existing rule already covers the offending
        //    calls; Supersede if this rule id already has a guard record (pattern update);
        //    New otherwise.
        var resolution = ExistingRuleMatches(candidate)
            ? GuardResolution.Noop
            : (_notebook.Get("guard-" + candidate.Id) is not null
                ? GuardResolution.Supersede
                : GuardResolution.New);
        if (resolution == GuardResolution.Noop)
        {
            Note(candidate, "superseded by existing rule; no new rule needed");
            return new GatedResult(candidate, true, resolution, null, null);
        }

        var lifecycle = resolution == GuardResolution.Supersede
            ? GuardLifecycle.Resume(_notebook, candidate.Id)
            : GuardLifecycle.BeginShadow(_notebook, candidate.Id);
        if (lifecycle is null)
        {
            Note(candidate, "could not start a shadow record; candidate stays a note");
            return new GatedResult(candidate, false, resolution, "no shadow record", null);
        }
        if (resolution == GuardResolution.Supersede) lifecycle.UpdatePattern(candidate.Pattern);
        lifecycle.RecordEvaluation(wouldBlock: true);
        Note(candidate, "passed gates; " + (resolution == GuardResolution.Supersede ? "supersedes" : "enters shadow as")
            + " guard rule, shadow window " + GuardLifecycle.RequiredShadowEvaluations + " evaluations");
        return new GatedResult(candidate, true, resolution, null, lifecycle);
    }

    private bool ExistingRuleMatches(Candidate candidate)
    {
        var existing = _notebook.List(kind: "guard");
        Regex regex;
        try { regex = ToolGuard.CompileForPattern(candidate.Pattern); }
        catch { return false; }
        var offending = Corrections().Where(c => c.HasOffendingCall).SelectMany(c => c.OffendingCalls).ToArray();
        return existing.Any(row =>
        {
            var text = row["text"]?.GetValue<string>();
            if (text is null) return false;
            JsonObject? body;
            try { body = JsonNode.Parse(text)?.AsObject(); } catch { return false; }
            var pattern = body?["pattern"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(pattern)) return false;
            Regex existingRegex;
            try { existingRegex = ToolGuard.CompileForPattern(pattern); } catch { return false; }
            // Noop when the existing rule already covers every offending call the new one does.
            return offending.All(call =>
            {
                try { return existingRegex.IsMatch(call.Name + " " + call.Arguments); } catch { return false; }
            });
        });
    }

    private void Note(Candidate candidate, string reason)
    {
        _notebook.Write("guard-note-" + candidate.Id + "-" + DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            candidate.Pattern + "\n" + reason,
            title: "guard candidate " + candidate.Id, tags: ["guard"], kind: "note");
    }

    private GatedResult Fail(Candidate candidate, string reason)
    {
        Note(candidate, reason);
        return new GatedResult(candidate, false, GuardResolution.Noop, reason, null);
    }

    /// <summary>Parses a <c>correction-{messageId}</c> record written by correction capture
    /// (guard-rules-2). The first entry is the offending call; the rest are the last successful
    /// calls before it.</summary>
    private CorrectionRecord? Read(JsonObject row)
    {
        var id = row["id"]!.GetValue<string>();
        var task = row["task"]?.GetValue<string>();
        var text = row["text"]?.GetValue<string>();
        if (text is null) return null;
        JsonArray? calls;
        try
        {
            var body = JsonNode.Parse(text)?.AsObject();
            calls = body?["calls"] as JsonArray;
        }
        catch { calls = null; }
        if (calls is null) return null;
        var entries = calls.Select((entry, index) =>
        {
            var call = entry as JsonObject;
            if (call is null) return null;
            var name = call["name"]?.GetValue<string>() ?? "unknown";
            var args = call["arguments"]?.GetValue<string>() ?? "";
            var branch = call["branch"] is JsonValue b ? b.GetValue<string>() : null;
            var sha = call["sha"] is JsonValue s ? s.GetValue<string>() : null;
            return new CorrectionEntry(new RecordedCall(name, args, branch, sha), index == 0);
        }).Where(e => e is not null).Cast<CorrectionEntry>().ToArray();
        if (entries.Length == 0) return null;
        return new CorrectionRecord(id, task, entries);
    }
}
