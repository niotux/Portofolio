// A seat whose assignment switches in place mid-run, and a compaction that keeps failing (agent-maker-2,
// 2026-10-02): the summary went out on the run's first model, the broker refused it with 409, and every turn
// asked a fresh save turn and failed again, 82 times, while the turns themselves went through.
using PiSharp.Core.Agent;
using PiSharp.Core.Compaction;
using PiSharp.Core.Models;
using PiSharp.Core.Messages;
using PiSharp.Core.Session;
using PiSharp.Tests.Agent;
using static PiSharp.Tests.Agent.Fakes;

namespace PiSharp.Tests.Session;

public class CompactionModelSwitchTests
{
    private static readonly ModelConfig Small = Model with { ContextWindow = 16_384 + 1_000 };
    private static readonly ModelConfig Switched = Small with { Id = "switched-model" };

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

    /// <summary>Summaries go to <see cref="Summarize"/>, everything else to the scripted agent; records the model
    /// each request named.</summary>
    private sealed class Split
    {
        public ScriptedStream Agent { get; } = new();
        public List<string> SummaryModels { get; } = [];
        public List<string> AgentModels { get; } = [];
        public Func<AssistantMessage> Summarize { get; set; } = () => Text("## Task\nsummary");

        public IAsyncEnumerable<AssistantMessageEvent> Stream(
            ModelConfig model, LlmContext context, StreamOptions options, CancellationToken cancellationToken)
        {
            if (context.SystemPrompt == CompactionPrompts.System
                || context.Messages is [.., UserMessage last] && last.Text.StartsWith(CompactionPrompts.InPlaceMarker, StringComparison.Ordinal))
            {
                SummaryModels.Add(model.Id);
                return new ScriptedStream().Then(Summarize()).Stream(model, context, options, cancellationToken);
            }
            AgentModels.Add(model.Id);
            return Agent.Stream(model, context, options, cancellationToken);
        }
    }

    private static AgentLoopConfig SwitchOnFirstPoll()
    {
        var polls = 0;
        return Config() with
        {
            Model = Small,
            PrepareNextTurn = _ => ValueTask.FromResult<TurnUpdate?>(++polls == 1 ? new TurnUpdate(Model: Switched) : null),
        };
    }

    [Fact]
    public async Task TheSummaryAfterASaveTurnGoesOutOnTheModelSwitchedInPlace()
    {
        var agent = Seeded();
        agent.Hooks = new CompactionHooks { SavePrompt = CompactionHooks.DefaultSavePrompt };
        var split = new Split();
        split.Agent
            .Then(() => ToolCalls(Call("c1", "no_such_tool")) with { Usage = new Usage(Input: 5_000, TotalTokens: 5_000) })
            .Then(Text("saved"))
            .Then(Text("done"));

        var events = await Collect(agent.Run([new UserMessage("go")], SwitchOnFirstPoll(), split.Stream));

        Assert.IsType<AgentEnd>(events[^1]);
        Assert.Equal([Small.Id, Switched.Id, Switched.Id], split.AgentModels);
        Assert.NotEmpty(split.SummaryModels);
        Assert.All(split.SummaryModels, id => Assert.Equal(Switched.Id, id));
        Assert.Single(agent.Session.Entries.OfType<CompactionEntry>());
    }

    [Fact]
    public async Task AFailedCompactionBacksOffAndAsksNoSecondSaveTurn()
    {
        var agent = Seeded();
        var failures = new List<string>();
        agent.Hooks = new CompactionHooks
        {
            SavePrompt = CompactionHooks.DefaultSavePrompt,
            OnCompactionFailed = failures.Add,
        };
        var notices = new List<string>();
        agent.Notice = notices.Add;
        var split = new Split { Summarize = () => Error("Subscription request refused by the broker (HTTP 409)") };
        var big = new Usage(Input: 5_000, TotalTokens: 5_000);
        split.Agent
            .Then(() => ToolCalls(Call("c1", "no_such_tool")) with { Usage = big })
            .Then(Text("saved"))
            .Then(() => ToolCalls(Call("c2", "no_such_tool")) with { Usage = big })
            .Then(() => ToolCalls(Call("c3", "no_such_tool")) with { Usage = big })
            .Then(Text("done"));

        var events = await Collect(agent.Run([new UserMessage("go")], Config() with { Model = Small }, split.Stream));

        Assert.IsType<AgentEnd>(events[^1]);
        // One save turn, one failed attempt; the later turns are inside the back-off and neither ask again.
        Assert.Single(notices, n => n.Contains("save-before-compact turn first", StringComparison.Ordinal));
        var failed = Assert.Single(failures);
        Assert.Contains("409", failed, StringComparison.Ordinal);
        Assert.Contains(notices, n => n.Contains("next try after 60s", StringComparison.Ordinal));
        Assert.Empty(agent.Session.Entries.OfType<CompactionEntry>());
    }

    [Fact]
    public async Task AfterTheBackOffTheRetryCompactsWithoutAnotherSaveTurn()
    {
        var agent = Seeded();
        agent.CompactionRetryDelay = TimeSpan.Zero;
        agent.Hooks = new CompactionHooks { SavePrompt = CompactionHooks.DefaultSavePrompt };
        var notices = new List<string>();
        agent.Notice = notices.Add;
        var split = new Split();
        // Every summary fails until the turn after the save (in place and transcript alike), then succeeds.
        split.Summarize = () => split.Agent.Calls < 3 ? Error("broker down") : Text("## Task\nsummary");
        var big = new Usage(Input: 5_000, TotalTokens: 5_000);
        split.Agent
            .Then(() => ToolCalls(Call("c1", "no_such_tool")) with { Usage = big })
            .Then(Text("saved"))
            .Then(() => ToolCalls(Call("c2", "no_such_tool")) with { Usage = big })
            .Then(Text("done"));

        var events = await Collect(agent.Run([new UserMessage("go")], Config() with { Model = Small }, split.Stream));

        Assert.IsType<AgentEnd>(events[^1]);
        Assert.Single(notices, n => n.Contains("save-before-compact turn first", StringComparison.Ordinal));
        Assert.Single(agent.Session.Entries.OfType<CompactionEntry>());
    }
}
