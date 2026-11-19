using System.Text.Json.Nodes;
using PiSharp.Core.Agent;
using PiSharp.Core.Messages;
using PiSharp.Core.Verification;

namespace PiSharp.Core.Notebook;

/// <summary>Runs one plan-step check and returns the outcome the PlanTool uses to gate completion.
/// <paramref name="store"/> is passed so the runner can record the verify- evidence.</summary>
public delegate Task<(bool Passed, string? Error, string? VerifyRecordId)> PlanCheckRunner(DoneCheck check, string task, NotebookStore store, CancellationToken ct);

/// <summary>A compact plan-step facade; NotebookStore remains the sole source of truth.
/// When a <see cref="PlanCheckRunner"/> is supplied (PT3), <c>done</c> on a check-gated step runs the
/// harness verifier before completing: pass → completed with a verify- record as sourceId; fail → the step
/// stays open and the first error is returned. Without a runner (PT2 behaviour), <c>done</c> completes
/// unconditionally (unchecked steps render as ✓? in the briefing).</summary>
public sealed class PlanTool(NotebookStore store, PlanCheckRunner? checkRunner = null) : IAgentTool
{
    /// <summary>What the tool says when a step is done: a short reflection before the next one (digital-scribe, 2026-10-26:
    /// "how do we enable more creativity and reasoning around the plan steps"; reflection after each step and a risk check
    /// before the next, as in AdaPlanner and the "anticipatory reflection" work).</summary>
    public const string Reflection = "Before the next step, think briefly: what did this step teach you? Does the rest of the plan still "
        + "fit, or should steps be added, dropped or reordered (plan op=set with why)? Is there a better way to the goal? For the next "
        + "step: what could go wrong, and what is your fallback?";

    /// <summary>What the tool says when a step is blocked.</summary>
    public const string Unblock = "If this way has failed twice, change the approach instead of trying it again: plan op=set with the "
        + "new way and why. If someone else must act, say who and what in a note on the work item.";

    /// <summary>Told after every change with the task's steps in order and the reason given (op=set's why); the factory seat
    /// puts them into the epic's doc plan. Its failures never fail the tool.</summary>
    public Func<string, IReadOnlyList<JsonObject>, string?, CancellationToken, Task>? Changed { get; init; }

    public string Name => "plan";
    public string Label => Name;
    public ToolExecutionMode? ExecutionMode => ToolExecutionMode.Sequential;
    public string Description => "Your working plan: set (the whole plan at once), add, done, block, or show. Use the epic's slug as task: the steps "
        + "then go into the epic's doc plan by themselves. Each step says what it must settle (title) and why (content), not how. "
        + "done takes settled (what the step settled); set takes why when you change the plan. Statuses: in_progress, done, pending; "
        + "in set a step without status keeps it, and a done step given pending or in_progress is reopened. done and block without id "
        + "take the current step. "
        + "At most 20 steps; split into subplans. A step with a check field (file:path or grep:pattern:path) must pass its check before done completes it.";
    public JsonObject Parameters => JsonNode.Parse("""
        {"type":"object","properties":{"op":{"type":"string","enum":["set","add","done","block","show"]},"task":{"type":"string"},"why":{"type":"string"},"settled":{"type":"string"},"id":{"type":"string"},"stepId":{"type":"string"},"taskId":{"type":"string"},"step":{"type":"string"},"title":{"type":"string"},"content":{"type":"string"},"check":{"type":"string"},"status":{"type":"string"},"steps":{"type":"array","items":{"type":"object","properties":{"id":{"type":"string"},"stepId":{"type":"string"},"taskId":{"type":"string"},"step":{"type":"string"},"title":{"type":"string"},"content":{"type":"string"},"check":{"type":"string"},"status":{"type":"string"}}}}},"required":["op","task"]}
        """)!.AsObject();

