using System.Text.Json.Nodes;
using PiSharp.Core.Agent;
using PiSharp.Core.Factory;
using PiSharp.Core.Messages;
using PiSharp.Tests.Agent;
using static PiSharp.Tests.Agent.Fakes;

namespace PiSharp.Tests.Factory;

// plan-pi-sharp-codex item 15: an epic is handed in only with each acceptance criterion and its evidence (the completion
// audit, enforced by the harness).
public sealed class HandInGateTests
{
    private const string Objective = "Sound on every machine\nAcceptance: the suite passes; a seat plays the boot chime.";

    private static DelegateTool Transition(bool takesCriteria, List<JsonObject> calls) => new()
    {
        Name = "kb_transition",
        Parameters = Json(takesCriteria
            ? """{"type":"object","properties":{"work_item":{"type":"string"},"to_status":{"type":"string"},"note":{"type":"string"},"criteria":{"type":"array","items":{"type":"object"}}},"required":["work_item","to_status"]}"""
            : """{"type":"object","properties":{"work_item":{"type":"string"},"to_status":{"type":"string"},"note":{"type":"string"}},"required":["work_item","to_status"]}"""),
        Execute = (_, args, _, _) => { calls.Add(args); return Task.FromResult(AgentToolResult.FromText("moved")); },
    };

    private static HandInGate Gate(IAgentTool inner, bool epic = true) =>
        new(inner, (slug, _) => Task.FromResult<FactoryHandIn?>(new FactoryHandIn(epic && slug == "sound-epic", Objective)));

    [Fact]
    public async Task AHandInWithoutEachCriterionsEvidenceIsRefused_EvenWithTheNamesAModelUses()
    {
        var calls = new List<JsonObject>();
        var gate = Gate(Transition(true, calls));
        var ctx = new AgentContext { SystemPrompt = "sys", Tools = [gate] };
        var stream = new ScriptedStream()
            // "slug" and "status" are renamed to work_item and to_status by the argument check before the gate runs.
            .Then(ToolCalls(Call("c1", "kb_transition", Json("""{"slug":"sound-epic","status":"review","note":"done"}"""))))
            .Then(ToolCalls(Call("c2", "kb_transition", Json("""{"work_item":"sound-epic","to_status":"review","criteria":[{"criterion":"the suite passes","result":"pass","evidence":"strata:1 1541/1541"},{"criterion":"a seat plays the boot chime","result":"fail","evidence":"no audio device"}]}"""))))
            .Then(ToolCalls(Call("c3", "kb_transition", Json("""{"work_item":"sound-epic","to_status":"review","criteria":[{"criterion":"the suite passes","result":"pass","evidence":"strata:1 1541/1541"},{"criterion":"a seat plays the boot chime","result":"pass","evidence":"demo #40, the chime dumped to chime.wav"}]}"""))))
            .Then(Text("handed in"));

        await Collect(AgentLoop.Run([new UserMessage("hand it in")], ctx, Config(), stream.Stream));

        var results = ctx.Messages.OfType<ToolResultMessage>().ToList();
        Assert.Equal(3, results.Count);
        Assert.True(results[0].IsError);
        Assert.Contains("no criteria were given", ResultText(results[0]));
        Assert.Contains("a seat plays the boot chime", ResultText(results[0])); // the epic's own text comes back
        Assert.True(results[1].IsError);
        Assert.Contains("'a seat plays the boot chime' failed", ResultText(results[1]));
        Assert.False(results[2].IsError);
        Assert.Single(calls); // only the full hand-in reached the KB
        Assert.Equal(2, calls[0]["criteria"]!.AsArray().Count);
    }

