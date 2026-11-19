// pi-sharp-hh-6: budget reminders at thresholds (25/10/5% remaining) + context_remaining tool.
// Acceptance: with maxTurns=20 reminders arrive at turns 15, 18, 19; each once, across restarts;
// no reminder at exhaustion; context_remaining agrees with the compaction gate's estimate.
using System.Text.Json.Nodes;
using PiSharp.Core.Agent;
using PiSharp.Core.Messages;
using PiSharp.Core.Models;
using PiSharp.Core.Notebook;
using PiSharp.Tests.Agent;
using PiSharp.Tests.Tools;

namespace PiSharp.Tests.Notebook;

public class BudgetReminderTests
{
    [Fact]
    public void ReminderDue_FiresOncePerThreshold_EachThreshold()
    {
        using var tmp = new TempWorkspace();
        var store = new NotebookStore(tmp.Path, "seat");
        var budget = new TaskBudget(store, () => "task1", 20, 100000, 500);

        // Turn 15: 5/20 = 25% remaining → 25% threshold crossed.
        var r1 = budget.ReminderDue(15, 150, 15);
        Assert.NotNull(r1);
        Assert.Contains("25% left", r1!);
        Assert.Contains("turns 15/20", r1);

        // Same turn again: already delivered, no repeat.
        Assert.Null(budget.ReminderDue(15, 150, 15));

        // Turn 16-17: no new threshold crossed.
        Assert.Null(budget.ReminderDue(16, 160, 16));
        Assert.Null(budget.ReminderDue(17, 170, 17));

        // Turn 18: 2/20 = 10% remaining → 10% threshold crossed.
        var r3 = budget.ReminderDue(18, 180, 18);
        Assert.NotNull(r3);
        Assert.Contains("10% left", r3!);

        // Turn 19: 1/20 = 5% remaining → 5% threshold crossed.
        var r4 = budget.ReminderDue(19, 190, 19);
        Assert.NotNull(r4);
        Assert.Contains("5% left", r4!);
        Assert.Contains("commit WIP", r4!);

        // Turn 20: exhaustion (turnsCharged >= maxTurns), no reminder.
        Assert.Null(budget.ReminderDue(20, 200, 20));
    }

    [Fact]
    public void ReminderDelivered_PersistsAcrossBudgetInstances()
    {
        using var tmp = new TempWorkspace();
        var store = new NotebookStore(tmp.Path, "seat");
        var b1 = new TaskBudget(store, () => "task1", 20, 100000, 500);
        var r = b1.ReminderDue(15, 150, 15);
        Assert.NotNull(r);
        Assert.NotNull(store.Get("budget-reminder-task1:25"));

        // New instance (simulating restart): the mark is in the notebook.
        var b2 = new TaskBudget(store, () => "task1", 20, 100000, 500);
        Assert.True(b2.ReminderDelivered("task1", 25));
        Assert.Null(b2.ReminderDue(15, 150, 15));
    }

    [Fact]
    public void CustomReminderThresholds()
    {
        using var tmp = new TempWorkspace();
        var store = new NotebookStore(tmp.Path, "seat");
        var budget = new TaskBudget(store, () => "task1", 10, 100000, 500, reminderAt: [50]);
        // Turn 5: 5/10 = 50% remaining.
        var r = budget.ReminderDue(5, 50, 5);
        Assert.NotNull(r);
        Assert.Contains("50% left", r!);
        // Default thresholds (25/10/5) are replaced, so turn 8 (20% remaining) fires nothing.
        Assert.Null(budget.ReminderDue(8, 80, 8));
    }

    [Fact]
    public void SnapshotAndLimits()
    {
        using var tmp = new TempWorkspace();
        var store = new NotebookStore(tmp.Path, "seat");
        var budget = new TaskBudget(store, () => "task1", 20, 100000, 500);
        var (mt, mtok, mc) = budget.Limits;
        Assert.Equal(20, mt);
        Assert.Equal(100000, mtok);
        Assert.Equal(500, mc);
        Assert.Equal((0, 0, 0), budget.Snapshot("task1"));
    }

