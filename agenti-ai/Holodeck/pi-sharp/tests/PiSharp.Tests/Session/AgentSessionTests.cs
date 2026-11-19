using PiSharp.Core.Agent;
using PiSharp.Core.Compaction;
using PiSharp.Core.Messages;
using PiSharp.Core.Session;
using PiSharp.Tests.Agent;
using static PiSharp.Tests.Agent.Fakes;

namespace PiSharp.Tests.Session;

public class AgentSessionTests
{
    private static bool Overflow(AssistantMessage message, int? _) =>
        message.StopReason == StopReason.Error
        && (message.ErrorMessage?.Contains("exceeds the available context size", StringComparison.OrdinalIgnoreCase) ?? false);

    private static AgentSession Seeded()
    {
        var session = SessionManager.InMemory("/tmp");
        for (var i = 0; i < 6; i++)
        {
            session.AppendMessage(new UserMessage($"turn-{i} " + new string('x', 32)));
            session.AppendMessage(new AssistantMessage
            {
                Content = [new TextContent($"ans-{i}")],
                Model = "m",
                StopReason = StopReason.Stop,
                Usage = new Usage(Input: 20, Output: 5, TotalTokens: 25),
            });
        }

        var context = new AgentContext { SystemPrompt = "sys" };
        return new AgentSession(
            session,
            context,
            new CompactionSettings(Enabled: true, ReserveTokens: 16_384, KeepRecentTokens: 8),
            Overflow);
    }

    [Fact]
    public async Task Run_PersistsSuccessfulTurn()
    {
        var agent = new AgentSession(SessionManager.InMemory("/tmp"), new AgentContext());
        var stream = new ScriptedStream().Then(Text("hello"));
        var events = await Collect(agent.Run([new UserMessage("hi")], Config(), stream.Stream));
        Assert.Contains(events, e => e is AgentEnd);
        Assert.Equal(2, agent.Session.Entries.Count);
        Assert.Equal("hi", ((UserMessage)((SessionMessageEntry)agent.Session.Entries[0]).Message).Text);
        Assert.Equal("hello", ((AssistantMessage)((SessionMessageEntry)agent.Session.Entries[1]).Message).Text);
    }

    [Fact]
    public async Task Overflow_CompactsAndRetriesOnce()
    {
        var agent = Seeded();
        var stream = new ScriptedStream()
            .Then(Error("prompt exceeds the available context size of 128"))
            .Then(Text("## Goal\nKeep going"))
            .Then(Text("recovered"));

        var events = await Collect(agent.Run([new UserMessage("continue the work")], Config(), stream.Stream));

        Assert.Equal(3, stream.Calls);
        var end = Assert.IsType<AgentEnd>(events[^1]);
        Assert.Equal("recovered", ((AssistantMessage)end.Messages[^1]).Text);
        Assert.Contains(agent.Session.Entries, e => e is CompactionEntry);
        Assert.DoesNotContain(agent.Context.Messages, m => m is AssistantMessage a && a.StopReason == StopReason.Error);
        Assert.IsType<CompactionSummaryMessage>(agent.Context.Messages[0]);
    }

