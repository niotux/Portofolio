using System.Text.Json.Nodes;
using PiSharp.Core.Factory;
using PiSharp.Core.Notebook;
using PiSharp.Tests.Tools;
using Xunit;

namespace PiSharp.Tests.Notebook;

/// <summary>digital-scribe, 2026-10-26: plans that are "proper and not single lines", "more creativity and reasoning around the
/// plan steps", and the plan tool wired in: each finished step asks for a short reflection, keeps what it settled, and the
/// steps go into the epic's doc plan.</summary>
public sealed class PlanToolReflectionTests
{
    private static Task<PiSharp.Core.Agent.AgentToolResult> Call(PlanTool tool, JsonObject args) => tool.ExecuteAsync("call", args, null, default);

    private static JsonObject Set(string why) => new()
    {
        ["op"] = "set", ["task"] = "hc2000-hdd", ["why"] = why,
        ["steps"] = new JsonArray(
            new JsonObject { ["title"] = "Find why demo 45 cannot start", ["content"] = "a staging gap or a code defect" },
            new JsonObject { ["title"] = "Fix the cause", ["content"] = "source or staging" },
            new JsonObject { ["title"] = "Prove the saved draft on screen", ["content"] = "one installed demo" }),
    };

    [Fact]
    public async Task ADoneStepKeepsWhatItSettledAndAsksForAReflection()
    {
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Path, "seat");
        var tool = new PlanTool(store);
        await Call(tool, Set("first plan"));

        var done = await Call(tool, new JsonObject { ["op"] = "done", ["task"] = "hc2000-hdd", ["settled"] = "ROMs were not staged; no code defect" });

        Assert.Contains(PlanTool.Reflection, done.Content.OfType<PiSharp.Core.Messages.TextContent>().Single().Text);
        var first = store.List("hc2000-hdd", "plan-step").OrderBy(x => x["order"]!.GetValue<double>()).First();
        Assert.Equal("completed", first["status"]!.GetValue<string>());
        Assert.EndsWith("Settled: ROMs were not staged; no code defect", first["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task APlanCanBeChangedWithItsFinishedStepsKeptDone()
    {
        // pi5, 2026-10-26 15:36Z: the reflection asks to change the plan, and every re-set with a finished step in it was
        // refused as "Completed plan step ... cannot be reopened" (the tool says done, the store compared with completed).
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Path, "seat");
        var tool = new PlanTool(store);
        await Call(tool, Set("first plan"));
        await Call(tool, new JsonObject { ["op"] = "done", ["task"] = "hc2000-hdd", ["settled"] = "ROMs missing" });

        await Call(tool, new JsonObject
        {
            ["op"] = "set", ["task"] = "hc2000-hdd", ["why"] = "staging, not code",
            ["steps"] = new JsonArray(
                new JsonObject { ["title"] = "Find why demo 45 cannot start", ["status"] = "done" },
                new JsonObject { ["title"] = "Stage the ROMs in the demo's home" },
                new JsonObject { ["title"] = "Prove the saved draft on screen" }),
        });

        var steps = store.List("hc2000-hdd", "plan-step").OrderBy(x => x["order"]!.GetValue<double>()).ToArray();
        Assert.Equal("completed", steps[0]["status"]!.GetValue<string>());
        Assert.Equal("Stage the ROMs in the demo's home", steps[1]["title"]!.GetValue<string>());
    }

    [Fact]
    public async Task AReplanReopensADoneStepItNamesOpen_AndKeepsDoneTheOnesItSaysNothingAbout()
    {
        // pi14, 2026-10-29 13:12Z: "Completed plan step s1 cannot be reopened" on a replan; a plan changes at any step.
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Path, "seat");
        var tool = new PlanTool(store);
        await Call(tool, Set("first plan"));
        await Call(tool, new JsonObject { ["op"] = "done", ["task"] = "hc2000-hdd", ["settled"] = "ROMs missing" });
        await Call(tool, new JsonObject { ["op"] = "done", ["task"] = "hc2000-hdd", ["settled"] = "staged" });

        await Call(tool, new JsonObject
        {
            ["op"] = "set", ["task"] = "hc2000-hdd", ["why"] = "the staging did not hold",
            ["steps"] = new JsonArray(
                new JsonObject { ["id"] = "plan-hc2000-hdd-01", ["title"] = "Find why demo 45 cannot start" },
                new JsonObject { ["id"] = "plan-hc2000-hdd-02", ["title"] = "Fix the cause", ["status"] = "in_progress" },
                new JsonObject { ["id"] = "plan-hc2000-hdd-03", ["title"] = "Prove the saved draft on screen", ["status"] = "pending" }),
        });

        var steps = store.List("hc2000-hdd", "plan-step").OrderBy(x => x["order"]!.GetValue<double>()).ToArray();
        Assert.Equal(["completed", "open", "open"], steps.Select(x => x["status"]!.GetValue<string>()));
        var next = await Call(tool, new JsonObject { ["op"] = "done", ["task"] = "hc2000-hdd", ["settled"] = "the cause is fixed" });
        Assert.Contains("plan-hc2000-hdd-02", next.Content.OfType<PiSharp.Core.Messages.TextContent>().Single().Text);
    }

