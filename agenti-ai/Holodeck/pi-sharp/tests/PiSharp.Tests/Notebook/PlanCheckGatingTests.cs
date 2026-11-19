using System.Text.Json.Nodes;
using PiSharp.Core.Notebook;
using PiSharp.Core.Verification;
using PiSharp.Tests.Tools;

namespace PiSharp.Tests.Notebook;

/// <summary>plan-tool-3 (PT3): gated step completion.
/// <list type="bullet">
/// <item>a failing check leaves the step open and returns the first error;</item>
/// <item>a passing check completes the step and records a linked verify- record as sourceId;</item>
/// <item>a raw notebook transition to completed on a checked plan-step is refused ("use plan done");</item>
/// <item>an unchecked completed step is marked ✓? in the plan summary.</item>
/// </list>
/// </summary>
public class PlanCheckGatingTests
{
    private const string Task = "wi-p3";

    // A harness-shaped runner: on pass it records a verify- record (as CliHost does) and returns its id;
    // on fail it returns the fixed first error and no record.
    private static PlanCheckRunner Runner(bool pass, string? error, string? cwd = null)
        => (check, task, store, ct) =>
        {
            if (!pass) return System.Threading.Tasks.Task.FromResult((false, (string?)(error ?? "check failed"), (string?)null));
            var verifier = new Verifier(cwd ?? Directory.GetCurrentDirectory());
            var one = verifier.RunOneAsync(check, null, ct).GetAwaiter().GetResult();
            var id = VerificationRecords.Record(store, new VerificationReport(task, [one], one.Passed, null));
            return System.Threading.Tasks.Task.FromResult((one.Passed, (string?)null, (string?)id));
        };

    private static JsonObject PutCheckedStep(NotebookStore store, string id, string text, string check, double order)
        => store.Put(id, "plan-step", text, Task, "open", 0, order: order, check: check);

    private static Task<PiSharp.Core.Agent.AgentToolResult> Call(PlanTool tool, JsonObject args) => tool.ExecuteAsync("call", args, null, default);

