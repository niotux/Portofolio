using System.Text.Json.Nodes;
using PiSharp.Core.Agent;
using PiSharp.Core.Guard;
using PiSharp.Core.Mcp;
using PiSharp.Core.Messages;
using PiSharp.Core.Notebook;
using PiSharp.Core.Verification;

namespace PiSharp.Tests.Agent;

/// <summary>Guard rules slice 4 (guard-rules-4): per-rule fire/override/confirm counters with
/// the deterministic retirement rules, the <c># guard-ok:</c> override marker and the
/// per-task block cap, and the test-run completion gate on <c>kb_transition</c>.</summary>
public sealed class GuardCountersTests : IDisposable
{
    private readonly string _root;
    private readonly NotebookStore _notebook;

    public GuardCountersTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "guard-counters-tests-" + Guid.NewGuid().ToString("N"));
        _notebook = new(_root, "test-seat");
    }

    public void Dispose()
    {
        _notebook.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private static ToolCallContent Call(string name, string json) =>
        new("call-1", name, JsonNode.Parse(json)?.AsObject()!);

    // ── a) counters + retirement ─────────────────────────────────────────────

    [Fact]
    public void Overrides_outnumbering_confirms_retire_the_rule_in_that_call()
    {
        // Retirement is one-way: the rule retires within the very call where Overridden reaches
        // RetireOverrides (2) while outnumbering the confirms (1). It never un-retires.
        GuardCounters.RecordConfirm(_notebook, "no-force-push", "wi-1");
        var first = GuardCounters.RecordOverride(_notebook, "no-force-push", "legit incident", "wi-1");
        Assert.False(first.Retired); // overridden 1, confirmed 1
        var second = GuardCounters.RecordOverride(_notebook, "no-force-push", "still fine", "wi-1");
        Assert.True(second.Retired); // overridden 2 > confirmed 1 -> retired in this call
        Assert.True(GuardCounters.IsRetired(_notebook, "no-force-push"));
        var third = GuardCounters.RecordOverride(_notebook, "no-force-push", "again", "wi-1");
        Assert.True(GuardCounters.IsRetired(_notebook, "no-force-push")); // one-way: still retired
        Assert.Equal(3, GuardCounters.Counters(_notebook, "no-force-push").Overridden);
    }

    [Fact]
    public void Confirms_keep_the_rule_alive_over_overrides()
    {
        GuardCounters.RecordConfirm(_notebook, "no-force-push", "wi-1");
        GuardCounters.RecordConfirm(_notebook, "no-force-push", "wi-1");
        GuardCounters.RecordConfirm(_notebook, "no-force-push", "wi-1");
        var second = GuardCounters.RecordOverride(_notebook, "no-force-push", "r", "wi-1");
        Assert.False(second.Retired); // overridden 2 is not > confirmed 3
    }

    [Fact]
    public void Never_confirmed_rule_with_no_recent_fire_retires_in_the_sweep()
    {
        var now = new DateTimeOffset(2026, 9, 29, 0, 0, 0, TimeSpan.Zero);
        // stale rule: fired 40 days ago, never confirmed
        GuardCounters.RecordFire(_notebook, "stale-rule", "wi-1", now - TimeSpan.FromDays(40));
        // fresh rule: fired 2 days ago
        GuardCounters.RecordFire(_notebook, "fresh-rule", "wi-1", now - TimeSpan.FromDays(2));
        // confirmed rule with an old fire: stays
        GuardCounters.RecordFire(_notebook, "confirmed-stale", "wi-1", now - TimeSpan.FromDays(40));
        GuardCounters.RecordConfirm(_notebook, "confirmed-stale", "wi-1");
        // never-fired rule: no record yet -> the sweep cannot see it (it has no counters row)
        GuardCounters.RecordFire(_notebook, "never-fired", "wi-1", now - TimeSpan.FromDays(40));

        var retired = GuardCounters.SweepStale(_notebook, now);
        Assert.Contains("stale-rule", retired);
        Assert.Contains("never-fired", retired);
        Assert.DoesNotContain("fresh-rule", retired);
        Assert.DoesNotContain("confirmed-stale", retired);
        Assert.True(GuardCounters.IsRetired(_notebook, "stale-rule"));
        Assert.Equal("never confirmed and no fire in 30 days",
            GuardCounters.Counters(_notebook, "stale-rule").RetireReason);
    }

    [Fact]
    public void Rule_history_rows_exist_for_counters_and_events()
    {
        GuardCounters.RecordFire(_notebook, "hist-rule", "wi-1");
        GuardCounters.RecordOverride(_notebook, "hist-rule", "because", "wi-1");

        var countersRow = _notebook.Get("guard-counters-hist-rule");
        Assert.NotNull(countersRow);
        Assert.Equal("guard", countersRow?["kind"]?.GetValue<string>());
        Assert.NotNull(countersRow?["text"]?.GetValue<string>());

        var events = _notebook.List().Where(r =>
            r["id"]!.GetValue<string>().StartsWith("guard-event-hist-rule-", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(events); // at least the override event
    }

    [Fact]
    public void Retired_rule_never_blocks_or_warns_in_the_guard()
    {
        // Retire on the override path: with two confirms, the third override retires the rule.
        GuardCounters.RecordConfirm(_notebook, "no-force-push", "wi-1");
        GuardCounters.RecordConfirm(_notebook, "no-force-push", "wi-1");
        GuardCounters.RecordOverride(_notebook, "no-force-push", "r", "wi-1");
        GuardCounters.RecordOverride(_notebook, "no-force-push", "r", "wi-1");
        GuardCounters.RecordOverride(_notebook, "no-force-push", "r", "wi-1");
        Assert.True(GuardCounters.IsRetired(_notebook, "no-force-push"));

        var guard = new ToolGuard(
            [new GuardRule("no-force-push", @"\bgit\s+push\s+(-f|--force)\b", "No force push.", GuardPoint.ToolCall, GuardAction.Warn)],
            GuardMode.Enforce, log: _ => { }, counters: _notebook, taskKey: () => "wi-1");
        Assert.Null(guard.Check(Call("bash", "{\"command\":\"git push --force origin main\"}")));
    }

    // ── override marker + block cap ───────────────────────────────────────────

    [Fact]
    public void Guard_ok_marker_exempts_the_call_and_records_an_override()
    {
        var offending = Call("bash", "{\"command\":\"git push --force origin main # guard-ok: incident recovery\"}");
        // The marker takes the first token after the colon; a longer phrase is truncated to it.
        Assert.Equal("incident", ToolGuard.GuardOverrideMarker(offending.Arguments.ToJsonString()));

        var guard = new ToolGuard(
            [new GuardRule("no-force-push", @"\bgit\s+push\s+(-f|--force)\b", "No force push.", GuardPoint.ToolCall, GuardAction.Block)],
            GuardMode.Enforce, log: _ => { }, counters: _notebook, taskKey: () => "wi-1");
        Assert.Null(guard.Check(offending)); // exempted in-band
        Assert.Equal(1, GuardCounters.Counters(_notebook, "no-force-push").Overridden);
        // and the same call still blocks without the marker
        Assert.NotNull(guard.Check(Call("bash", "{\"command\":\"git push --force origin main\"}")));
    }

    [Fact]
    public void Marker_reason_is_capped_at_eighty_chars()
    {
        var longReason = new string('x', 120);
        var call = Call("bash", "{\"command\":\"git push --force origin main # guard-ok: " + longReason + "\"}");
        Assert.Equal(80, ToolGuard.GuardOverrideMarker(call.Arguments.ToJsonString())!.Length);
    }

    [Fact]
    public void Block_cap_stops_further_refusals_in_the_task_with_one_ops_line()
    {
        var logs = new List<string>();
        var guard = new ToolGuard(
            [new GuardRule("no-force-push", @"\bgit\s+push\s+(-f|--force)\b", "No force push.", GuardPoint.ToolCall, GuardAction.Block)],
            GuardMode.Enforce, log: logs.Add, counters: _notebook, taskKey: () => "wi-1", maxBlocksPerRulePerTask: 2);
        var call = Call("bash", "{\"command\":\"git push --force origin main\"}");
        Assert.NotNull(guard.Check(call)); // block 1
        Assert.NotNull(guard.Check(call)); // block 2 -> cap hit
        Assert.Null(guard.Check(call));    // cap reached: degraded
        Assert.Null(guard.Check(call));    // stays degraded
        var warnings = logs.Where(l => l.Contains("stopped blocking", StringComparison.Ordinal)).ToList();
        Assert.Single(warnings); // one ops line, not one per call
    }

    [Fact]
    public void Block_cap_resets_for_a_new_task()
    {
        var task = "wi-1";
        var guard = new ToolGuard(
            [new GuardRule("no-force-push", @"\bgit\s+push\s+(-f|--force)\b", "No force push.", GuardPoint.ToolCall, GuardAction.Block)],
            GuardMode.Enforce, counters: _notebook, taskKey: () => task, maxBlocksPerRulePerTask: 1);
        var call = Call("bash", "{\"command\":\"git push --force origin main\"}");
        Assert.NotNull(guard.Check(call)); // block 1 for wi-1
        Assert.Null(guard.Check(call)); // cap (1 block) reached: degraded
        task = "wi-2";
        Assert.NotNull(guard.Check(call)); // new task: blocks again
        Assert.Null(guard.Check(call)); // cap (1 block) reached: degraded again
    }

    [Fact]
    public void Firing_a_rule_records_its_fire_counter()
    {
        var guard = new ToolGuard(
            [new GuardRule("no-force-push", @"\bgit\s+push\s+(-f|--force)\b", "No force push.", GuardPoint.ToolCall, GuardAction.Block)],
            GuardMode.Shadow, counters: _notebook, taskKey: () => "wi-1");
        guard.Check(Call("bash", "{\"command\":\"git push --force origin main\"}"));
        var state = GuardCounters.Counters(_notebook, "no-force-push");
        Assert.NotNull(state.LastFireUtc);
    }

    // ── b) completion rules on kb_transition and the stop gate ───────────────

    private sealed class FakeTransitionTool : IRemoteMcpAgentTool
    {
        public string Name => "kb_transition";
        public string RemoteName => "kb_transition";
        public string Label => "kb_transition";
        public string Description => "fake";
        public JsonObject Parameters => new();
        public ToolExecutionMode? ExecutionMode => null;
        public JsonNode? PrepareArguments(JsonNode? arguments) => arguments;
        public int Calls;
        public Task<AgentToolResult> ExecuteAsync(string toolCallId, JsonObject arguments,
            Action<AgentToolResult>? onUpdate, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(AgentToolResult.FromText("{}"));
        }
    }

    private static VerificationReport Report(string task, bool passed) =>
        new(task,
            [new VerificationResult("dc-1", passed, passed ? 0 : 1, passed ? "" : "boom",
                new JsonObject(), new JsonObject(), false, "abc12345678901234567890123456789012345678",
                DateTimeOffset.UtcNow)],
            passed, passed ? null : "dc-1 failed");

    [Fact]
    public async Task Transition_to_review_is_refused_without_a_harness_test_run()
    {
        const string task = "wi-42";
        var tool = new FakeTransitionTool();
        var gate = new GuardTransitionGate(tool, _notebook,
            () => new GuardCompletionRule("test-run-before-review"), () => task);

        var args = new JsonObject { ["work_item"] = task, ["to_status"] = "review" }; // as the argument check hands it over
        var refusal = await gate.ExecuteAsync("call-1", args, null, CancellationToken.None);
        Assert.True(refusal.IsError);
        Assert.Contains("test-run-before-review", refusal.Text, StringComparison.Ordinal);
        Assert.Equal(0, tool.Calls); // the MCP call never ran

        // Non-final transitions pass through untouched.
        var inProgress = await gate.ExecuteAsync("call-2", new JsonObject { ["to_status"] = "in_progress" }, null, CancellationToken.None);
        Assert.False(inProgress.IsError);
        Assert.Equal(1, tool.Calls);
    }

    [Fact]
    public async Task Transition_is_allowed_after_a_harness_test_run()
    {
        const string task = "wi-42";
        VerificationRecords.Record(_notebook, Report(task, passed: true));
        var tool = new FakeTransitionTool();
        var gate = new GuardTransitionGate(tool, _notebook,
            () => new GuardCompletionRule("test-run-before-review"), () => task);

        var result = await gate.ExecuteAsync("call-1", new JsonObject { ["status"] = "done" }, null, CancellationToken.None);
        Assert.False(result.IsError);
        Assert.Equal(1, tool.Calls);
    }

    [Fact]
    public async Task Gate_refuses_to_wrap_a_tool_that_is_not_kb_transition()
    {
        var notTheTool = new FakeTransitionTool();
        var other = new OtherRemoteTool();
        Assert.Throws<ArgumentException>(() => new GuardTransitionGate(other, _notebook,
            () => new GuardCompletionRule("test-run-before-review"), () => "wi-42"));
        _ = notTheTool;
    }

    private sealed class OtherRemoteTool : IRemoteMcpAgentTool
    {
        public string Name => "notebook";
        public string RemoteName => "notebook";
        public string Label => "notebook";
        public string Description => "fake";
        public JsonObject Parameters => new();
        public ToolExecutionMode? ExecutionMode => null;
        public JsonNode? PrepareArguments(JsonNode? arguments) => arguments;
        public Task<AgentToolResult> ExecuteAsync(string toolCallId, JsonObject arguments,
            Action<AgentToolResult>? onUpdate, CancellationToken cancellationToken)
            => Task.FromResult(AgentToolResult.FromText("{}"));
    }

    [Fact]
    public void Stop_gate_keeps_the_run_alive_while_unmet_and_is_bounded()
    {
        const string task = "wi-42";
        var gate = new GuardCompletionGate(_notebook, () => task,
            [new GuardCompletionRule("test-run-before-review")], maxNudges: 2);

        // Each refusal is one nudge; the reminder is injected once per refused stop
        // (TakeReminder returns it at most once after the refusal that produced it).
        Assert.False(gate.MayStop());
        Assert.NotNull(gate.TakeReminder());
        Assert.Null(gate.TakeReminder()); // taken: the loop's PrepareNextTurn delivers it
        Assert.False(gate.MayStop());
        Assert.NotNull(gate.TakeReminder()); // second and final nudge
        Assert.True(gate.MayStop()); // bounded: at most 2 nudges, then it may stop

        VerificationRecords.Record(_notebook, Report(task, passed: true));
        Assert.True(gate.MayStop()); // satisfied: stops even for a new task
    }

    [Fact]
    public void Parse_rule_reads_the_completion_fence_and_rejects_malformed_bodies()
    {
        Assert.NotNull(GuardCompletion.ParseRule("spec\n```completion\n{\"testRun\": true}\n```\n"));
        Assert.Null(GuardCompletion.ParseRule("spec without any fence"));
        Assert.Null(GuardCompletion.ParseRule("```completion\nnot json\n```"));
        Assert.Null(GuardCompletion.ParseRule("```completion\n{\"testRun\": false}\n```"));
    }

    [Fact]
    public void Evaluate_passes_for_other_tasks_but_not_the_unverified_one()
    {
        VerificationRecords.Record(_notebook, Report("wi-other", passed: true));
        var rule = new GuardCompletionRule("test-run-before-review");
        Assert.Null(GuardCompletion.Evaluate("wi-other", _notebook, rule));
        Assert.NotNull(GuardCompletion.Evaluate("wi-42", _notebook, rule));
        Assert.Null(GuardCompletion.Evaluate(null, _notebook, rule)); // no task: not applicable
    }
}