    [Fact]
    public async Task BlockWithoutAnIdBlocksTheCurrentStep()
    {
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Path, "seat");
        var tool = new PlanTool(store);
        await Call(tool, Set("first plan"));
        await Call(tool, new JsonObject { ["op"] = "done", ["task"] = "hc2000-hdd" });

        await Call(tool, new JsonObject { ["op"] = "block", ["task"] = "hc2000-hdd" });

        var steps = store.List("hc2000-hdd", "plan-step").OrderBy(x => x["order"]!.GetValue<double>()).ToArray();
        Assert.Equal(["completed", "blocked", "open"], steps.Select(x => x["status"]!.GetValue<string>()));
    }

    [Fact]
    public async Task TheStatusWordsOfTheCoderTextAreTaken()
    {
        // pi3, 2026-10-26 16:28Z: the coder text says todo, doing and done; the tool took only pending, in_progress and done.
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Path, "seat");
        var tool = new PlanTool(store);
        await Call(tool, new JsonObject
        {
            ["op"] = "set", ["task"] = "hc2000-keyboard-reference",
            ["steps"] = new JsonArray(
                new JsonObject { ["title"] = "Settle the mapping sources", ["status"] = "done: KeyMap is authoritative" },
                new JsonObject { ["title"] = "Confirm the mock-up", ["status"] = "doing" },
                new JsonObject { ["title"] = "Implement the reference", ["status"] = "todo" }),
        });
        var steps = store.List("hc2000-keyboard-reference", "plan-step").OrderBy(x => x["order"]!.GetValue<double>()).ToArray();
        Assert.Equal(["completed", "open", "open"], steps.Select(x => x["status"]!.GetValue<string>()));
    }

    [Fact]
    public async Task ABlockedStepAsksToChangeTheApproach()
    {
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Path, "seat");
        var tool = new PlanTool(store);
        await Call(tool, Set("first plan"));
        var id = store.List("hc2000-hdd", "plan-step").OrderBy(x => x["order"]!.GetValue<double>()).Last()["id"]!.GetValue<string>();

        var blocked = await Call(tool, new JsonObject { ["op"] = "block", ["task"] = "hc2000-hdd", ["id"] = id });

        Assert.Contains(PlanTool.Unblock, blocked.Content.OfType<PiSharp.Core.Messages.TextContent>().Single().Text);
    }

    [Fact]
    public async Task EveryChangeIsToldWithTheStepsInOrderAndTheReasonForASet()
    {
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Path, "seat");
        var told = new List<(string Task, string[] Titles, string? Why)>();
        var tool = new PlanTool(store)
        {
            Changed = (task, steps, why, _) => { told.Add((task, steps.Select(s => s["title"]!.GetValue<string>()).ToArray(), why)); return Task.CompletedTask; },
        };

        await Call(tool, Set("demo 45 failed before start"));
        await Call(tool, new JsonObject { ["op"] = "done", ["task"] = "hc2000-hdd" });
        await Call(tool, new JsonObject { ["op"] = "show", ["task"] = "hc2000-hdd" });

        Assert.Equal(2, told.Count);
        Assert.Equal(["Find why demo 45 cannot start", "Fix the cause", "Prove the saved draft on screen"], told[0].Titles);
        Assert.Equal("demo 45 failed before start", told[0].Why);
        Assert.Null(told[1].Why);
    }

    [Fact]
    public async Task AFailingCopyNeverFailsThePlan()
    {
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Path, "seat");
        var tool = new PlanTool(store) { Changed = (_, _, _, _) => throw new HttpRequestException("KB gone") };
        await Call(tool, Set("first plan"));
        Assert.Equal(3, store.List("hc2000-hdd", "plan-step").Count);
    }

    [Fact]
    public void TheEpicDocKeepsTheCodersTextAndReplacesOnlyTheSteps()
    {
        var steps = new List<JsonObject>
        {
            new() { ["title"] = "Find the cause", ["text"] = "Find the cause\nSettled: ROMs missing", ["status"] = "completed" },
            new() { ["title"] = "Fix it", ["text"] = "staging", ["status"] = "open" },
            new() { ["title"] = "Prove it", ["text"] = "Prove it", ["status"] = "open" },
        };
        var section = PlanDoc.Render(steps, new DateTimeOffset(2026, 10, 26, 14, 0, 0, TimeSpan.Zero), "demo 45 failed");

        Assert.Contains("1. **done**: Find the cause - Settled: ROMs missing", section);
        Assert.Contains("2. **doing**: Fix it - staging", section);
        Assert.Contains("3. **todo**: Prove it\n", section);
        Assert.Contains("Last change: demo 45 failed", section);

        var first = PlanDoc.Merge("My flow: frame, fix, prove.", section);
        Assert.StartsWith("My flow: frame, fix, prove.\n\n" + PlanDoc.StepsHeading, first);
        var again = PlanDoc.Merge(first, PlanDoc.Render(steps.Take(1).ToList(), DateTimeOffset.UnixEpoch));
        Assert.StartsWith("My flow: frame, fix, prove.\n\n" + PlanDoc.StepsHeading, again);
        Assert.DoesNotContain("Fix it", again);
        Assert.Equal(section, PlanDoc.Merge("", section));
    }
}
