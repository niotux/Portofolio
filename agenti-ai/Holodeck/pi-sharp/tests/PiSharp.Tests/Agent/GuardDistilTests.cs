using System.Text.Json.Nodes;
using PiSharp.Core.Guard;
using PiSharp.Core.Notebook;
using PiSharp.Core.Messages;

namespace PiSharp.Tests.Agent;

public sealed class GuardDistilTests : IDisposable
{
    private readonly string _root;
    private readonly NotebookStore _notebook;

    public GuardDistilTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "guard-distil-tests-" + Guid.NewGuid().ToString("N"));
        _notebook = new(_root, "test-seat");
    }

    public void Dispose()
    {
        _notebook.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    // ── Replay gate ──────────────────────────────────────────────────────────

    [Fact]
    public void Replay_rejects_candidate_that_blocks_no_offending_call()
    {
        var offending = new[] { new RecordedCall("bash", "{\"command\":\"git push origin master\"}", "main", "abc123") };
        var good = new[] { new RecordedCall("bash", "{\"command\":\"git status\"}", "main", "abc123") };
        // A pattern that matches neither
        var result = GuardReplay.Evaluate("test-rule", "bash\\s+\\.\\.\\.unrelated", offending, good);
        Assert.False(result.Passed);
        Assert.Equal("blocks none of the recorded offending calls", result.FailureReason);
    }

    [Fact]
    public void Replay_rejects_candidate_that_blocks_a_good_call()
    {
        var offending = new[] { new RecordedCall("bash", "{\"command\":\"git push origin master\"}", "main", "abc123") };
        // Good call that also matches the pattern
        var good = new[]
        {
            new RecordedCall("bash", "{\"command\":\"git push origin feature\"}", "main", "abc123"),
            new RecordedCall("bash", "{\"command\":\"git status\"}", "main", "abc123"),
        };
        // Pattern that matches "git push origin" — blocks both offending and good
        var result = GuardReplay.Evaluate("test-rule", "bash.*git push origin", offending, good);
        Assert.False(result.Passed);
        Assert.NotEmpty(result.BlockedGood);
        Assert.Contains("recent successful", result.FailureReason);
    }

    [Fact]
    public void Replay_accepts_candidate_that_blocks_offending_only()
    {
        var offending = new[] { new RecordedCall("bash", "{\"command\":\"git push origin master\"}", "main", "abc123") };
        var good = new[]
        {
            new RecordedCall("bash", "{\"command\":\"git status\"}", "main", "abc123"),
            new RecordedCall("edit", "{\"path\":\"src/foo.cs\"}", "main", "abc123"),
        };
        // Pattern that only matches "push origin master"
        var result = GuardReplay.Evaluate("test-rule", "bash.*git push origin master", offending, good);
        Assert.True(result.Passed);
        Assert.Empty(result.BlockedGood);
        Assert.NotEmpty(result.BlockedOffending);
    }

    [Fact]
    public void Replay_rejects_candidate_with_invalid_regex()
    {
        var offending = new[] { new RecordedCall("bash", "{\"command\":\"x\"}", null, null) };
        var good = Array.Empty<RecordedCall>();
        var result = GuardReplay.Evaluate("test-rule", "[invalid(", offending, good);
        Assert.False(result.Passed);
        Assert.Contains("regex", result.FailureReason);
    }

    // ── GoodCallsFromNotebook ────────────────────────────────────────────────

    [Fact]
    public void GoodCallsFromNotebook_returns_guard_call_records()
    {
        // Write 3 guard-call records
        for (int i = 1; i <= 3; i++)
        {
            _notebook.Write("guard-call-t1-" + i,
                System.Text.Json.JsonSerializer.Serialize(new { name = "bash", arguments = "{\"command\":\"x\" + " + i + "}" }),
                title: "test call", tags: new[] { "guard" }, kind: "guard-call");
        }
        var calls = GuardReplay.GoodCallsFromNotebook(_notebook);
        Assert.Equal(3, calls.Count);
        Assert.All(calls, c => Assert.Equal("bash", c.Name));
    }

    [Fact]
    public void GoodCallsFromNotebook_caps_at_200()
    {
        for (int i = 1; i <= 250; i++)
        {
            _notebook.Write("guard-call-t1-" + i,
                System.Text.Json.JsonSerializer.Serialize(new { name = "bash", arguments = "{\"command\":\"x\"}" }),
                title: "test call", tags: new[] { "guard" }, kind: "guard-call");
        }
        var calls = GuardReplay.GoodCallsFromNotebook(_notebook);
        Assert.Equal(GuardReplay.GoodCallWindow, calls.Count);
    }

    [Fact]
    public void GoodCallsFromNotebook_excludes_correction_records()
    {
        // A correction record should NOT contribute to the good-call baseline;
        // only guard-call records do.
        var correctionBody = new JsonObject
        {
            ["calls"] = new JsonArray
            {
                new JsonObject { ["name"] = "bash", ["arguments"] = "{\"command\":\"bad\"}" },
                new JsonObject { ["name"] = "bash", ["arguments"] = "{\"command\":\"good\"}" },
            }
        };
        _notebook.Write("correction-msg-1", correctionBody.ToJsonString(),
            title: "correction", tags: new[] { "guard" }, kind: "correction");

        var calls = GuardReplay.GoodCallsFromNotebook(_notebook);
        Assert.Empty(calls);
    }

    // ── GuardLifecycle ───────────────────────────────────────────────────────

    [Fact]
    public void Lifecycle_begin_shadow_and_track_evaluations()
    {
        var lifecycle = GuardLifecycle.BeginShadow(_notebook, "test-rule-1");
        Assert.NotNull(lifecycle);
        Assert.Equal(0, lifecycle.Evaluations);
        Assert.Equal(GuardStatus.Shadow, lifecycle.Status);
        Assert.False(lifecycle.EligibleForActive());

        // Record 19 evaluations — not yet eligible
        for (int i = 0; i < 19; i++) lifecycle.RecordEvaluation(wouldBlock: false);
        Assert.Equal(19, lifecycle.Evaluations);
        Assert.False(lifecycle.EligibleForActive());

        // 20th evaluation — eligible
        lifecycle.RecordEvaluation(wouldBlock: false);
        Assert.Equal(20, lifecycle.Evaluations);
        Assert.True(lifecycle.EligibleForActive());
        Assert.Equal(GuardStatus.Active, lifecycle.Status);
    }

    [Fact]
    public void Lifecycle_false_positive_blocks_promotion()
    {
        var lifecycle = GuardLifecycle.BeginShadow(_notebook, "test-rule-2");
        Assert.NotNull(lifecycle);

        for (int i = 0; i < 19; i++) lifecycle.RecordEvaluation(wouldBlock: false);
        // One false positive on the 20th evaluation
        lifecycle.RecordEvaluation(wouldBlock: true, blockedGoodCall: true);
        Assert.Equal(20, lifecycle.Evaluations);
        Assert.Equal(1, lifecycle.FalsePositives);
        Assert.False(lifecycle.EligibleForActive());
        Assert.Equal(GuardStatus.Shadow, lifecycle.Status);
    }

    [Fact]
    public void Lifecycle_retire_prevents_further_evaluations()
    {
        var lifecycle = GuardLifecycle.BeginShadow(_notebook, "test-rule-3");
        Assert.NotNull(lifecycle);
        lifecycle.RecordEvaluation(wouldBlock: false);
        lifecycle.Retire();
        Assert.Equal(GuardStatus.Retired, lifecycle.Status);
        // After retire, evaluations should not increment
        lifecycle.RecordEvaluation(wouldBlock: false);
        Assert.Equal(1, lifecycle.Evaluations);
    }

    [Fact]
    public void Lifecycle_resume_restores_state()
    {
        var lifecycle = GuardLifecycle.BeginShadow(_notebook, "test-rule-4");
        Assert.NotNull(lifecycle);
        for (int i = 0; i < 5; i++) lifecycle.RecordEvaluation(wouldBlock: false);

        var resumed = GuardLifecycle.Resume(_notebook, "test-rule-4");
        Assert.NotNull(resumed);
        Assert.Equal(5, resumed.Evaluations);
        Assert.Equal(0, resumed.FalsePositives);
    }

    [Fact]
    public void Lifecycle_begin_shadow_returns_null_for_live_rule()
    {
        var first = GuardLifecycle.BeginShadow(_notebook, "test-rule-5");
        Assert.NotNull(first);
        // A second BeginShadow for the same id should return null
        var second = GuardLifecycle.BeginShadow(_notebook, "test-rule-5");
        Assert.Null(second);
    }

    [Fact]
    public void Lifecycle_update_pattern_supersede()
    {
        var lifecycle = GuardLifecycle.BeginShadow(_notebook, "test-rule-6");
        Assert.NotNull(lifecycle);
        lifecycle.RecordEvaluation(wouldBlock: true);
        lifecycle.UpdatePattern("new-pattern-xyz");

        var resumed = GuardLifecycle.Resume(_notebook, "test-rule-6");
        Assert.NotNull(resumed);
        Assert.Equal(1, resumed.Evaluations);
        // Pattern was updated — the record should have it
        var row = _notebook.Get("guard-test-rule-6");
        Assert.NotNull(row);
        var text = row["text"]!.GetValue<string>();
        Assert.Contains("new-pattern-xyz", text);
    }

    // ── GuardDistil.Gate ─────────────────────────────────────────────────────

    [Fact]
    public void Gate_no_corrections_fails_and_writes_note()
    {
        var distil = new GuardDistil(_notebook);
        var result = distil.Gate(new GuardDistil.Candidate("cand-1", "bash.*bad", "test"));
        Assert.False(result.Passed);
        Assert.Equal(GuardResolution.Noop, result.Resolution);
        // A guard-note should have been written
        var notes = _notebook.List(kind: "note");
        Assert.Contains(notes, n => n["id"]!.GetValue<string>()!.StartsWith("guard-note-cand-1"));
    }

    [Fact]
    public void Gate_candidate_blocks_good_call_fails_replay()
    {
        // Set up a correction with an offending call
        var correctionBody = new JsonObject
        {
            ["calls"] = new JsonArray
            {
                new JsonObject { ["name"] = "bash", ["arguments"] = "{\"command\":\"rm -rf /\"}" },
            }
        };
        _notebook.Write("correction-msg-99", correctionBody.ToJsonString(),
            title: "correction", tags: new[] { "guard" }, kind: "correction");

        // A good call that the candidate would also block
        _notebook.Write("guard-call-t1-1",
            System.Text.Json.JsonSerializer.Serialize(new { name = "bash", arguments = "{\"command\":\"rm -rf /tmp\"}" }),
            title: "good call", tags: new[] { "guard" }, kind: "guard-call");

        var distil = new GuardDistil(_notebook);
        // Pattern that matches both "rm -rf /" and "rm -rf /tmp"
        var result = distil.Gate(new GuardDistil.Candidate("cand-2", "bash.*rm -rf /", "test"));
        Assert.False(result.Passed);
        Assert.Contains("recent successful", result.FailureReason!);
    }

    [Fact]
    public void Gate_candidate_passes_and_enters_shadow()
    {
        // Offending call
        var correctionBody = new JsonObject
        {
            ["calls"] = new JsonArray
            {
                new JsonObject { ["name"] = "bash", ["arguments"] = "{\"command\":\"git push origin master\"}" },
            }
        };
        _notebook.Write("correction-msg-100", correctionBody.ToJsonString(),
            title: "correction", tags: new[] { "guard" }, kind: "correction");

        // A good call that the candidate does NOT block
        _notebook.Write("guard-call-t1-1",
            System.Text.Json.JsonSerializer.Serialize(new { name = "bash", arguments = "{\"command\":\"git status\"}" }),
            title: "good call", tags: new[] { "guard" }, kind: "guard-call");

        var distil = new GuardDistil(_notebook);
        var result = distil.Gate(new GuardDistil.Candidate("cand-3", "bash.*git push origin master", "test"));
        Assert.True(result.Passed);
        Assert.Equal(GuardResolution.New, result.Resolution);
        Assert.NotNull(result.Lifecycle);
        Assert.Equal(GuardStatus.Shadow, result.Lifecycle!.Status);
        // The guard record should exist
        Assert.NotNull(_notebook.Get("guard-cand-3"));
    }

    [Fact]
    public void Gate_candidate_supersedes_existing_rule()
    {
        // Existing guard rule
        var existingBody = new JsonObject
        {
            ["ruleId"] = "cand-4",
            ["pattern"] = "bash.*git push origin master",
            ["evaluations"] = 5,
            ["falsePositives"] = 0,
        };
        _notebook.Write("guard-cand-4", existingBody.ToJsonString(),
            title: "guard rule lifecycle", tags: new[] { "guard" }, kind: "guard", status: "open");

        // Offending call
        var correctionBody = new JsonObject
        {
            ["calls"] = new JsonArray
            {
                new JsonObject { ["name"] = "bash", ["arguments"] = "{\"command\":\"git push origin master\"}" },
            }
        };
        _notebook.Write("correction-msg-101", correctionBody.ToJsonString(),
            title: "correction", tags: new[] { "guard" }, kind: "correction");

        var distil = new GuardDistil(_notebook);
        var result = distil.Gate(new GuardDistil.Candidate("cand-4", "bash.*git push origin master", "test"));
        // The existing rule covers the offending call → Noop (superseded)
        Assert.True(result.Passed);
        Assert.Equal(GuardResolution.Noop, result.Resolution);
    }

    // ── ToolGuard.Matches / CompileForPattern ────────────────────────────────

    [Fact]
    public void ToolGuard_Matches_returns_true_for_matching_call()
    {
        var call = new ToolCallContent("call-1", "bash", new JsonObject { ["command"] = "git push origin master" });
        Assert.True(ToolGuard.Matches("bash.*git push origin master", call));
    }

    [Fact]
    public void ToolGuard_Matches_returns_false_for_non_matching_call()
    {
        var call = new ToolCallContent("call-1", "bash", new JsonObject { ["command"] = "git status" });
        Assert.False(ToolGuard.Matches("bash.*git push origin master", call));
    }

    [Fact]
    public void ToolGuard_Matches_returns_false_for_invalid_regex()
    {
        var call = new ToolCallContent("call-1", "bash", new JsonObject { ["command"] = "x" });
        Assert.False(ToolGuard.Matches("[invalid(", call));
    }
}
