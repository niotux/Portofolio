using System.Globalization;
using System.Text.Json.Nodes;
using PiSharp.Core.Notebook;

namespace PiSharp.Core.Factory;

/// <summary>
/// A budget per epic, with notices (plan-pi-sharp-codex item 14; Codex's goal budgets): the output tokens a seat spends while it
/// holds an epic are counted in its notebook, across tasks and restarts, and the seat is told once at 50%, 80% and 100% of
/// the epic's budget and at every further 50%. It informs, it never stops the work (digital-scribe: never throttle delivery).
/// The budget is the epic's <c>attrs.budget_output_tokens</c>, else the seat's <c>epicOutputBudget</c> setting.
/// </summary>
public sealed class EpicBudget(NotebookStore store, long defaultBudget)
{
    private readonly object _gate = new();
    public const long DefaultOutputBudget = 2_000_000;
    private static readonly int[] FirstNotices = [50, 80, 100];

    /// <summary>Adds <paramref name="outputTokens"/> to the epic's count; returns the notice owed, if this charge crossed one.</summary>
    public string? Charge(string epic, long outputTokens, long? budget = null)
    {
        lock (_gate) return ChargeLocked(epic, outputTokens, budget);
    }

    private string? ChargeLocked(string epic, long outputTokens, long? budget)
    {
        var limit = budget is > 0 ? budget.Value : defaultBudget;
        if (limit <= 0 || outputTokens <= 0) return null;
        var id = "epic-budget-" + epic;
        var row = store.Get(id);
        var used = row?["text"]?.GetValue<string>() is { } text && JsonNode.Parse(text)?["output"]?.GetValue<long>() is { } n ? n : 0;
        var now = used + outputTokens;
        store.Put(id, "attempt", new JsonObject { ["output"] = now, ["budget"] = limit }.ToJsonString(), epic, "open", row?["revision"]?.GetValue<long>() ?? 0);
        var before = (int)(used * 100 / limit);
        var after = (int)(now * 100 / limit);
        var due = Thresholds(after).Where(p => p > before && p <= after).DefaultIfEmpty(0).Max();
        if (due == 0) return null;
        var marker = $"epic-budget-notice-{epic}:{due}";
        if (store.Get(marker) is not null) return null;
        store.Put(marker, "note", $"notice at {due}%", epic, "completed", 0);
        return Notice(epic, due, now, limit);
    }

    /// <summary>50, 80, 100, then every 50 up to and past <paramref name="pct"/>.</summary>
    private static IEnumerable<int> Thresholds(int pct)
    {
        foreach (var p in FirstNotices) yield return p;
        for (var p = 150; p <= pct; p += 50) yield return p;
    }

    public static string Notice(string epic, int pct, long used, long limit) =>
        string.Create(CultureInfo.InvariantCulture, $"[harness] {epic} has used {used * 100 / limit}% of its output budget ({used:N0} of {limit:N0} tokens). ") +
        (pct < 100
            ? "Put what is left into what proves its acceptance criteria, and keep its memory current."
            : "Hand in what is proven, or write in the epic's memory why it needs more and what is left; the budget informs, it does not stop you.");
}