    [Fact]
    public async Task FullRun_RemindersLandAtCorrectTurns()
    {
        using var tmp = new TempWorkspace();
        var store = new NotebookStore(tmp.Path, "seat");
        var budget = new TaskBudget(store, () => "task1", 20, 100000, 500);
        var tool = new DelegateTool { Name = "noop" };
        var ctx = new AgentContext { SystemPrompt = "sys", Tools = [tool] };
        var stream = new ScriptedStream();
        // 20 tool-call turns + 1 final text turn.
        for (var i = 0; i < 21; i++)
            stream.Then(() => Fakes.ToolCalls(Fakes.Call("c" + i, "noop")) with { Usage = new Usage(Output: 10) });
        stream.Then(Fakes.Text("done"));

        var composed = ComposeReminders(Fakes.Config() with { MaxIdenticalToolCalls = 0 }, budget);
        var events = await Fakes.Collect(AgentLoop.Run([new UserMessage("go")], ctx, composed, budget.Wrap(stream.Stream)));

        // All three reminders delivered.
        Assert.NotNull(store.Get("budget-reminder-task1:25"));
        Assert.NotNull(store.Get("budget-reminder-task1:10"));
        Assert.NotNull(store.Get("budget-reminder-task1:5"));

        // The 5% reminder includes the WIP warning.
        Assert.Contains("commit WIP", store.Get("budget-reminder-task1:5")!["text"]!.GetValue<string>());

        // The reminder appeared as a trailing user message in at least one stream context.
        var hasReminder = stream.Contexts.Any(c =>
            c.Messages.OfType<UserMessage>().Any(m => m.Text.Contains("% left")));
        Assert.True(hasReminder, "reminder should appear as a trailing user message in a stream context");

        // System prompt unchanged in all contexts.
        Assert.All(stream.Contexts, c => Assert.Equal("sys", c.SystemPrompt));
    }

    [Fact]
    public async Task RestartAfterPartialRun_DoesNotRedeliver()
    {
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Path, "seat");
        var b1 = new TaskBudget(store, () => "task1", 20, 100000, 500);
        var tool = new DelegateTool { Name = "noop" };
        var ctx = new AgentContext { SystemPrompt = "sys", Tools = [tool] };
        var stream = new ScriptedStream();
        // Run 15 turns (crosses 25%).
        for (var i = 0; i < 16; i++)
            stream.Then(() => Fakes.ToolCalls(Fakes.Call("c" + i, "noop")) with { Usage = new Usage(Output: 10) });
        stream.Then(Fakes.Text("done"));
        await Fakes.Collect(AgentLoop.Run([new UserMessage("go")], ctx,
            Fakes.Config() with { MaxIdenticalToolCalls = 0 }, b1.Wrap(stream.Stream)));
        Assert.NotNull(store.Get("budget-reminder-task1:25"));

        // Simulate restart: new budget instance, same store, continue.
        var b2 = new TaskBudget(store, () => "task1", 20, 100000, 500);
        // The 25% mark is already in the notebook.
        Assert.True(b2.ReminderDelivered("task1", 25));
        // Running more turns: 10% and 5% will fire, but 25% will not redeliver.
        var stream2 = new ScriptedStream();
        var ctx2 = new AgentContext { SystemPrompt = "sys", Tools = [tool] };
        // Need to get past turns 15-19. The budget already has 15 turns used, so we need 5 more.
        for (var i = 0; i < 6; i++)
            stream2.Then(() => Fakes.ToolCalls(Fakes.Call("r" + i, "noop")) with { Usage = new Usage(Output: 10) });
        stream2.Then(Fakes.Text("done"));
        var composed = ComposeReminders(Fakes.Config() with { MaxIdenticalToolCalls = 0 }, b2);
        await Fakes.Collect(AgentLoop.Run([new UserMessage("continue")], ctx2, composed, b2.Wrap(stream2.Stream)));

