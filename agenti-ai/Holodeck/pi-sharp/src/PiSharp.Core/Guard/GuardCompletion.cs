using System.Text.Json.Nodes;
using PiSharp.Core.Agent;
using PiSharp.Core.Mcp;
using PiSharp.Core.Notebook;

namespace PiSharp.Core.Guard;

/// <summary>Completion rules: a work item may not move to <c>review</c> or <c>done</c> until the
/// task has evidence the completion condition was met (guard-rules-4).
///
/// A completion rule is declared in a <c>```completion```</c> fence in the work-item's spec,
/// next to the <c>done-check</c> fence (verify-loop-1):
/// <c>{"testRun": true}</c> requires at least one harness verification run for the task
/// (a <c>verify-&lt;task&gt;-&lt;n&gt;</c> check record) before review/done. The check is
/// deterministic and harness-authored - the model cannot satisfy it by writing evidence, the
/// trust boundary as the harness's own verification records.
///
/// Rules come from the work-item spec and the local settings. Rules are evaluated:
/// <list type="bullet">
/// <item>at <c>kb_transition</c> time - <see cref="GuardTransitionGate"/> wraps the tool and
/// refuses the move with a named rule when its condition is unmet;</item>
/// <item>in <c>ShouldStopAfterTurn</c> - <see cref="GuardCompletionGate"/> keeps the run alive
/// when the task would stop with the condition unmet, so the model gets one more turn to
/// satisfy it (a test run, in the default case). Bounded by a nudge cap.</item>
/// </list>
/// Both share <see cref="Evaluate"/> so the refusal message is the same in both places.</summary>
public static class GuardCompletion
{
    private static readonly System.Text.RegularExpressions.Regex CompletionFence =
        new(@"```completion\s*(.*?)```",
            System.Text.RegularExpressions.RegexOptions.Singleline
            | System.Text.RegularExpressions.RegexOptions.CultureInvariant
            | System.Text.RegularExpressions.RegexOptions.NonBacktracking,
            TimeSpan.FromMilliseconds(50));

    /// <summary>Parses a <c>```completion```</c> fence body (JSON) into a rule. Returns null when
    /// the fence is absent or malformed: a completion rule is an explicit declaration, never
    /// inferred.</summary>
    public static GuardCompletionRule? ParseRule(string? document)
    {
        if (string.IsNullOrWhiteSpace(document)) return null;
        foreach (System.Text.RegularExpressions.Match block in CompletionFence.Matches(document))
        {
            try
            {
                var obj = JsonNode.Parse(block.Groups[1].Value)?.AsObject();
                if (obj?["testRun"]?.GetValue<bool>() == true)
                    return new GuardCompletionRule("test-run-before-review");
            }
            catch (System.Text.Json.JsonException) { /* malformed fence: no rule */ }
        }
        return null;
    }

    /// <summary>Checks one rule against a task's evidence. Returns null when the rule is met or
    /// not applicable, otherwise the refusal message (named rule).</summary>
    public static string? Evaluate(string? taskKey, NotebookStore notebook, GuardCompletionRule rule)
    {
        if (taskKey is not { Length: > 0 } || rule.Condition is not CompletionCondition.TestRunBeforeReview)
            return null;
        var prefix = PiSharp.Core.Verification.VerificationRecords.IdPrefix(taskKey);
        foreach (var row in notebook.List(task: taskKey, kind: "check"))
        {
            if (row["id"]?.GetValue<string>()?.StartsWith(prefix, StringComparison.Ordinal) != true) continue;
            if (row["text"]?.GetValue<string>() is not { } text) continue;
            try
            {
                // A harness verification ran for this task: the record always carries `passed`.
                if (JsonNode.Parse(text)?["passed"]?.GetValue<bool>() is bool)
                    return null;
            }
            catch (System.Text.Json.JsonException) { }
        }
        return "Completion rule '" + rule.Id + "' not met: no harness test run for task '" + taskKey
            + "' before moving to review/done. Run the tests and re-verify.";
    }
}

/// <summary>One declared completion rule. <c>test-run-before-review</c> requires a harness
/// verification run for the task before <c>kb_transition</c> to review/done.</summary>
public sealed record GuardCompletionRule(string Id,
    CompletionCondition Condition = CompletionCondition.TestRunBeforeReview);

