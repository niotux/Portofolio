using System.Text.Json.Nodes;
using PiSharp.Core.Agent;
using PiSharp.Core.Messages;
using PiSharp.Core.Models;

namespace PiSharp.Core.Notebook;

/// <summary>Durable logical model-call budget, including summary calls and overflow recovery.
/// Charges before dispatch so a crash cannot give a task unlimited fresh turns.
/// pi-sharp-hh-6: when a threshold (default 25%, 10%, 5% of each limit) is crossed, the seat may owe
/// a one-time reminder, durable in the notebook so a restart does not repeat it.</summary>
public sealed class TaskBudget
{
    private static readonly int[] DefaultReminderAt = [25, 10, 5];

    private readonly NotebookStore _store;
    private readonly Func<string> _taskId;
    private readonly int _maxTurns;
    private readonly int _maxOutputTokens;
    private readonly int _maxToolCalls;
    private readonly int[] _reminderAt;
    private readonly HashSet<(string Task, int Pct)> _delivered = new();
    private string? _lastReminder;

    public TaskBudget(NotebookStore store, Func<string> taskId, int maxTurns, int maxOutputTokens, int maxToolCalls, int[]? reminderAt = null)
    {
        _store = store;
        _taskId = taskId;
        _maxTurns = maxTurns;
        _maxOutputTokens = maxOutputTokens;
        _maxToolCalls = maxToolCalls;
        _reminderAt = reminderAt is { Length: > 0 } ? reminderAt.Where(p => p > 0).OrderByDescending(p => p).ToArray() : DefaultReminderAt;
    }

    /// <summary>The budget limits this budget enforces.</summary>
    public (int MaxTurns, int MaxOutputTokens, int MaxToolCalls) Limits => (_maxTurns, _maxOutputTokens, _maxToolCalls);

    public StreamFunction Wrap(StreamFunction inner) => (model, context, options, ct) => Run(inner, model, context, options, ct);

    /// <summary>Live view of the counters (as last settled on disk), for the context_remaining tool.</summary>
    public (int Turns, int OutputTokens, int ToolCalls) Snapshot(string task)
    {
        var row = _store.Get("budget-" + task);
        if (row is null) return (0, 0, 0);
        var counters = JsonNode.Parse(row["text"]!.GetValue<string>())!.AsObject();
        return (counters["turns"]!.GetValue<int>(), counters["outputTokens"]!.GetValue<int>(), counters["toolCalls"]!.GetValue<int>());
    }

    /// <summary>True when a reminder for this task is already marked delivered in the notebook
    /// (a previous seat delivered it) or by this seat (its write settled, even if not yet read back).</summary>
    public bool ReminderDelivered(string task, int pct)
    {
        if (_store.Get("budget-reminder-" + task + ":" + pct) is not null) return true;
        lock (_delivered) { return _delivered.Contains((task, pct)); }
    }

    /// <summary>The reminder due after the turn just charged, consumed by the seat's next-turn hook.
    /// Null when no reminder was due for that turn (or it was already delivered).</summary>
    public string? TakeDueReminder() { var r = _lastReminder; _lastReminder = null; return r; }

