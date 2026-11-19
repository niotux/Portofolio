using System.Text.Json.Nodes;
using PiSharp.Core.Agent;
using PiSharp.Core.Guard;
using PiSharp.Core.Messages;
using PiSharp.Tests.Agent;

namespace PiSharp.Tests.Guard;

// plan-pi-sharp-codex item 2 (Codex auto-review): the seat's own model decides an Ask from a compact view of the work.
public sealed class ActionReviewerTests
{
    private static readonly GuardVerdict Verdict = new(GuardAction.Ask,
        new GuardRule("force-push", "force", "A force push rewrites a branch others may have.", action: GuardAction.Ask), "ask");

    private static readonly ToolCallContent Call = new("c1", "bash", new JsonObject { ["command"] = "git push --force origin x" });

    private static AgentContext Work() => new()
    {
        Messages =
        [
            new UserMessage("[inbox from @team-lead-2] rebase your branch on main and push it"),
            Fakes.Text("Rebased; pushing my branch."),
        ],
    };

    [Fact]
    public async Task ItAllowsOrDeniesFromTheModelsJson_AndAnUnreadableAnswerIsADenial()
    {
        var stream = new ScriptedStream()
            .Then(Fakes.Text("""{"decision":"allow","rationale":"Its own branch, rebased as the team lead asked."}"""))
            .Then(Fakes.Text("Sure, go ahead!"));
        var reviewer = new ModelActionReviewer(stream.Stream, () => Fakes.Model);

        var allowed = await reviewer.ReviewAsync(Call, Verdict, Work(), default);
        Assert.True(allowed.Allowed);
        Assert.Equal("Its own branch, rebased as the team lead asked.", allowed.Rationale);

        var unreadable = await reviewer.ReviewAsync(Call, Verdict, Work(), default);
        Assert.False(unreadable.Allowed);
        Assert.Contains("could not be read", unreadable.Rationale);
    }

    [Fact]
    public async Task ThreeDenialsInARowStopTheTurn_AnAllowResetsTheCount()
    {
        var deny = Fakes.Text("""{"decision":"deny","rationale":"Another seat's branch."}""");
        var allow = Fakes.Text("""{"decision":"allow","rationale":"Fine."}""");
        var stream = new ScriptedStream().Then(deny).Then(deny).Then(allow).Then(deny).Then(deny).Then(deny);
        var reviewer = new ModelActionReviewer(stream.Stream, () => Fakes.Model);
        var stops = new List<bool>();
        for (var i = 0; i < 6; i++) stops.Add((await reviewer.ReviewAsync(Call, Verdict, Work(), default)).StopTurn);
        Assert.Equal([false, false, false, false, false, true], stops);
    }

    [Fact]
    public async Task TenDenialsInTheLastFiftyStopTheTurn()
    {
        var deny = Fakes.Text("""{"decision":"deny","rationale":"No."}""");
        var allow = Fakes.Text("""{"decision":"allow","rationale":"Fine."}""");
        var stream = new ScriptedStream();
        for (var i = 0; i < 10; i++) stream.Then(deny).Then(allow); // never two in a row
        var reviewer = new ModelActionReviewer(stream.Stream, () => Fakes.Model);
        var stops = new List<bool>();
        for (var i = 0; i < 20; i++) stops.Add((await reviewer.ReviewAsync(Call, Verdict, Work(), default)).StopTurn);
        Assert.Equal(18, stops.IndexOf(true)); // the tenth denial
    }

    [Fact]
    public void ThePromptCarriesTheWorkTheCallAndTheRule()
    {
        var prompt = new ModelActionReviewer((_, _, _, _) => throw new InvalidOperationException(), () => Fakes.Model).Prompt(Call, Verdict, Work());
        Assert.Contains("[user/inbox] [inbox from @team-lead-2] rebase your branch on main and push it", prompt);
        Assert.Contains("[seat] Rebased; pushing my branch.", prompt);
        Assert.Contains("git push --force origin x", prompt);
        Assert.Contains("[force-push] A force push rewrites", prompt);
    }
}