public enum CompletionCondition { TestRunBeforeReview }

/// <summary>The <c>kb_transition</c> half of the completion rules: wraps the MCP tool and
/// refuses review/done moves while a declared rule is unmet. Passes everything else through
/// untouched.</summary>
public sealed class GuardTransitionGate : IAgentTool
{
    private readonly IAgentTool _inner;
    private readonly NotebookStore _notebook;
    private readonly Func<GuardCompletionRule> _rule;
    private readonly Func<string?> _taskKey;

    public GuardTransitionGate(IAgentTool inner, NotebookStore notebook,
        Func<GuardCompletionRule> rule, Func<string?> taskKey)
    {
        if (inner is not IRemoteMcpAgentTool { RemoteName: "kb_transition" })
            throw new ArgumentException("GuardTransitionGate must wrap MCP kb_transition.", nameof(inner));
        _inner = inner;
        _notebook = notebook;
        _rule = rule;
        _taskKey = taskKey;
    }
    public string Name => _inner.Name;
    public string Label => _inner.Label;
    public string Description => _inner.Description;
    public JsonObject Parameters => _inner.Parameters;
    public ToolExecutionMode? ExecutionMode => _inner.ExecutionMode;
    public TimeSpan? MaxDuration => _inner.MaxDuration;
    public JsonNode? PrepareArguments(JsonNode? arguments) => _inner.PrepareArguments(arguments);

    public async Task<AgentToolResult> ExecuteAsync(string toolCallId, JsonObject arguments,
        Action<AgentToolResult>? onUpdate, CancellationToken cancellationToken)
    {
        // kb_transition's own name is to_status: the argument check renames a model's "status" to it before any tool runs, so
        // reading "status" alone left this gate silent from the rename on (audit 2026-10-26).
        var status = (arguments["to_status"] ?? arguments["status"])?.GetValue<string>();
        if (status is "review" or "done")
        {
            var task = _taskKey();
            if (task is not null)
            {
                var refusal = GuardCompletion.Evaluate(task, _notebook, _rule());
                if (refusal is not null) return AgentToolResult.Error(refusal);
            }
        }
        return await _inner.ExecuteAsync(toolCallId, arguments, onUpdate, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>The <c>ShouldStopAfterTurn</c> half of the completion rules: keeps the run alive
/// when the task would stop while a completion rule is unmet, so the model gets one more turn
/// to satisfy it. Bounded: at most <paramref name="maxNudges"/> reminders per task before it
/// stops anyway (a completion rule steers; it must not loop an unattended seat).</summary>
public sealed class GuardCompletionGate
{
    private readonly NotebookStore _notebook;
    private readonly Func<string> _taskKey;
    private readonly GuardCompletionRule[] _rules;
    private readonly int _maxNudges;
    private readonly object _gate = new();
    private string? _task;
    private int _nudges;
    private string? _reminder;

    public GuardCompletionGate(NotebookStore notebook, Func<string> taskKey,
        IEnumerable<GuardCompletionRule> rules, int maxNudges = 3)
    {
        _notebook = notebook;
        _taskKey = taskKey;
        _rules = rules.ToArray();
        _maxNudges = Math.Max(1, maxNudges);
    }

    /// <summary>True when the run may stop. False means the loop should continue; the pending
    /// reminder is available from <see cref="TakeReminder"/>.</summary>
    public bool MayStop()
    {
        var task = _taskKey();
        if (task is not { Length: > 0 }) return true;
        string? unmet;
        lock (_gate)
        {
            if (!string.Equals(_task, task, StringComparison.Ordinal))
            {
                _task = task;
                _nudges = 0;
            }
            unmet = _rules.Select(r => GuardCompletion.Evaluate(task, _notebook, r)).FirstOrDefault(m => m is not null);
            if (unmet is null) return true;
            if (_nudges >= _maxNudges) return true; // bounded: steer, do not loop
            _nudges++;
        }
        lock (_gate) { _reminder = unmet; }
        return false;
    }

    /// <summary>The reminder to inject after a <see cref="MayStop"/> refusal; null when none
    /// is pending (delivered once per refusal).</summary>
    public string? TakeReminder()
    {
        lock (_gate)
        {
            var reminder = _reminder;
            _reminder = null;
            return reminder;
        }
    }
}