    [Fact]
    public async Task LongToolRun_CompactsBetweenTurns()
    {
        // One prompt, many tool turns: the threshold must be checked before every turn, not only when
        // the prompt starts, or a headless run grows until the server refuses it.
        var agent = Seeded();
        var notices = new List<string>();
        agent.Notice = notices.Add;
        var model = Model with { ContextWindow = 16_384 + 1_000 };
        var stream = new ScriptedStream()
            // Built at stream time: usage only counts when the reply is newer than the prompt.
            .Then(() => ToolCalls(Call("c1", "no_such_tool")) with { Usage = new Usage(Input: 5_000, TotalTokens: 5_000) })
            // The cut lands inside the running turn; one in-place summary covers history and turn prefix alike.
            .Then(Text("## Goal\nKeep going"))
            .Then(Text("done"));

        var events = await Collect(agent.Run([new UserMessage("go")], Config() with { Model = model }, stream.Stream));

        Assert.Equal(3, stream.Calls);
        // The summary was asked on the agent's own last request, unchanged, with the instruction appended.
        var asked = stream.Contexts[1];
        Assert.Equal(stream.Contexts[0].SystemPrompt, asked.SystemPrompt);
        Assert.Equal(stream.Contexts[0].Messages, asked.Messages.Take(asked.Messages.Count - 1));
        Assert.StartsWith(CompactionPrompts.InPlaceMarker, Assert.IsType<UserMessage>(asked.Messages[^1]).Text);
        Assert.Contains(agent.Session.Entries, e => e is CompactionEntry);
        Assert.Contains(notices, n => n.StartsWith("compacted between turns", StringComparison.Ordinal));
        Assert.IsType<AgentEnd>(events[^1]);
        Assert.IsType<CompactionSummaryMessage>(agent.Context.Messages[0]);
        Assert.Contains(agent.Context.Messages, m => m is ToolResultMessage);
        Assert.Equal("done", Assert.IsType<AssistantMessage>(agent.Context.Messages[^1]).Text);
    }

    [Fact]
    public async Task AnInPlaceAnswerThatIsNotASummaryFallsBackToTheTranscript()
    {
        var agent = Seeded();
        var notices = new List<string>();
        agent.Notice = notices.Add;
        var model = Model with { ContextWindow = 16_384 + 1_000 };
        var stream = new ScriptedStream()
            .Then(() => ToolCalls(Call("c1", "no_such_tool")) with { Usage = new Usage(Input: 5_000, TotalTokens: 5_000) })
            .Then(ToolCalls(Call("c2", "no_such_tool"))) // the in-place request answered with a tool call
            .Then(Text("## Goal\nKeep going"))
            .Then(Text("## Turn prefix\nCalled a tool"))
            .Then(Text("done"));

        var events = await Collect(agent.Run([new UserMessage("go")], Config() with { Model = model }, stream.Stream));

        Assert.IsType<AgentEnd>(events[^1]);
        Assert.Equal(5, stream.Calls);
        Assert.Contains(notices, n => n.StartsWith("in-place summary failed", StringComparison.Ordinal));
        Assert.Equal(CompactionPrompts.System, stream.Contexts[2].SystemPrompt);
        Assert.Contains(agent.Session.Entries, e => e is CompactionEntry);
    }