    /// <summary>One reminder per threshold per task, exactly once across restarts: the durable notebook
    /// mark is checked before dispatch and written before the message is returned, so a crash cannot
    /// redeliver it (at-most-once, never twice). Null when no threshold was crossed by this charge.
    /// Never fires at exhaustion: the existing budget_exhausted error stands alone.</summary>
    public string? ReminderDue(int turnsCharged, int tokensReserved, int callsCharged)
    {
        if (turnsCharged >= _maxTurns || tokensReserved >= _maxOutputTokens || callsCharged >= _maxToolCalls) return null;
        var pcts = new List<int>(_reminderAt.Length);
        void Check(int used, int max) { if (max > 0 && used < max) foreach (var p in _reminderAt) if ((max - used) * 100 <= (long)p * max) pcts.Add(p); }
        Check(turnsCharged, _maxTurns);
        Check(tokensReserved, _maxOutputTokens);
        Check(callsCharged, _maxToolCalls);
        if (pcts.Count == 0) return null;
        var task = _taskId();
        // Pick the highest threshold not yet delivered (a lower one may already be settled).
        int? pct = null;
        foreach (var p in pcts.OrderByDescending(p => p)) { if (!ReminderDelivered(task, p)) { pct = p; break; } }
        if (pct is null) return null;
        var id = "budget-reminder-" + task + ":" + pct;
        var text = "Budget: " + pct + "% left (turns " + turnsCharged + "/" + _maxTurns + ", output tokens " + tokensReserved + "/" + _maxOutputTokens + ", tool calls " + callsCharged + "/" + _maxToolCalls + ")."
            + (pct <= 5 ? " At 5%: commit WIP, write the handover note, stop." : "");
        try
        {
            _store.Put(id, "note", text, task, "open", _store.Get(id)?["revision"]?.GetValue<long>() ?? 0);
            lock (_delivered) { _delivered.Add((task, pct.Value)); }
            return text;
        }
        catch { return null; } // a reminder must never fail a turn
    }

    private async IAsyncEnumerable<AssistantMessageEvent> Run(StreamFunction inner, ModelConfig model, LlmContext context, StreamOptions options,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var task = _taskId();
        var id = "budget-" + task;
        var row = _store.Get(id);
        var counters = row is null ? new JsonObject { ["turns"] = 0, ["outputTokens"] = 0, ["toolCalls"] = 0 } : JsonNode.Parse(row["text"]!.GetValue<string>())!.AsObject();
        var turns = counters["turns"]!.GetValue<int>();
        var tokens = counters["outputTokens"]!.GetValue<int>();
        var calls = counters["toolCalls"]!.GetValue<int>();
        AssistantError Exhausted(string reason) => new(StopReason.Error, new AssistantMessage { Model = model.Id, Content = [], StopReason = StopReason.Error, ErrorCode = "budget_exhausted", ErrorMessage = reason });
        if (turns >= _maxTurns || tokens >= _maxOutputTokens || calls >= _maxToolCalls)
        { yield return Exhausted("Task budget exhausted; unfinished commitments remain in notebook. Raise the task budget or explicitly resume after review."); yield break; }
        var allowance = Math.Min(options.MaxTokens ?? model.MaxOutputTokens, _maxOutputTokens - tokens);
        // Reserve the full allowance before dispatch. Refund only after an observed terminal result.
        // Unknown/crashed calls retain the reservation instead of pretending they used no tokens.
        counters["turns"] = turns + 1; counters["outputTokens"] = tokens + allowance;
        var reserved = _store.Put(id, "attempt", counters.ToJsonString(), task, "open", row?["revision"]?.GetValue<long>() ?? 0);
        var bounded = options with { MaxTokens = allowance };
        await foreach (var evt in inner(model, context, bounded, ct).WithCancellation(ct).ConfigureAwait(false))
        {
            var terminal = evt switch { AssistantDone done => done.Message, AssistantError error => error.Error, _ => null };
            if (terminal is not null)
            {
                var requested = terminal.ToolCalls.Count();
                counters["outputTokens"] = tokens + (terminal.Usage.Output > 0 ? terminal.Usage.Output : allowance);
                counters["toolCalls"] = calls + requested;
                _store.Put(id, "attempt", counters.ToJsonString(), task, "open", reserved["revision"]!.GetValue<long>());
                // pi-sharp-hh-6: settle against the turn the reminder actually describes (the one that
                // just finished), so the seat sees it on the next turn.
                _lastReminder = ReminderDue(turns + 1, counters["outputTokens"]!.GetValue<int>(), calls + requested);
                if (calls + requested > _maxToolCalls)
                { yield return Exhausted("Task tool-call budget exhausted before executing this batch; no calls in this batch were run."); yield break; }
            }
            yield return evt;
        }
    }
}
