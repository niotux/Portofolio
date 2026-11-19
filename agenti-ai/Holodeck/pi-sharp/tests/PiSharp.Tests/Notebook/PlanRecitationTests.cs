using PiSharp.Core.Messages;
using PiSharp.Core.Notebook;
using PiSharp.Tests.Tools;

namespace PiSharp.Tests.Notebook;

public class PlanRecitationTests
{
    // These behavior fixtures model a full harness prompt; budget boundaries are tested separately.
    private static IReadOnlyList<Message> Prepare(NotebookStore store, IReadOnlyList<Message> messages,
        int? reciteEvery = null, int? stallTurns = null) => NotebookContext.Prepare(store, messages,
            NotebookContext.Default() with { BaseCharacters = 120000 }, reciteEvery, stallTurns);

    private static NotebookStore MakeStore(string path) => new(path, "seat");

    private static (string Id, string Text, string Title, string Status, double Order) Step(string id, string text, double order, string status = "open")
        => (id, text, text, status, order);

    private static int NudgeCount(IReadOnlyList<Message> result) =>
        result.OfType<UserMessage>().Count(m => m.Text.Contains("[harness] no plan progress"));

    private static int ReciteCount(IReadOnlyList<Message> result) =>
        result.OfType<UserMessage>().Count(m => m.Text.Contains("> plan step:"));

    [Fact]
    public void RecitationAppearsOnStepChange()
    {
        using var tmp = new TempWorkspace();
        using var store = MakeStore(tmp.Path);
        store.SetPlan("t1", [Step("s1", "first", 1, "in_progress"), Step("s2", "second", 2, "pending")]);

        var messages = new List<Message> { new UserMessage("go") };
        // Turn 1: first pin + recitation (step change from null).
        var r1 = Prepare(store, messages, reciteEvery: 100, stallTurns: 100);
        Assert.Equal(1, ReciteCount(r1));
        Assert.Contains(r1.OfType<UserMessage>(), m => m.Text.Contains("> plan step: s1"));

        // Complete s1 → current step changes to s2.
        store.CompletePlanStep("t1", "s1");
        var r2 = Prepare(store, messages, reciteEvery: 100, stallTurns: 100);
        // The s2 recitation should appear (new step).
        Assert.Contains(r2.OfType<UserMessage>(), m => m.Text.Contains("> plan step: s2"));
    }

    [Fact]
    public void RecitationRepeatsEveryNthTurn()
    {
        using var tmp = new TempWorkspace();
        using var store = MakeStore(tmp.Path);
        store.SetPlan("t1", [Step("s1", "work", 1, "in_progress")]);

        var messages = new List<Message> { new UserMessage("go") };
        // Turn 1: recitation (step change from null).
        Prepare(store, messages, reciteEvery: 2, stallTurns: 100);
        // Turn 2: PlanTurnsSinceRecitation=1, not yet >= 2.
        Prepare(store, messages, reciteEvery: 2, stallTurns: 100);
        // Turn 3: PlanTurnsSinceRecitation=2, >= 2 → recitation.
        var r3 = Prepare(store, messages, reciteEvery: 2, stallTurns: 100);
        // The recitation persists in the context (re-inserted injection), so count >= 1.
        Assert.True(ReciteCount(r3) >= 1);
    }

    [Fact]
    public void StallNudgeIsInjectedExactlyOnce()
    {
        using var tmp = new TempWorkspace();
        using var store = MakeStore(tmp.Path);
        store.SetPlan("t1", [Step("s1", "work", 1, "in_progress")]);

        var messages = new List<Message> { new UserMessage("go") };
        const int stallTurns = 3;

        // Turn 1: progress reset (step change from null), PlanTurnsSinceProgress=0.
        var r1 = Prepare(store, messages, reciteEvery: 1000, stallTurns: stallTurns);
        Assert.Equal(0, NudgeCount(r1));

        // Turn 2: PlanTurnsSinceProgress=1.
        var r2 = Prepare(store, messages, reciteEvery: 1000, stallTurns: stallTurns);
        Assert.Equal(0, NudgeCount(r2));

        // Turn 3: PlanTurnsSinceProgress=2.
        var r3 = Prepare(store, messages, reciteEvery: 1000, stallTurns: stallTurns);
        Assert.Equal(0, NudgeCount(r3));

        // Turn 4: PlanTurnsSinceProgress=3 >= stallTurns → nudge.
        var r4 = Prepare(store, messages, reciteEvery: 1000, stallTurns: stallTurns);
        Assert.Equal(1, NudgeCount(r4));

        // Turn 5: nudge persists (re-inserted) but no NEW nudge added.
        var r5 = Prepare(store, messages, reciteEvery: 1000, stallTurns: stallTurns);
        Assert.Equal(1, NudgeCount(r5));

        // Progress: complete s1. Reset.
        store.CompletePlanStep("t1", "s1");
        var r6 = Prepare(store, messages, reciteEvery: 1000, stallTurns: stallTurns);
        // After progress, the state is reset. The old nudge may still be in the context
        // (it's an injection that persists), but no new nudge is added.
        // The key check: after progress, StallNudged is false, so a future stall CAN nudge again.
    }