    [Fact]
    public async Task ReplacementContext_IsTheOneThatGetsCompacted()
    {
        // FactorySteering starts each inbox task from a fresh context. The session used to keep measuring
        // its own, frozen at the first task, so the second task grew until the server refused it.
        var agent = Seeded();
        var notices = new List<string>();
        agent.Notice = notices.Add;
        var model = Model with { ContextWindow = 16_384 + 1_000 };
        var replaced = false;
        var steered = false;
        var config = Config() with
        {
            Model = model,
            PrepareNextTurn = _ =>
            {
                if (replaced) return ValueTask.FromResult<TurnUpdate?>(null);
                replaced = true;
                return ValueTask.FromResult<TurnUpdate?>(new TurnUpdate(Context: new AgentContext { SystemPrompt = "sys" }));
            },
            GetSteeringMessages = () =>
            {
                if (!replaced || steered) return ValueTask.FromResult<IReadOnlyList<Message>>([]);
                steered = true;
                return ValueTask.FromResult<IReadOnlyList<Message>>([new UserMessage("task two " + new string('y', 64))]);
            },
        };
        var stream = new ScriptedStream()
            .Then(() => ToolCalls(Call("c1", "no_such_tool")) with { Usage = new Usage(Input: 10, TotalTokens: 10) })
            .Then(() => ToolCalls(Call("c2", "no_such_tool")) with { Usage = new Usage(Input: 5_000, TotalTokens: 5_000) })
            .Then(Text("## Goal\nTask two"))
            .Then(Text("## Turn prefix\nCalled a tool"))
            .Then(Text("done"));

        var events = await Collect(agent.Run([new UserMessage("task one")], config, stream.Stream));

        Assert.Contains(notices, n => n.StartsWith("compacted between turns", StringComparison.Ordinal));
        var compaction = Assert.Single(agent.Session.Entries.OfType<CompactionEntry>());
        Assert.DoesNotContain("turn-", compaction.Summary, StringComparison.Ordinal);
        Assert.IsType<AgentEnd>(events[^1]);
        // The request after compaction is built from the compacted context, and nothing of task one is in it.
        var last = stream.Contexts[^1];
        Assert.IsType<CompactionSummaryMessage>(agent.Context.Messages[0]);
        Assert.DoesNotContain(last.Messages, m => m is UserMessage u && u.Text.StartsWith("task one", StringComparison.Ordinal));
        Assert.DoesNotContain(last.Messages, m => m is UserMessage u && u.Text.StartsWith("turn-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Overflow_WithNothingToCompact_Throws()
    {
        var session = SessionManager.InMemory("/tmp");
        var agent = new AgentSession(
            session,
            new AgentContext(),
            new CompactionSettings(KeepRecentTokens: 1_000_000),
            Overflow);
        var stream = new ScriptedStream().Then(Error("exceeds the available context size"));

        var ex = await Assert.ThrowsAsync<ContextOverflowException>(async () =>
        {
            await foreach (var _ in agent.Run([new UserMessage("hi")], Config(), stream.Stream)) { }
        });
        Assert.Contains("nothing left to compact", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Overflow_RetriesUpToTheBudgetAndNeverLoopsForever()
    {
        // Every stream call overflows: the model can never fit the turn. Recovery must be
        // bounded — at most MaxOverflowRetries compaction-and-retry rounds — and must surface
        // ContextOverflowException instead of dying silently or re-sending forever.
        var agent = Seeded();
        var stream = new ScriptedStream()
            .Then(Error("prompt exceeds the available context size of 128"))   // first pass overflows
            .Then(Text("## Goal summary"))                                     // round 1 summary
            .Then(Error("prompt exceeds the available context size of 128"))   // round 1 retry overflows
            .Then(Text("## Goal summary"))                                     // round 2 summary
            .Then(Error("prompt exceeds the available context size of 128"));  // round 2 retry overflows

        var ex = await Assert.ThrowsAsync<ContextOverflowException>(async () =>
        {
            await foreach (var _ in agent.Run([new UserMessage("continue the work")], Config(), stream.Stream)) { }
        });

        Assert.Contains("compaction", ex.Message, StringComparison.OrdinalIgnoreCase);
        // 1 first-pass + (1 summary + 1 retry) per round, for exactly MaxOverflowRetries rounds.
        Assert.Equal(1 + 2 * AgentSession.MaxOverflowRetries, stream.Calls);
    }

    [Fact]
    public async Task ProactiveCompact_FiresBelowReactiveThreshold()
    {
        // wi-59: after a compaction the floor is raised to after + KeepRecentTokens/2.
        // A second compaction should fire when the estimate reaches that floor — which is
        // set BELOW the reactive (window − reserve) threshold — so a turn that would have
        // grown the context toward the wall is compacted early, not at refusal.
        //
        // Setup:
        //   ContextWindow = 10_000, ReserveTokens = 5_000 → reactive threshold = 5_000
        //   KeepRecentTokens = 8 → floor after compaction 1 ≈ after + 4
        //
        // The seeded context (6 × "turn-i " + 32 x's) + system prompt ≈ ~500 tokens.
        // Turn 1 tool call with usage 5_000 → estimate ≈ 5_500 > 5_000 → reactive compaction.
        // After compaction 1: context ≈ ~100 tokens (summary + keep-recent), floor ≈ 104.
        // Turn 2 tool call with usage 5_000 → estimate ≈ 5_100.
        //   Reactive gate: 5_100 > 5_000 AND 5_100 >= floor(104) → fires (same as before).
        //
        // To make the test discriminate, use a TIGHTER window so the reactive threshold
        // is much higher than the floor:
        //   ContextWindow = 20_000, ReserveTokens = 16_384 → reactive threshold = 3_616
        //   After compaction 1: context ≈ ~100, floor ≈ 104.
        //   Turn 2: estimate ≈ ~600 (no large usage) → 600 < 3_616 (reactive doesn't fire)
        //   But 600 >= 104 (proactive fires) → second compaction.
        //
        // Without the fix: only 1 compaction (reactive only).
        // With the fix: 2 compactions (reactive + proactive).
        var session = SessionManager.InMemory("/tmp");
        for (var i = 0; i < 6; i++)
        {
            session.AppendMessage(new UserMessage($"turn-{i} " + new string('x', 32)));
            session.AppendMessage(new AssistantMessage
            {
                Content = [new TextContent($"ans-{i}")],
                Model = "m",
                StopReason = StopReason.Stop,
                Usage = new Usage(Input: 20, Output: 5, TotalTokens: 25),
            });
        }
        var agent = new AgentSession(
            session,
            new AgentContext { SystemPrompt = "sys" },
            new CompactionSettings(Enabled: true, ReserveTokens: 16_384, KeepRecentTokens: 8),
            Overflow);

        var notices = new List<string>();
        agent.Notice = notices.Add;
        // Reactive threshold: 20_000 - 16_384 = 3_616
        var model = Model with { ContextWindow = 20_000 };
        var stream = new ScriptedStream()
            // turn 1: tool call with large usage → estimate crosses 3_616 → reactive compaction
            .Then(() => ToolCalls(Call("c1", "no_such_tool")) with { Usage = new Usage(Input: 5_000, TotalTokens: 5_000) })
            // compaction 1 (reactive): one in-place summary
            .Then(Text("## Goal\nKeep going"))
            // turn 2: tool call with small usage → estimate reaches the floor (~104)
            // but stays well below the reactive threshold (3_616)
            .Then(() => ToolCalls(Call("c2", "no_such_tool")) with { Usage = new Usage(Input: 200, TotalTokens: 200) })
            // compaction 2 (proactive, wi-59): only fires when the floor gate is present
            .Then(Text("## Goal\nMore work"))
            // turn 3: done
            .Then(Text("done"));

        var events = await Collect(agent.Run([new UserMessage("go")], Config() with { Model = model }, stream.Stream));

        Assert.IsType<AgentEnd>(events[^1]);

        // With the proactive gate: 2 compactions. Without: 1.
        var compactions = agent.Session.Entries.OfType<CompactionEntry>().ToList();
        Assert.True(compactions.Count >= 2, $"expected >=2 compactions (reactive + proactive), got {compactions.Count}");
        Assert.Contains(notices, n => n.StartsWith("compacted between turns", StringComparison.Ordinal));
        Assert.DoesNotContain(notices, n => n.Contains("overflow"));
    }

    [Fact]
    public async Task ConvertToLlm_SendsCompactionSummaryAsUser()
    {
        var session = SessionManager.InMemory("/tmp");
        session.AppendMessage(new UserMessage("old"));
        session.AppendMessage(new AssistantMessage { Content = [new TextContent("a")], Model = "m", StopReason = StopReason.Stop });
        var kept = session.AppendMessage(new UserMessage("kept"));
        session.AppendCompaction("prior work", kept, 10);
        var agent = new AgentSession(session, new AgentContext());
        var stream = new ScriptedStream().Then(Text("ok"));
        await Collect(agent.Run([new UserMessage("next")], Config(), stream.Stream));
        var sent = stream.Contexts[0].Messages;
        Assert.Contains(sent, m => m is UserMessage u && u.Text.Contains("<summary>"));
    }
}