    [Fact]
    public async Task OtherMovesOtherItemsAndAnOlderHubPassThrough()
    {
        var calls = new List<JsonObject>();
        var gate = Gate(Transition(true, calls));
        Assert.False((await gate.ExecuteAsync("a", Json("""{"work_item":"sound-epic","to_status":"blocked","blocked_by":"wi-7","note":"x"}"""), null, default)).IsError);
        Assert.False((await gate.ExecuteAsync("b", Json("""{"work_item":"wi-9","to_status":"review"}"""), null, default)).IsError); // not an epic
        var older = Gate(Transition(false, calls));
        Assert.False(older.KbTakesCriteria);
        Assert.False((await older.ExecuteAsync("c", Json("""{"work_item":"sound-epic","to_status":"review"}"""), null, default)).IsError);
        var unknown = new HandInGate(Transition(true, calls), (_, _) => Task.FromResult<FactoryHandIn?>(null)); // the KB did not answer
        Assert.True((await unknown.ExecuteAsync("d", Json("""{"work_item":"sound-epic","to_status":"review"}"""), null, default)).IsError);
        Assert.Equal(3, calls.Count);
    }

    [Fact]
    public async Task ABlockOnAPersonPassesOnlyOnTheThirdAttempt_ButAWaitOnAnotherItemPassesAtOnce()
    {
        var calls = new List<JsonObject>();
        var clock = new ManualClock(new DateTimeOffset(2026, 10, 26, 9, 0, 0, TimeSpan.Zero));
        var gate = new HandInGate(Transition(true, calls), (_, _) => Task.FromResult<FactoryHandIn?>(new FactoryHandIn(true, Objective)), clock);
        var onPerson = Json("""{"work_item":"sound-epic","to_status":"blocked","note":"waiting for the owner to say which chime"}""");
        var first = await gate.ExecuteAsync("a", onPerson, null, default);
        Assert.True(first.IsError);
        Assert.Contains("Not blocked yet (1 of 3)", ResultTextOf(first));
        Assert.Contains("Can a lane decide it?", ResultTextOf(first));
        Assert.Contains("(2 of 3)", ResultTextOf(await gate.ExecuteAsync("b", onPerson, null, default)));
        Assert.False((await gate.ExecuteAsync("c", onPerson, null, default)).IsError);
        Assert.Single(calls);

        // A wait on another work item is a dependency: recorded at once.
        Assert.False((await gate.ExecuteAsync("d", Json("""{"work_item":"sound-epic","to_status":"blocked","blocked_by":"audio-dump-epic"}"""), null, default)).IsError);
        // Attempts older than the window do not count.
        Assert.True((await gate.ExecuteAsync("e", onPerson, null, default)).IsError);
        clock.Advance(HandInGate.BlockedAuditWindow + TimeSpan.FromMinutes(1));
        Assert.Contains("(1 of 3)", ResultTextOf(await gate.ExecuteAsync("f", onPerson, null, default)));
        Assert.Equal(2, calls.Count);
    }

    private sealed class ManualClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan by) => now += by;
    }

    private static string ResultTextOf(AgentToolResult result) => string.Concat(result.Content.OfType<TextContent>().Select(t => t.Text));

    [Theory]
    [InlineData("""[{"criterion":"a","result":"pass","evidence":"run 1"}]""", null)]
    [InlineData("""[{"criterion":"a","result":"PASS","evidence":"run 1"}]""", null)]
    [InlineData("""[]""", "no criteria were given")]
    [InlineData("""[{"criterion":"a","result":"pass","evidence":" "}]""", "'a' has no evidence")]
    [InlineData("""[{"criterion":"a","evidence":"run 1"}]""", "'a' has no result")]
    [InlineData("""[{"result":"pass","evidence":"run 1"}]""", "criterion 1 has no text")]
    public void EachCriterionNeedsItsTextAResultAndEvidence(string criteria, string? problem)
    {
        var refusal = HandInGate.Refusal("e", "Acceptance: a", JsonNode.Parse(criteria));
        if (problem is null) Assert.Null(refusal);
        else Assert.Contains(problem, refusal);
    }
}