    public async Task<AgentToolResult> ExecuteAsync(string toolCallId, JsonObject arguments, Action<AgentToolResult>? onUpdate, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var op = Str(arguments, "op");
        var task = Str(arguments, "task") ?? Str(arguments, "taskId") ?? throw new ArgumentException("task is required.");
        JsonNode result;
        switch (op)
        {
            case "set":
            {
                if (arguments["steps"] is not JsonArray input) throw new ArgumentException("set requires steps.");
                if (input.Count > 20) throw new ArgumentException("A plan is limited to 20 steps; split into subplans.");
                var steps = input.Select((node, index) =>
                {
                    var row = node as JsonObject ?? throw new ArgumentException("Each step must be an object.");
                    var text = Str(row, "step") ?? Str(row, "title") ?? Str(row, "content") ?? throw new ArgumentException("Each step needs step, title, or content.");
                    var id = Str(row, "id") ?? Str(row, "stepId") ?? Str(row, "taskId") ?? $"plan-{Safe(task)}-{index + 1:00}";
                    // No status: the step keeps what it had (a finished one stays done); one given reopens or closes it.
                    var status = Str(row, "status") is { Length: > 0 } given ? NormalizeStatus(given) : "";
                    return (id, text, Str(row, "title") ?? text, status, (double)index);
                }).ToArray();
                result = new JsonArray(store.SetPlan(task, steps).Select(ToJson).ToArray());
                break;
            }
            case "add":
            {
                var text = Str(arguments, "step") ?? Str(arguments, "title") ?? Str(arguments, "content") ?? throw new ArgumentException("add requires step, title, or content.");
                var check = Str(arguments, "check");
                var existing = store.List(task, "plan-step").Where(x => x["status"]!.GetValue<string>() == "open").ToArray();
                if (existing.Length >= 20) throw new ArgumentException("A plan is limited to 20 steps; split into subplans.");
                var id = Str(arguments, "id") ?? Str(arguments, "stepId") ?? Str(arguments, "taskId") ?? $"plan-{Safe(task)}-{existing.Length + 1:00}";
                // Add the step with an optional check via a direct Write, then the snapshot is consistent.
                var status = NormalizeStatus(Str(arguments, "status"));
                var mapped = status switch { "done" => "completed", "in_progress" => "open", _ => "open" };
                var row = store.Write(id, text, title: Str(arguments, "title") ?? text, kind: "plan-step", task: task,
                    status: mapped, order: existing.Length, check: check).Record;
                result = ToJson(row);
                break;
            }
            case "done":
            {
                var id = Str(arguments, "id") ?? Str(arguments, "stepId") ?? Str(arguments, "taskId");
                var step = ResolveStepForDone(store, task, id);
                var check = store.PlanStepCheck(step["id"]!.GetValue<string>());
                if (check is { Length: > 0 } && checkRunner is not null)
                {
                    var doneCheck = PlanCheck.Parse(check, task);
                    if (doneCheck is null)
                        throw new ArgumentException($"Step {step["id"]} has a check '{check}' but it could not be parsed; fix the check or use an inline file:/grep: predicate.");
                    var (passed, error, verifyId) = await checkRunner(doneCheck, task, store, cancellationToken);
                    if (!passed)
                    {
                        result = new JsonObject
                        {
                            ["ok"] = false,
                            ["step"] = step["id"]!.GetValue<string>(),
                            ["error"] = error ?? "check failed",
                        };
                        break;
                    }
                    var completed = verifyId is not null
                        ? store.CompletePlanStepWithEvidence(task, step["id"]!.GetValue<string>(), verifyId)
                        : store.CompletePlanStep(task, step["id"]!.GetValue<string>());
                    Settle(completed, task, Str(arguments, "settled"));
                    result = new JsonObject
                    {
                        ["ok"] = true,
                        ["step"] = completed["id"]!.GetValue<string>(),
                        ["status"] = "completed",
                        ["sourceId"] = verifyId,
                        ["next"] = Reflection,
                    };
                    break;
                }
                // No check, or no runner: complete unconditionally (unchecked steps render as ✓?).
                var plain = Settle(store.CompletePlanStep(task, id), task, Str(arguments, "settled"));
                result = ((JsonObject)ToJson(plain).DeepClone());
                result["next"] = Reflection;
                break;
            }
            case "block":
            {
                var id = Str(arguments, "id") ?? Str(arguments, "stepId") ?? Str(arguments, "taskId");
                result = (JsonObject)ToJson(store.BlockPlanStep(task, id)).DeepClone();
                result["next"] = Unblock;
                break;
            }
            case "show": result = new JsonArray(store.List(task, "plan-step").OrderBy(x => x["order"]?.GetValue<double>() ?? double.MaxValue).Select(ToJson).ToArray()); break;
            default: throw new ArgumentException("Unknown plan operation; use set, add, done, block, or show.");
        }
        if (op is not "show" && Changed is { } changed)
        {
            try
            {
                var steps = store.List(task, "plan-step").OrderBy(x => x["order"]?.GetValue<double>() ?? double.MaxValue)
                    .ThenBy(x => x["id"]!.GetValue<string>(), StringComparer.Ordinal).ToArray();
                await changed(task, steps, op == "set" ? Str(arguments, "why") : null, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { /* the plan stands; its copy catches up on the next change */ }
        }
        return AgentToolResult.FromText(result.ToJsonString(), result);
    }

    /// <summary>A done step keeps what it settled: its text gains a "Settled:" line (kept in the notebook and the epic's doc plan).</summary>
    private JsonObject Settle(JsonObject step, string task, string? settled)
    {
        if (settled is null) return step;
        var text = step["text"]!.GetValue<string>();
        return store.Write(step["id"]!.GetValue<string>(), text + "\nSettled: " + settled, title: step["title"]?.GetValue<string>(), kind: "plan-step",
            task: task, status: "completed", order: step["order"]?.GetValue<double>()).Record;
    }

    private static JsonObject ResolveStepForDone(NotebookStore store, string task, string? id)
    {
        var step = id is null
            ? store.List(task, "plan-step").Where(x => x["status"]!.GetValue<string>() == "open")
                .OrderBy(x => x["order"]?.GetValue<double>() ?? double.MaxValue)
                .ThenBy(x => x["id"]!.GetValue<string>(), StringComparer.Ordinal).FirstOrDefault()
            : store.Get(id);
        if (step is null || step["kind"]!.GetValue<string>() != "plan-step" || step["task"]!.GetValue<string>() != task)
            throw new KeyNotFoundException(id ?? $"open plan step for {task}");
        if (step["status"]!.GetValue<string>() != "open") throw new InvalidOperationException("Only an open plan step can be marked done.");
        return step;
    }

    private static string? Str(JsonObject o, string key) => o[key] is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s) ? s.Trim() : null;
    /// <summary>A step's status in the words the tool takes and the words the coder text and the epic's doc plan use (todo,
    /// doing, done): pi3, 2026-10-26 16:28Z, wrote todo/doing as the rule says and was refused.</summary>
    private static string NormalizeStatus(string? s) => s?.Trim().ToLowerInvariant() switch
    {
        null or "" or "pending" or "todo" or "to do" or "to-do" or "not_started" => "pending",
        "in_progress" or "in-progress" or "in progress" or "open" or "doing" or "active" or "current" => "in_progress",
        "done" or "completed" or "complete" or "finished" => "done",
        var other when other.StartsWith("done", StringComparison.Ordinal) => "done",
        _ => throw new ArgumentException("Step status must be pending (todo), in_progress (doing) or done."),
    };
    private static string Safe(string task) => new(task.Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '-').ToArray());
    private static JsonObject ToJson(JsonObject row) => row;
}
