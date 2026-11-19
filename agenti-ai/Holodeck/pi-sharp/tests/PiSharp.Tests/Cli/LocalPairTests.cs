using System.Text.Json.Nodes;
using PiSharp.Cli;
using PiSharp.Core.Agent;
using PiSharp.Core.Config;
using PiSharp.Core.Messages;
using PiSharp.Core.Models;

namespace PiSharp.Tests.Cli;

/// <summary>A router seat on two local models, the side of each turn picked by its kind (digital-scribe, 2026-10-28:
/// "hybrid with the two local models ... miniclaude for fast cheap calls and qwen3.8-flash-next for expensive smarter
/// ones"; epic local-pair-hybrid).</summary>
public sealed class LocalPairTests
{
    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);
    }

    /// <summary>The seat's own model: miniclaude through its own router connection.</summary>
    private static readonly ModelConfig Own = new()
    {
        Id = "miniclaude", BaseUrl = "http://127.0.0.1:18787/v1", Provider = "openai-completions", ContextWindow = 200000, MaxOutputTokens = 32000,
    };

    /// <summary>The smart side: flash-next's router pool, as the broker assigns it.</summary>
    private static readonly ModelConfig Smart = new()
    {
        Id = "qwen3-8-flash", BaseUrl = "http://router.test/v1", Provider = "openai-completions", Connection = "router",
        ContextWindow = 262144, MaxOutputTokens = 32000, CompactAtTokens = 180000,
    };

    private static HybridAnswer Pair(int? window = 200000) => new(0, "pair", new LocalProfile(150000, 32000))
    {
        LocalModel = "miniclaude", LocalWindow = window, Pick = SubscriptionAssignment.TurnKindPick, FollowupMaxTokens = 4000,
    };

    private static async Task<SeatAssignmentSwitch> OnPair(HybridAnswer pair)
    {
        var seat = new SeatAssignmentSwitch(new Uri("https://broker/"), "t", null, "rev-local", false, Own, Own, new Settings(), new Clock())
        {
            Fetch = (_, _) => Task.FromResult(new SeatAssignmentAnswer(false, Smart, "rev-pair") { Hybrid = pair }),
        };
        Assert.True(await seat.CheckAsync(force: true, CancellationToken.None));
        return seat;
    }

    [Fact]
    public async Task EachTurnRunsOnTheSideItsKindNeeds_AndNeverOnTheSubscriptionClient()
    {
        var seat = await OnPair(Pair());
        Assert.Equal("qwen3-8-flash", seat.Model.Id);
        Assert.False(seat.RunsSubscription); // both sides are router models the seat calls itself

        Assert.False(seat.PickTurn(20000, kind: TurnKind.TaskStart)); // a task's first turn: smart, where it already is
        Assert.True(seat.PickTurn(20000, kind: TurnKind.Routine));
        Assert.Equal("miniclaude", seat.Model.Id);
        Assert.False(seat.RunsSubscription);
        Assert.True(seat.PickTurn(20000, kind: TurnKind.Failure)); // a failed test or tool: smart
        Assert.Equal("qwen3-8-flash", seat.Model.Id);
        Assert.True(seat.PickTurn(20000, watch: true)); // reading the digest: fast
        Assert.True(seat.PickTurn(20000, kind: TurnKind.Work)); // a file changed: smart
        Assert.False(seat.PickTurn(20000, kind: TurnKind.Unknown)); // nothing known: smart
        Assert.Equal(2, seat.LocalTurns);
        Assert.Equal(4, seat.SubscriptionTurns);
        Assert.False(seat.RunsSubscription);
    }

    [Fact]
    public async Task TheSeatCompactsAtTheSmallerSide_AndTheCompactingTurnIsSmart()
    {
        var seat = await OnPair(Pair());
        // The fast side is the smaller (200k against the smart side's 180k compaction point: its own point is lower),
        // so the session compacts where both sides still hold it.
        var sizing = seat.SizingModel!;
        Assert.True(SubscriptionAssignment.CompactAt(sizing) <= SubscriptionAssignment.CompactAt(Smart));
        var limits = seat.CompactionFor(new Settings());
        Assert.Equal(SubscriptionAssignment.CompactAt(sizing), limits.CompactionTriggerTokens);

        // At the compaction point a routine turn still goes smart: the summary is written on that turn's model.
        var due = SubscriptionAssignment.CompactAt(sizing);
        Assert.False(seat.PickTurn(due, kind: TurnKind.Routine));
        Assert.Equal("qwen3-8-flash", seat.Model.Id);
        Assert.True(seat.PickTurn(due - 1000, kind: TurnKind.Routine));
        Assert.Equal("miniclaude", seat.Model.Id);
    }

    [Fact]
    public async Task ARouterSeatWithoutAPairStaysOnItsRoute_AndAShareHybridOnARouterSeatIsIgnored()
    {
        var shareOnly = new HybridAnswer(50, "low", null) { LocalModel = "miniclaude" };
        var seat = new SeatAssignmentSwitch(new Uri("https://broker/"), "t", null, "rev-local", false, Own, Own, new Settings(), new Clock())
        {
            Fetch = (_, _) => Task.FromResult(new SeatAssignmentAnswer(false, Smart, "rev-router") { Hybrid = shareOnly }),
        };
        Assert.True(await seat.CheckAsync(force: true, CancellationToken.None));
        Assert.Null(seat.Hybrid);
        for (var i = 0; i < 6; i++) Assert.False(seat.PickTurn(20000, kind: TurnKind.Routine));
        Assert.Equal("qwen3-8-flash", seat.Model.Id);
    }

    private static TurnContext Turn(IEnumerable<ToolCallContent> calls, params ToolResultMessage[] results) =>
        new(new AssistantMessage { Content = [.. calls], Model = "m" }, results, new AgentContext { SystemPrompt = "", Tools = [] }, []);

    private static ToolResultMessage Result(string tool, string text, bool error = false) =>
        new() { ToolCallId = "c-" + tool, ToolName = tool, Content = [new TextContent(text)], IsError = error };

    [Fact]
    public void ATurnIsRoutineWhenItsToolsReadLittleAndChangedNothing()
    {
        ToolCallContent Call(string name) => new("c-" + name, name, new JsonObject());
        Assert.Equal(TurnKind.Routine, TurnKinds.After(Turn([Call("chat__chat_ack")], Result("chat__chat_ack", "{\"acknowledged\":[1]}")), 4000));
        Assert.Equal(TurnKind.Routine, TurnKinds.After(Turn([Call("bash")], Result("bash", "On branch main\nnothing to commit")), 4000));
        Assert.Equal(TurnKind.Work, TurnKinds.After(Turn([Call("edit")], Result("edit", "ok")), 4000)); // a file changed
        Assert.Equal(TurnKind.Work, TurnKinds.After(Turn([Call("read")], Result("read", new string('x', 20000))), 4000)); // 5,000 tokens read
        Assert.Equal(TurnKind.Routine, TurnKinds.After(Turn([Call("read")], Result("read", new string('x', 20000))), 6000)); // the limit is the assignment's
        Assert.Equal(TurnKind.Failure, TurnKinds.After(Turn([Call("bash")], Result("bash", "Failed!  - Failed: 1", error: true)), 4000));
    }
}
