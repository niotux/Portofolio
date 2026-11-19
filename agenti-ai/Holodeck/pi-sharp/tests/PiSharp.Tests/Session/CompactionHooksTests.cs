// The attached-seat compaction harness: carry-forward note, save-before-compact, compaction log, and the
// loop's trailing messages it rides on.
using System.Text.RegularExpressions;
using PiSharp.Core.Agent;
using PiSharp.Core.Compaction;
using PiSharp.Core.Models;
using PiSharp.Core.Messages;
using PiSharp.Core.Session;
using PiSharp.Tests.Agent;
using static PiSharp.Tests.Agent.Fakes;

namespace PiSharp.Tests.Session;

public class CompactionHooksTests
{
    private const string Note = "[open tasks carried across compaction]\n1. message 59494 from @digital-scribe";

    private static readonly ModelConfig Small = Model with { ContextWindow = 16_384 + 1_000 };

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
        return new AgentSession(
            session,
            new AgentContext { SystemPrompt = "sys" },
            new CompactionSettings(Enabled: true, ReserveTokens: 16_384, KeepRecentTokens: 8));
    }

    /// <summary>Routes summarization requests (system prompt <see cref="CompactionPrompts.System"/>) to an endless
    /// summarizer and everything else to the scripted agent, recording the order of both.</summary>
    private sealed class Split
    {
        public ScriptedStream Agent { get; } = new();
        public List<string> Order { get; } = [];
        public List<LlmContext> Summaries { get; } = [];

        public IAsyncEnumerable<AssistantMessageEvent> Stream(
            ModelConfig model, LlmContext context, StreamOptions options, CancellationToken cancellationToken)
        {
            if (context.SystemPrompt == CompactionPrompts.System
                || context.Messages is [.., UserMessage last] && last.Text.StartsWith(CompactionPrompts.InPlaceMarker, StringComparison.Ordinal))
            {
                Order.Add("summary");
                Summaries.Add(context);
                return new ScriptedStream().Then(Text("## Task\nsummary")).Stream(model, context, options, cancellationToken);
            }
            Order.Add("agent");
            return Agent.Stream(model, context, options, cancellationToken);
        }
    }

    private static string? UserText(Message m) => m is UserMessage u ? u.Text : null;

    private static AgentLoopConfig SteerOnce(Message message)
    {
        // Poll 1 is at the start of the run; poll 2 follows the first turn, before the compaction.
        var polls = 0;
        return Config() with
        {
            Model = Small,
            GetSteeringMessages = () =>
            {
                if (++polls != 2) return ValueTask.FromResult<IReadOnlyList<Message>>([]);
                return ValueTask.FromResult<IReadOnlyList<Message>>([message]);
            },
        };
    }

    [Fact]
    public async Task TheCarryForwardNoteComesLastAfterTheNewerInboxItemsAndTheCompactionIsLogged()
    {
        var agent = Seeded();
        var notices = new List<string>();
        agent.Notice = notices.Add;
        agent.Hooks = new CompactionHooks { Actor = "agent-maker-2", CarryForward = () => Note, OpenTaskCount = () => 1 };
        var split = new Split();
        split.Agent
            .Then(() => ToolCalls(Call("c1", "no_such_tool")) with { Usage = new Usage(Input: 5_000, TotalTokens: 5_000) })
            .Then(Text("done"));

        var events = await Collect(agent.Run([new UserMessage("go")], SteerOnce(new UserMessage("newer inbox item")), split.Stream));

        Assert.IsType<AgentEnd>(events[^1]);
        Assert.Equal(["agent", "summary"], split.Order.Take(2));
        Assert.Equal("agent", split.Order[^1]);
        var sent = split.Agent.Contexts[^1].Messages;
        Assert.Equal(Note, UserText(sent[^1]));
        Assert.Equal("newer inbox item", UserText(sent[^2]));
        // Persisted, so a later compaction summarizes it like any other message.
        Assert.Contains(agent.Session.Entries, e => e is SessionMessageEntry { Message: UserMessage { Text: Note } });

        var log = Assert.Single(agent.Session.Entries.OfType<CompactionLogEntry>());
        Assert.Equal("agent-maker-2", log.Actor);
        Assert.Equal("between-turns", log.Trigger);
        Assert.Equal(1, log.OpenTasks);
        Assert.Equal("off", log.SaveBeforeCompact);
        Assert.True(log.TokensBefore > log.TokensAfter, $"{log.TokensBefore} -> {log.TokensAfter}");
        var line = Assert.Single(notices, n => n.StartsWith("compaction log:", StringComparison.Ordinal));
        Assert.Matches(new Regex(
            @"^compaction log: actor=agent-maker-2 at=\d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ trigger=between-turns tokens=\d+->\d+ open_tasks=1 save_before_compact=off$"),
            line);
    }

    /// <summary>wi-60 wiring: on a real between-turns compaction through the loop, OnCompactionOccurred fires
    /// with the open-tasks note the seat is carrying, so a running seat actually computes the carried-across
    /// mark (the hook routes it to FactorySteering.NoteCompactionOccurred in CliHost).</summary>
    [Fact]
    public async Task TheCompactionOccurredHookFiresOnARealCompactionWithTheCarriedNote()
    {
        var agent = Seeded();
        var notes = new List<string?>();
        var openNote = "[open tasks carried across compaction]\n1. message 59494 from @digital-scribe";
        agent.Hooks = new CompactionHooks
        {
            Actor = "agent-maker-2",
            CarryForward = () => openNote,
            OpenTaskCount = () => 1,
            OnCompactionOccurred = notes.Add,
        };
        var split = new Split();
        split.Agent
            .Then(() => ToolCalls(Call("c1", "no_such_tool")) with { Usage = new Usage(Input: 5_000, TotalTokens: 5_000) })
            .Then(Text("done"));

        await Collect(agent.Run([new UserMessage("go")], Config() with { Model = Small }, split.Stream));

        // Exactly one completed compaction fired the hook once, with the note carried across it.
        Assert.Equal("between-turns", Assert.Single(agent.Session.Entries.OfType<CompactionLogEntry>()).Trigger);
        Assert.Equal([openNote], notes);
    }

    /// <summary>plan-pi-sharp-codex item 4: the PreCompact and PostCompact command hooks fire on a real compaction, and what
    /// PostCompact adds goes with the note the seat carries after it.</summary>
    [Fact]
    public async Task PreCompactAndPostCompactHooksFireOnARealCompaction_AndPostCompactsContextIsCarried()
    {
        if (OperatingSystem.IsWindows()) return;
        var dir = Directory.CreateTempSubdirectory("pi-compact-hooks-").FullName;
        try
        {
            var before = Path.Combine(dir, "pre.json");
            var after = Path.Combine(dir, "post.json");
            var agent = Seeded();
            agent.Hooks = new CompactionHooks { Actor = "coder-06-pi3" }; // an attached seat: compactions are logged
            var split = new Split();
            split.Agent
                .Then(() => ToolCalls(Call("c1", "no_such_tool")) with { Usage = new Usage(Input: 5_000, TotalTokens: 5_000) })
                .Then(Text("done"));
            var config = Config() with
            {
                Model = Small,
                CommandHooks = new CommandHookRunner(new Dictionary<CommandHookEvent, IReadOnlyList<CommandHook>>
                {
                    [CommandHookEvent.PreCompact] = [new($"cat > '{before}'", "v1", TimeSpan.FromSeconds(5))],
                    [CommandHookEvent.PostCompact] = [new($"cat > '{after}'; printf '{{\"additional_context\":\"re-read the epic memory\"}}'", "v1", TimeSpan.FromSeconds(5))],
                }),
            };

            await Collect(agent.Run([new UserMessage("go")], config, split.Stream));

            Assert.Single(agent.Session.Entries.OfType<CompactionLogEntry>());
            Assert.Contains("tokens_before", File.ReadAllText(before));
            Assert.Contains("## Task", File.ReadAllText(after)); // PostCompact is told the summary
            Assert.Contains(agent.Context.Messages.OfType<UserMessage>(), m => m.Text.Contains("[hook] re-read the epic memory", StringComparison.Ordinal));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    /// <summary>A hook that throws is reported and never fails the compaction or the run.</summary>
    [Fact]
    public async Task AThrowingCompactionOccurredHookNeverFailsTheCompaction()
    {
        var agent = Seeded();
        var notices = new List<string>();
        agent.Notice = notices.Add;
        agent.Hooks = new CompactionHooks
        {
            CarryForward = () => Note,
            OnCompactionOccurred = _ => throw new InvalidOperationException("note layer down"),
        };
        var split = new Split();
        split.Agent
            .Then(() => ToolCalls(Call("c1", "no_such_tool")) with { Usage = new Usage(Input: 5_000, TotalTokens: 5_000) })
            .Then(Text("done"));

        var events = await Collect(agent.Run([new UserMessage("go")], Config() with { Model = Small }, split.Stream));

        Assert.IsType<AgentEnd>(events[^1]);
        Assert.Single(agent.Session.Entries.OfType<CompactionLogEntry>());
        Assert.Contains(notices, n => n == "compaction occurred hook failed: note layer down");
    }

    [Fact]
    public async Task WithoutHooksNothingIsLoggedAndNoNoteIsAdded()
    {
        var agent = Seeded();
        var notices = new List<string>();
        agent.Notice = notices.Add;
        var split = new Split();
        split.Agent
            .Then(() => ToolCalls(Call("c1", "no_such_tool")) with { Usage = new Usage(Input: 5_000, TotalTokens: 5_000) })
            .Then(Text("done"));

        await Collect(agent.Run([new UserMessage("go")], SteerOnce(new UserMessage("newer inbox item")), split.Stream));

        Assert.Contains(agent.Session.Entries, e => e is CompactionEntry);
        Assert.DoesNotContain(agent.Session.Entries, e => e is CompactionLogEntry);
        Assert.DoesNotContain(notices, n => n.StartsWith("compaction log:", StringComparison.Ordinal));
        Assert.Equal("newer inbox item", UserText(split.Agent.Contexts[^1].Messages[^1]));
    }

    [Fact]
    public async Task AFailingCarryForwardNeverFailsTheCompaction()
    {
        var agent = Seeded();
        var notices = new List<string>();
        agent.Notice = notices.Add;
        agent.Hooks = new CompactionHooks { CarryForward = () => throw new InvalidOperationException("chat down") };
        var split = new Split();
        split.Agent
            .Then(() => ToolCalls(Call("c1", "no_such_tool")) with { Usage = new Usage(Input: 5_000, TotalTokens: 5_000) })
            .Then(Text("done"));

        var events = await Collect(agent.Run([new UserMessage("go")], Config() with { Model = Small }, split.Stream));

        Assert.IsType<AgentEnd>(events[^1]);
        Assert.Contains(agent.Session.Entries, e => e is CompactionEntry);
        Assert.Contains(notices, n => n == "carry-forward note after compaction failed: chat down");
        var log = Assert.Single(agent.Session.Entries.OfType<CompactionLogEntry>());
        Assert.Null(log.OpenTasks);
    }

    [Fact]
    public async Task SaveBeforeCompactRunsOneTurnThenCompactsAndTheSaveReplyIsNotTheAnswer()
    {
        var agent = Seeded();
        agent.Hooks = new CompactionHooks
        {
            SavePrompt = CompactionHooks.DefaultSavePrompt,
            CarryForward = () => Note,
            OpenTaskCount = () => 2,
        };
        var stopSaw = new List<string>();
        var config = Config() with
        {
            Model = Small,
            ShouldStopAfterTurn = turn =>
            {
                stopSaw.Add(turn.Message.Text);
                return ValueTask.FromResult(false);
            },
        };
        var split = new Split();
        split.Agent
            .Then(() => ToolCalls(Call("c1", "no_such_tool")) with { Usage = new Usage(Input: 5_000, TotalTokens: 5_000) })
            .Then(Text("saved"))
            .Then(Text("done"));

        var events = await Collect(agent.Run([new UserMessage("go")], config, split.Stream));

        Assert.IsType<AgentEnd>(events[^1]);
        // agent turn, the save turn, the compaction, then the work continues.
        Assert.Equal(["agent", "agent", "summary"], split.Order.Take(3));
        Assert.Equal("agent", split.Order[^1]);
        Assert.Equal(3, split.Agent.Calls);
        Assert.Equal(CompactionHooks.DefaultSavePrompt, UserText(split.Agent.Contexts[1].Messages[^1]));
        var after = split.Agent.Contexts[2].Messages;
        Assert.Equal(Note, UserText(after[^1]));
        Assert.Equal(CompactionHooks.ContinueAfterSave, UserText(after[^2]));
        // The inner stop hook (the factory's reply poster) never saw the "saved" reply.
        Assert.DoesNotContain("saved", stopSaw);
        Assert.Contains("done", stopSaw);

        Assert.Single(agent.Session.Entries.OfType<CompactionEntry>());
        var log = Assert.Single(agent.Session.Entries.OfType<CompactionLogEntry>());
        Assert.Equal("ran", log.SaveBeforeCompact);
        Assert.Equal(2, log.OpenTasks);
    }

    /// <summary>The save turn does what the prompt asks, a notebook call, so the compaction runs right after the
    /// call and "saved" arrives on the far side of it. It is still not the answer (pi4, 2026-09-29 21:48Z: that
    /// "saved" ended pi-sharp-codeintel-mcp-3 mid-debug and the seat sat idle holding the item).</summary>
    [Fact]
    public async Task ASavedReplyAfterASaveTurnWithToolCallsIsNotTheAnswer()
    {
        var agent = Seeded();
        agent.Hooks = new CompactionHooks { SavePrompt = CompactionHooks.DefaultSavePrompt };
        var stopSaw = new List<string>();
        var config = Config() with
        {
            Model = Small,
            ShouldStopAfterTurn = turn =>
            {
                stopSaw.Add(turn.Message.Text);
                return ValueTask.FromResult(false);
            },
        };
        var split = new Split();
        split.Agent
            .Then(() => ToolCalls(Call("c1", "no_such_tool")) with { Usage = new Usage(Input: 5_000, TotalTokens: 5_000) })
            // Distinct arguments, so the identical-batch loop guard does not end the run.
            .Then(ToolCalls(Call("c2", "no_such_tool", new() { ["n"] = 2 })))   // the save: a notebook write
            .Then(ToolCalls(Call("c3", "no_such_tool", new() { ["n"] = 3 })))   // after the compaction: the save retried
            .Then(Text("saved"))
            .Then(Text("done"));

        var events = await Collect(agent.Run([new UserMessage("go")], config, split.Stream));

        Assert.IsType<AgentEnd>(events[^1]);
        Assert.Equal(5, split.Agent.Calls);
        Assert.Equal(CompactionHooks.ContinueAfterSave, UserText(split.Agent.Contexts[4].Messages[^1]));
        Assert.DoesNotContain("saved", stopSaw);
        Assert.Contains("done", stopSaw);
        Assert.Equal("ran", agent.Session.Entries.OfType<CompactionLogEntry>().First().SaveBeforeCompact);
    }

    /// <summary>A real answer after the save is the answer: no extra turn is forced.</summary>
    [Fact]
    public async Task ARealAnswerAfterASaveTurnWithToolCallsEndsTheTask()
    {
        var agent = Seeded();
        agent.Hooks = new CompactionHooks { SavePrompt = CompactionHooks.DefaultSavePrompt };
        var stopSaw = new List<string>();
        var config = Config() with
        {
            Model = Small,
            ShouldStopAfterTurn = turn =>
            {
                stopSaw.Add(turn.Message.Text);
                return ValueTask.FromResult(false);
            },
        };
        var split = new Split();
        split.Agent
            .Then(() => ToolCalls(Call("c1", "no_such_tool")) with { Usage = new Usage(Input: 5_000, TotalTokens: 5_000) })
            .Then(ToolCalls(Call("c2", "no_such_tool")))
            .Then(Text("CI3: all seven tests pass at abc1234, filed REVIEW"));

        var events = await Collect(agent.Run([new UserMessage("go")], config, split.Stream));

        Assert.IsType<AgentEnd>(events[^1]);
        Assert.Equal(3, split.Agent.Calls);
        Assert.Contains("CI3: all seven tests pass at abc1234, filed REVIEW", stopSaw);
    }

    [Theory]
    [InlineData("saved", true)]
    [InlineData("Saved.", true)]
    [InlineData("**Notes saved**", true)]
    [InlineData("All state saved!", true)]
    [InlineData("done", false)]
    [InlineData("NO_REPLY", false)]
    [InlineData("I saved the anchor evidence on the item and filed REVIEW with the suite counts", false)]
    public void OnlyAShortReplyNamingTheSaveIsASaveAcknowledgement(string text, bool expected) =>
        Assert.Equal(expected, AgentSession.IsSaveAcknowledgement(text));

    [Fact]
    public async Task SaveBeforeCompactIsSkippedWhenTheSaveTurnWouldNotFit()
    {
        var agent = Seeded();
        var notices = new List<string>();
        agent.Notice = notices.Add;
        agent.Hooks = new CompactionHooks { SavePrompt = CompactionHooks.DefaultSavePrompt };
        var split = new Split();
        split.Agent
            // 15k of a 17.4k window: a 4k save turn would overflow it.
            .Then(() => ToolCalls(Call("c1", "no_such_tool")) with { Usage = new Usage(Input: 15_000, TotalTokens: 15_000) })
            .Then(Text("done"));

        var events = await Collect(agent.Run([new UserMessage("go")], Config() with { Model = Small }, split.Stream));

        Assert.IsType<AgentEnd>(events[^1]);
        Assert.Equal(2, split.Agent.Calls);
        Assert.Equal("summary", split.Order[1]);
        Assert.DoesNotContain(split.Agent.Contexts.SelectMany(c => c.Messages), m => UserText(m) == CompactionHooks.DefaultSavePrompt);
        Assert.Contains(notices, n => n.StartsWith("save-before-compact skipped", StringComparison.Ordinal));
        Assert.Equal("skipped", Assert.Single(agent.Session.Entries.OfType<CompactionLogEntry>()).SaveBeforeCompact);
    }

    [Fact]
    public async Task TheLoopAddsTrailingMessagesAfterTheSteeringMessages()
    {
        var prepared = false;
        var config = Config() with
        {
            PrepareNextTurn = _ =>
            {
                if (prepared) return ValueTask.FromResult<TurnUpdate?>(null);
                prepared = true;
                return ValueTask.FromResult<TurnUpdate?>(new TurnUpdate(Trailing: [new UserMessage("trailing")]));
            },
            GetSteeringMessages = () => ValueTask.FromResult<IReadOnlyList<Message>>(prepared ? [] : [new UserMessage("steered")]),
        };
        var stream = new ScriptedStream().Then(ToolCalls(Call("c1", "no_such_tool"))).Then(Text("done"));

        await Collect(AgentLoop.Run([new UserMessage("go")], new AgentContext(), config, stream.Stream));

        var sent = stream.Contexts[1].Messages;
        Assert.Equal("trailing", UserText(sent[^1]));
        Assert.Equal("steered", UserText(sent[^2]));
    }

    [Fact]
    public void ACompactionLogEntryRoundTripsThroughTheSessionFile()
    {
        using var tmp = new PiSharp.Tests.Tools.TempWorkspace();
        var session = SessionManager.Create(tmp.Path, tmp.Path);
        session.AppendMessage(new UserMessage("u"));
        session.AppendMessage(new AssistantMessage { Content = [new TextContent("a")], Model = "m", StopReason = StopReason.Stop });
        session.AppendCompactionLog(new CompactionLogEntry
        {
            Id = "",
            Actor = "agent-maker-2",
            Trigger = "between-turns",
            TokensBefore = 170_000,
            TokensAfter = 12_000,
            OpenTasks = 5,
            SaveBeforeCompact = "ran",
        });

        var loaded = SessionManager.Open(session.SessionFile!);
        var log = Assert.Single(loaded.Entries.OfType<CompactionLogEntry>());
        Assert.Equal("agent-maker-2", log.Actor);
        Assert.Equal("between-turns", log.Trigger);
        Assert.Equal(170_000, log.TokensBefore);
        Assert.Equal(12_000, log.TokensAfter);
        Assert.Equal(5, log.OpenTasks);
        Assert.Equal("ran", log.SaveBeforeCompact);
        // It carries no message.
        Assert.Equal(2, loaded.BuildContextMessages().Count);
    }

    [Fact]
    public void ALogEntryAfterACompactionDoesNotCountAsNewWork()
    {
        var session = SessionManager.InMemory("/tmp");
        session.AppendMessage(new UserMessage("u"));
        var kept = session.AppendMessage(new UserMessage("kept"));
        session.AppendCompaction("S", kept, 10);
        session.AppendCompactionLog(new CompactionLogEntry { Id = "", Trigger = "between-turns", SaveBeforeCompact = "off" });

        Assert.Null(Compactor.Prepare(session.Entries, new CompactionSettings(KeepRecentTokens: 1)));
    }
}