    // Acceptance (1): a failing check leaves the step open and returns the first error.
    [Fact]
    public async Task FailingCheckLeavesStepOpenAndReturnsFirstError()
    {
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Path, "seat");
        PutCheckedStep(store, "plan-wi-p3-01", "build the thing", "file:does-not-exist.bin", order: 0);
        var tool = new PlanTool(store, Runner(false, "boom-to-stderr: check failed (exit 1)"));
        var result = await Call(tool, new() { ["op"] = "done", ["task"] = Task, ["id"] = "plan-wi-p3-01" });
        var json = JsonNode.Parse(result.Text)!.AsObject();
        Assert.False(json["ok"]!.GetValue<bool>());
        Assert.Contains("boom-to-stderr", json["error"]!.GetValue<string>());
        // The step is still open and uncompleted.
        Assert.Equal("open", store.Get("plan-wi-p3-01")!["status"]!.GetValue<string>());
    }

    // Acceptance (2): a passing check completes the step and the verify- record is linked as sourceId.
    [Fact]
    public async Task PassingCheckCompletesStepAndLinksVerifyRecord()
    {
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Path, "seat");
        PutCheckedStep(store, "plan-wi-p3-02", "build the thing", "file:README.md", order: 0);
        File.WriteAllText(Path.Combine(tmp.Path, "README.md"), "marker\n");
        var tool = new PlanTool(store, Runner(true, null, cwd: tmp.Path));
        var result = await Call(tool, new() { ["op"] = "done", ["task"] = Task, ["id"] = "plan-wi-p3-02" });
        var json = JsonNode.Parse(result.Text)!.AsObject();
        Assert.True(json["ok"]!.GetValue<bool>());
        var sourceId = json["sourceId"]!.GetValue<string>();
        Assert.StartsWith(VerificationRecords.VerifyPrefix, sourceId);
        // The step is completed and carries the verify- record as its sourceId.
        var step = store.Get("plan-wi-p3-02")!;
        Assert.Equal("completed", step["status"]!.GetValue<string>());
        Assert.Equal(sourceId, step["sourceId"]!.GetValue<string>());
        // The verify- record exists and names the task. Its evidence fields live in the record's text as JSON.
        var verify = store.Get(sourceId)!;
        var verifyJson = JsonNode.Parse(verify["text"]!.GetValue<string>())!.AsObject();
        Assert.Equal(Task, verifyJson["task"]!.GetValue<string>());
        Assert.True(verifyJson["passed"]!.GetValue<bool>());
    }

    // Acceptance (3): a raw notebook transition to completed on a checked plan-step is refused.
    [Fact]
    public async Task RawNotebookTransitionToCompletedOnCheckedStepIsRefused()
    {
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Path, "seat");
        PutCheckedStep(store, "plan-wi-p3-04", "build the thing", "file:README.md", order: 0);
        var tool = new NotebookTool(store);
        var result = await tool.ExecuteAsync("t", new()
        {
            ["op"] = "transition", ["id"] = "plan-wi-p3-04", ["status"] = "completed",
        }, null, default);
        // ExecuteAsync catches the ArgumentException and returns it as an error result.
        Assert.Contains("plan done", result.Text);
        // The step is untouched.
        Assert.Equal("open", store.Get("plan-wi-p3-04")!["status"]!.GetValue<string>());
    }

    // An unchecked plan-step can still be transitioned to completed through the raw notebook (no check → no gate).
    [Fact]
    public async Task RawTransitionOnUncheckedStepIsAllowed()
    {
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Path, "seat");
        store.Put("plan-wi-p3-05", "plan-step", "no check here", Task, "open", 0, order: 0, check: null);
        var tool = new NotebookTool(store);
        await tool.ExecuteAsync("t", new() { ["op"] = "transition", ["id"] = "plan-wi-p3-05", ["status"] = "completed" }, null, default);
        Assert.Equal("completed", store.Get("plan-wi-p3-05")!["status"]!.GetValue<string>());
    }

    // A checked step that moves to a non-completed status (e.g. blocked) is not gated.
    [Fact]
    public async Task RawTransitionToBlockedOnCheckedStepIsAllowed()
    {
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Path, "seat");
        PutCheckedStep(store, "plan-wi-p3-06", "blocked step", "file:README.md", order: 0);
        var tool = new NotebookTool(store);
        await tool.ExecuteAsync("t", new() { ["op"] = "transition", ["id"] = "plan-wi-p3-06", ["status"] = "blocked" }, null, default);
        Assert.Equal("blocked", store.Get("plan-wi-p3-06")!["status"]!.GetValue<string>());
    }

    // The ✓? rendering: a completed step with no check is counted in the summary as an unchecked (✓?) completion.
    [Fact]
    public void UncheckedCompletedStepRendersAsMarkedInSummary()
    {
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Path, "seat");
        // One completed step with no check → the summary line must carry the ✓? marker.
        store.Put("plan-wi-p3-10", "plan-step", "done unverified", Task, "completed", 0, order: 0, check: null);
        store.Put("plan-wi-p3-11", "plan-step", "still open", Task, "open", 0, order: 1, check: null);
        var entries = store.BriefingEntries();
        var summary = entries.Single(e => e.Key == "plan:~summary").Line;
        Assert.Contains("1/2 done", summary);
        Assert.Contains("✓?", summary);

        // A completed step WITH a check is not counted as unchecked → no ✓? marker.
        using var tmp2 = new TempWorkspace();
        using var store2 = new NotebookStore(tmp2.Path, "seat");
        store2.Put("plan-wi-p3-20", "plan-step", "done verified", Task, "completed", 0, order: 0, check: "file:README.md");
        store2.Put("plan-wi-p3-21", "plan-step", "still open", Task, "open", 0, order: 1, check: null);
        var summary2 = store2.BriefingEntries().Single(e => e.Key == "plan:~summary").Line;
        Assert.Contains("1/2 done", summary2);
        Assert.DoesNotContain("✓?", summary2);
    }

    // The PlanCheck parser turns inline predicates into the right DoneCheck kinds and leaves ids as references.
    [Fact]
    public void PlanCheckParsesInlinePredicatesAndIdReferences()
    {
        var file = PlanCheck.Parse("file:out/bin/app", Task);
        Assert.Equal(DoneCheckKind.FileExists, file!.Kind);
        Assert.Equal("out/bin/app", file.Spec);

        var grep = PlanCheck.Parse("grep:TODO::src", Task);
        Assert.Equal(DoneCheckKind.Grep, grep!.Kind);
        Assert.Equal("TODO::src", grep.Spec);

        Assert.True(PlanCheck.IsIdReference("dc-1"));
        Assert.False(PlanCheck.IsIdReference("file:README.md"));
        Assert.False(PlanCheck.IsIdReference("grep:TODO::src"));
        Assert.Null(PlanCheck.Parse("   ", Task));
        Assert.Null(PlanCheck.Parse(null, Task));
    }
}
