using PiSharp.Cli;
using PiSharp.Core.Agent;
using PiSharp.Core.Config;
using PiSharp.Core.Factory;
using PiSharp.Core.Mcp;
using PiSharp.Core.Models;
using PiSharp.Core.Session;
using PiSharp.Tests.Agent;
using Xunit;

namespace PiSharp.Tests.Factory;

/// <summary>digital-scribe, 2026-10-26: "also the re-reading of the role is needed": a seat reads its role texts again while
/// it runs, at most every two minutes, and keeps the ones it has when the KB does not answer.</summary>
public sealed class SeatStandingTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task ReadsAgainOnlyAfterTheIntervalAndSaysChangedOnlyWhenTheTextChanged()
    {
        var clock = new Clock();
        var text = "v1";
        var reads = 0;
        var standing = new SeatStanding((_, _) => { reads++; return Task.FromResult<string?>(text); }, TimeSpan.FromMinutes(2), clock);

        Assert.True(await standing.ReadAsync(default));
        Assert.Equal("v1", standing.Current);

        clock.Now += TimeSpan.FromSeconds(119);
        text = "v2";
        Assert.False(await standing.RefreshAsync(default));
        Assert.Equal(1, reads);

        clock.Now += TimeSpan.FromSeconds(1);
        Assert.True(await standing.RefreshAsync(default));
        Assert.Equal("v2", standing.Current);

        clock.Now += TimeSpan.FromMinutes(2);
        Assert.False(await standing.RefreshAsync(default));
        Assert.Equal(3, reads);
    }

    [Fact]
    public async Task NoAnswerKeepsTheTextAndAsksForItWholeOnceTheSeatHasIt()
    {
        var clock = new Clock();
        var answers = new Queue<string?>(["v1", null]);
        var asked = new List<bool>();
        var standing = new SeatStanding((whole, _) => { asked.Add(whole); return Task.FromResult(answers.Dequeue()); }, TimeSpan.FromMinutes(2), clock);

        Assert.True(await standing.ReadAsync(default));
        clock.Now += TimeSpan.FromMinutes(3);
        Assert.False(await standing.RefreshAsync(default));
        Assert.Equal("v1", standing.Current);
        Assert.Equal([false, true], asked);

        // Not asked again on the next turn: the failed read counts for the interval.
        Assert.False(await standing.RefreshAsync(default));
        Assert.Equal(2, asked.Count);
    }

    [Fact]
    public async Task AThrowingReadIsNoAnswer()
    {
        var clock = new Clock();
        var calls = 0;
        var standing = new SeatStanding((_, _) => ++calls == 1 ? Task.FromResult<string?>("v1") : throw new HttpRequestException("gone"),
            TimeSpan.FromMinutes(2), clock);
        Assert.True(await standing.ReadAsync(default));
        clock.Now += TimeSpan.FromMinutes(2);
        Assert.False(await standing.RefreshAsync(default));
        Assert.Equal("v1", standing.Current);
    }

    [Fact]
    public async Task BetweenTurnsTheTextsAreReadBeforeAFreshContextCopiesThePrompt()
    {
        var context = new AgentContext { SystemPrompt = "old role" };
        var state = new CliState
        {
            Cwd = "/tmp",
            Model = Fakes.Model,
            Thinking = ThinkingLevel.Off,
            Models = [Fakes.Model],
            Skills = [],
            Templates = [],
            Mcp = new McpManager(),
            Agent = new AgentSession(SessionManager.InMemory("/tmp"), context),
            Context = context,
            Settings = new Settings(),
            Stream = new ScriptedStream().Then(Fakes.Text("ok")).Stream,
            RefreshStanding = _ => { context.SystemPrompt = "new role"; return Task.CompletedTask; },
        };
        string? seenByInner = null;
        // The factory's fresh context for a new epic copies the prompt of the context the turn ran on.
        var prepare = CliHost.RefreshStandingBetweenTurns(turn =>
        {
            seenByInner = turn.Context.SystemPrompt;
            return new ValueTask<TurnUpdate?>(new TurnUpdate(Context: new AgentContext { SystemPrompt = "old role", Tools = turn.Context.Tools }));
        }, state, default);

        var update = await prepare(new TurnContext(Fakes.Text("done"), [], context, []));

        Assert.Equal("new role", seenByInner);
        Assert.Equal("new role", update!.Context!.SystemPrompt);
    }

    [Fact]
    public void ALocalRunHasNoReadAgain()
    {
        var context = new AgentContext();
        var state = new CliState
        {
            Cwd = "/tmp",
            Model = Fakes.Model,
            Thinking = ThinkingLevel.Off,
            Models = [Fakes.Model],
            Skills = [],
            Templates = [],
            Mcp = new McpManager(),
            Agent = new AgentSession(SessionManager.InMemory("/tmp"), context),
            Context = context,
            Settings = new Settings(),
            Stream = new ScriptedStream().Then(Fakes.Text("ok")).Stream,
        };
        Func<TurnContext, ValueTask<TurnUpdate?>> inner = _ => new ValueTask<TurnUpdate?>((TurnUpdate?)null);
        Assert.Same(inner, CliHost.RefreshStandingBetweenTurns(inner, state, default));
    }
}