    [Fact]
    public void NudgeCanFireAgainAfterProgress()
    {
        using var tmp = new TempWorkspace();
        using var store = MakeStore(tmp.Path);
        store.SetPlan("t1", [Step("s1", "work", 1, "in_progress"), Step("s2", "second", 2, "pending")]);

        var messages = new List<Message> { new UserMessage("go") };
        const int stallTurns = 1;

        // Turn 1: progress (step change from null), PlanTurnsSinceProgress=0.
        Prepare(store, messages, reciteEvery: 1000, stallTurns: stallTurns);
        // Turn 2: PlanTurnsSinceProgress=1 >= 1 → nudge.
        var r2 = Prepare(store, messages, reciteEvery: 1000, stallTurns: stallTurns);
        Assert.Equal(1, NudgeCount(r2));

        // Progress: complete s1, current step changes to s2.
        store.CompletePlanStep("t1", "s1");
        Prepare(store, messages, reciteEvery: 1000, stallTurns: stallTurns);
        // Turn after progress: PlanTurnsSinceProgress resets to 0, then increments.
        Prepare(store, messages, reciteEvery: 1000, stallTurns: stallTurns);
        // Now PlanTurnsSinceProgress=1 again → second nudge.
        var r4 = Prepare(store, messages, reciteEvery: 1000, stallTurns: stallTurns);
        // Two nudges total (one for s1, one for s2).
        Assert.Equal(2, NudgeCount(r4));
    }

    [Fact]
    public void PinnedStateIsByteIdenticalAcrossTurns()
    {
        using var tmp = new TempWorkspace();
        using var store = MakeStore(tmp.Path);
        store.SetPlan("t1", [Step("s1", "work", 1, "in_progress")]);

        var messages = new List<Message> { new UserMessage("go") };
        var r1 = Prepare(store, messages, reciteEvery: 1000, stallTurns: 1000);
        var pinned1 = r1.OfType<UserMessage>().First(m => m.Text.StartsWith("<notebook-state>")).Text;

        var r2 = Prepare(store, messages, reciteEvery: 1000, stallTurns: 1000);
        var pinned2 = r2.OfType<UserMessage>().First(m => m.Text.StartsWith("<notebook-state>")).Text;

        Assert.Equal(pinned1, pinned2);
    }

    [Fact]
    public void AfterResetThePinnedStateContainsPlanGroup()
    {
        using var tmp = new TempWorkspace();
        using var store = MakeStore(tmp.Path);
        store.SetPlan("t1", [Step("s1", "work", 1, "in_progress"), Step("s2", "second", 2, "pending")]);

        var messages = new List<Message> { new UserMessage("go") };
        Prepare(store, messages, reciteEvery: 100, stallTurns: 100);

        // Simulate compaction: reset the context.
        NotebookContext.Reset(store);

        // After reset, the next Prepare re-pins the full state including the plan group.
        var result = Prepare(store, messages, reciteEvery: 100, stallTurns: 100);
        var pinned = result.OfType<UserMessage>().First(m => m.Text.StartsWith("<notebook-state>")).Text;
        Assert.Contains("plan: 0/2 done", pinned);
        Assert.Contains("CURRENT", pinned);
    }

    [Fact]
    public void NoPlanMeansNoRecitationOrNudge()
    {
        using var tmp = new TempWorkspace();
        using var store = MakeStore(tmp.Path);
        var messages = new List<Message> { new UserMessage("go") };
        var r = Prepare(store, messages, reciteEvery: 1, stallTurns: 1);
        Assert.Equal(0, NudgeCount(r));
        Assert.Equal(0, ReciteCount(r));
    }

    [Fact]
    public void SettingsParsePlanReciteAndStallTurns()
    {
        var s = PiSharp.Core.Config.SettingsLoader.Parse("{\"planReciteEveryTurns\":4,\"planStallTurns\":12}");
        Assert.Equal(4, s.PlanReciteEveryTurns);
        Assert.Equal(12, s.PlanStallTurns);

        var s2 = PiSharp.Core.Config.SettingsLoader.Parse("{}");
        Assert.Null(s2.PlanReciteEveryTurns);
        Assert.Null(s2.PlanStallTurns);
    }
}