        // 10% and 5% fired, 25% was not redelivered.
        Assert.NotNull(store.Get("budget-reminder-task1:10"));
        Assert.NotNull(store.Get("budget-reminder-task1:5"));
        // 25% reminder exists but was written by b1, not b2.
        var r25 = store.Get("budget-reminder-task1:25")!;
        Assert.Equal(1L, r25["revision"]!.GetValue<long>()); // one write, no revision bump
    }

    [Fact]
    public async Task ExhaustionProducesNoReminder()
    {
        using var tmp = new TempWorkspace();
        var store = new NotebookStore(tmp.Path, "seat");
        var budget = new TaskBudget(store, () => "task1", 3, 100000, 500);
        var tool = new DelegateTool { Name = "noop" };
        var ctx = new AgentContext { SystemPrompt = "sys", Tools = [tool] };
        var stream = new ScriptedStream();
        for (var i = 0; i < 4; i++)
            stream.Then(() => Fakes.ToolCalls(Fakes.Call("c" + i, "noop")) with { Usage = new Usage(Output: 10) });
        stream.Then(Fakes.Text("done"));

        var events = await Fakes.Collect(AgentLoop.Run([new UserMessage("go")], ctx,
            Fakes.Config() with { MaxIdenticalToolCalls = 0 }, budget.Wrap(stream.Stream)));

        // Budget exhausted: the last assistant message carries the error code.
        Assert.Equal("budget_exhausted", Assert.Single(events.OfType<AgentEnd>()).Outcome);
        // With maxTurns=3: 25% means remaining<=0.75, i.e. used>=2.25 → turn 3.
        // But turn 3 IS the exhaustion turn (turnsCharged=3 >= 3), so ReminderDue returns null.
        // No reminders at all.
        Assert.Null(store.Get("budget-reminder-task1:25"));
        Assert.Null(store.Get("budget-reminder-task1:10"));
        Assert.Null(store.Get("budget-reminder-task1:5"));
    }

    [Fact]
    public async Task ContextRemainingTool_ReturnsBudgetAndContext()
    {
        using var tmp = new TempWorkspace();
        var store = new NotebookStore(tmp.Path, "seat");
        var budget = new TaskBudget(store, () => "task1", 20, 100000, 500);
        // Seed some state.
        budget.ReminderDue(15, 150, 15);

        var tool = new PiSharp.Tools.ContextRemainingTool(
            () => budget.Snapshot("task1"),
            () => budget.Limits,
            () => 5000,
            128000);

        Assert.Equal("context_remaining", tool.Name);
        var result = await tool.ExecuteAsync("t1", new JsonObject(), null, CancellationToken.None);
        Assert.Contains("Task budget", result.Text);
        Assert.Contains("5000 tokens used of a 128000 token window", result.Text);
    }

    [Fact]
    public async Task ContextRemainingTool_WithoutBudget()
    {
        var tool = new PiSharp.Tools.ContextRemainingTool(
            () => null,
            () => null,
            () => 3000,
            128000);
        var result = await tool.ExecuteAsync("t1", new JsonObject(), null, CancellationToken.None);
        Assert.Contains("Task budget not configured", result.Text);
        Assert.Contains("3000 tokens used of a 128000 token window", result.Text);
    }

    [Fact]
    public async Task ContextRemainingTool_ShowPercentLeft()
    {
        using var tmp = new TempWorkspace();
        var store = new NotebookStore(tmp.Path, "seat");
        var budget = new TaskBudget(store, () => "task1", 20, 100000, 500);
        // Manually set counters via the budget's internal write.
        budget.ReminderDue(15, 150, 15);
        // Snapshot will read from the budget row, but ReminderDue doesn't write the budget row itself.
        // Let's use a different approach: run one turn through the budget.
        // For simplicity, just verify the tool formats correctly with the snapshot.
        var tool = new PiSharp.Tools.ContextRemainingTool(
            () => (15, 150, 15),
            () => budget.Limits,
            () => 0,
            100000);
        var result = await tool.ExecuteAsync("t1", new JsonObject(), null, CancellationToken.None);
        Assert.Contains("turns 15/20 (25% left)", result.Text);
    }

    /// <summary>Mirrors CliHost.ComposeBudgetReminder: layers the budget reminder over PrepareNextTurn.</summary>
    private static AgentLoopConfig ComposeReminders(AgentLoopConfig config, TaskBudget budget) =>
        config with
        {
            PrepareNextTurn = async turn =>
            {
                var update = config.PrepareNextTurn is null ? null : await config.PrepareNextTurn!(turn).ConfigureAwait(false);
                var reminder = budget.TakeDueReminder();
                if (reminder is null) return update;
                var trailing = new List<Message>(update?.Trailing ?? []);
                trailing.Add(new UserMessage(reminder));
                return (update ?? new TurnUpdate()) with { Trailing = trailing };
            },
        };
}
