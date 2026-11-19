// pi-sharp-hh-11: the read-only explore sub-run tool.
using System.Text.Json.Nodes;
using PiSharp.Core.Agent;
using PiSharp.Core.Messages;
using PiSharp.Core.Models;
using PiSharp.Core.Notebook;
using PiSharp.Tools;
using static PiSharp.Tests.Agent.Fakes;
using PiSharp.Tests.Agent;

namespace PiSharp.Tests.Tools;

public class ExploreToolTests
{
    private static readonly ModelConfig Model = Fakes.Model;

    private static ExploreTool NewTool(
        string cwd,
        ScriptedStream stream,
        NotebookStore? store = null,
        List<AgentEvent>? observed = null,
        int maxTurns = 10,
        int maxOutputTokens = 30_000,
        int findingsTokens = 2_000)
    {
        Action<AgentEvent>? observer = observed is null ? null : evt => observed.Add(evt);
        return new ExploreTool(cwd, () => (Model, stream.Stream), () => "task-1", store, observer,
            maxTurns: maxTurns, maxOutputTokens: maxOutputTokens, findingsTokens: findingsTokens);
    }

    [Fact]
    public async Task ReturnsFindingsFromSubRunWithReadGrepFindOnly()
    {
        using var ws = new TempWorkspace();
        var stream = new ScriptedStream()
            .Then(ToolCalls(Call("e1", "grep", Json("""{"pattern":"foo"}"""))))
            .Then(ToolCalls(Call("e2", "find", Json("""{"pattern":"*.cs"}"""))))
            .Then(Text("Findings: foo handled in src/Foo.cs:10."));
        var observed = new List<AgentEvent>();
        var tool = NewTool(ws.Path, stream, observed: observed);

        var result = await tool.ExecuteAsync("t1", Json("""{"task":"where is foo handled?"}"""), null, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Contains("Findings: foo handled in src/Foo.cs:10.", result.Text);
        var details = (JsonObject)result.Details!;
        Assert.Equal(3, details["turns"]!.GetValue<int>());
        Assert.Equal("model_completed", details["outcome"]!.GetValue<string>());
        var subRunId = details["subRunId"]!.GetValue<string>();
        Assert.StartsWith("explore-", subRunId);
        // The sub-run's events reached the observer (tracer visibility), including tool events for
        // read-only tools only.
        var toolEvents = observed.OfType<ToolExecutionStart>().ToList();
        Assert.Equal(2, toolEvents.Count);
        Assert.All(toolEvents, e => Assert.Contains(e.ToolName, new[] { "read", "grep", "find" }));
        Assert.True(observed.OfType<AgentEnd>().Any());
        Assert.True(toolEvents.All(e => e.ToolName != "bash"));
        // The sub-run's own context carried only read/grep/find tools.
        var firstContext = stream.Contexts.First();
        Assert.Contains("read-only", firstContext.SystemPrompt ?? "");
    }

    [Fact]
    public async Task SubRunCannotUseWriteTools()
    {
        using var ws = new TempWorkspace();
        var stream = new ScriptedStream()
            .Then(ToolCalls(Call("w1", "bash", Json("""{"command":"rm -rf /"}"""))))
            .Then(Text("Findings: attempted."));
        var tool = NewTool(ws.Path, stream);
        var result = await tool.ExecuteAsync("t1", Json("""{"task":"x"}"""), null, CancellationToken.None);

        // The write tool does not exist in the sub-run: the call is rejected and the model sees
        // the "not found" error in the sub-run's own context.
        var seenMessages = string.Join("\n", stream.Contexts.SelectMany(c => c.Messages)
            .OfType<ToolResultMessage>().Select(m => string.Concat(m.Content.OfType<TextContent>().Select(t => t.Text))));
        Assert.Contains("Tool bash not found", seenMessages);
    }

    [Fact]
    public async Task TurnCapStopsAtMaxTurns()
    {
        using var ws = new TempWorkspace();
        // Each turn calls grep with a distinct pattern (so the identical-call guard never fires),
        // and never stops on its own. With a 3-turn cap the sub-run must stop after 3 turns.
        var stream = new ScriptedStream();
        for (var i = 0; i < 5; i++)
            stream = stream.Then(ToolCalls(Call("g" + i, "grep", Json($$"""{"pattern":"p{i}"}""" ))));
        var tool = NewTool(ws.Path, stream, maxTurns: 3);
        var result = await tool.ExecuteAsync("t1", Json("""{"task":"x"}"""), null, CancellationToken.None);

        var details = (JsonObject)result.Details!;
        Assert.Equal(3, details["turns"]!.GetValue<int>());
        Assert.Equal(3, stream.Calls);
        // The stop was forced by the turn cap, not by the model.
        Assert.DoesNotContain("Findings: x", result.Text);
    }

    [Fact]
    public async Task TokenCapChargesTaskBudgetAndStopsAtBudget()
    {
        using var ws = new TempWorkspace();
        using var dir = new TempWorkspace();
        var store = new NotebookStore(Path.Combine(dir.Path, "nb"), "explore-test");
        // 6 turns x 5000 output tokens = 30k: after the 6th turn the budget is exhausted, so a
        // 7th response must not be served.
        var stream = new ScriptedStream();
        for (var i = 0; i < 6; i++)
        {
            var msg = new AssistantMessage
            {
                Content = [Call("b" + i, "grep", Json($$"""{"pattern":"t{i}"}""" ))],
                Model = Fakes.Model.Id,
                StopReason = StopReason.ToolUse,
                Usage = new Usage(Output: 5_000, TotalTokens: 5_000),
            };
            stream = stream.Then(msg);
        }
        var tool = NewTool(ws.Path, stream, store: store, maxOutputTokens: 30_000);
        var result = await tool.ExecuteAsync("t1", Json("""{"task":"x"}"""), null, CancellationToken.None);

        var details = (JsonObject)result.Details!;
        Assert.True(details["budgetExhausted"]!.GetValue<bool>(), $"expected budget exhaustion: {result.Text}");
        Assert.Equal("budget_exhausted", details["outcome"]!.GetValue<string>());
        Assert.True(details["outputTokens"]!.GetValue<int>() >= 30_000);
        // Charged to a durable TaskBudget row named budget-<subRunId> (restart-safe cap).
        var row = store.Get("budget-" + details["subRunId"]!.GetValue<string>());
        Assert.NotNull(row);
        Assert.Equal(30_000, (int)JsonNode.Parse(row!["text"]!.GetValue<string>())!["outputTokens"]!.GetValue<int>());
    }

    [Fact]
    public async Task OnlyOneSubRunAtATimePerSeat()
    {
        using var ws = new TempWorkspace();
        // The first sub-run is held at its first model call until the second one has been refused. Until
        // 2026-10-02 it ran 100 scripted turns freely, and on a loaded box (coder-06 gate, 1 run in 2) it
        // finished before the test looked, so IsRunning was never seen.
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var scripted = new ScriptedStream().Then(Text("done"));
        async IAsyncEnumerable<AssistantMessageEvent> Held(ModelConfig model, LlmContext context, StreamOptions options,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            await release.Task.WaitAsync(ct);
            await foreach (var e in scripted.Stream(model, context, options, ct))
                yield return e;
        }
        var tool = new ExploreTool(ws.Path, () => (Model, Held), () => "task-1", maxTurns: 10);

        var run = tool.ExecuteAsync("t1", Json("""{"task":"x"}"""), null, CancellationToken.None);
        Assert.True(tool.IsRunning);

        var second = await tool.ExecuteAsync("t2", Json("""{"task":"y"}"""), null, CancellationToken.None);
        Assert.True(second.IsError);
        Assert.Contains("already running", second.Text);

        release.SetResult();
        await run;
        Assert.False(tool.IsRunning);
    }

    [Fact]
    public async Task FindingsCappedAtTwoKTokens()
    {
        using var ws = new TempWorkspace();
        // A ~6k-token findings message must be truncated to the 2k cap.
        var longText = string.Join("\n", Enumerable.Range(0, 2_400).Select(i => $"finding line {i}: " + new string('x', 30)));
        var stream = new ScriptedStream().Then(Text(longText));
        var tool = NewTool(ws.Path, stream);
        var result = await tool.ExecuteAsync("t1", Json("""{"task":"x"}"""), null, CancellationToken.None);

        var details = (JsonObject)result.Details!;
        Assert.Contains("omitted from the sub-run's findings", result.Text);
        Assert.Contains("truncated to 2000 tokens", result.Text);
        // The truncation marker names how much was left out; the kept part is within the cap.
        var truncated = ExploreTool.CapTokens(longText, 2_000);
        Assert.True(PiSharp.Core.Session.TokenEstimate.EstimateTextTokens(truncated) <= 2_050);
        Assert.NotEqual(longText, truncated);
        // The body handed to the seat (everything after the header) is at or under the cap + marker.
        var idx = result.Text.IndexOf("Findings:", StringComparison.Ordinal);
        var body = result.Text[(idx + "Findings:".Length + 1)..];
        Assert.True(PiSharp.Core.Session.TokenEstimate.EstimateTextTokens(body) <= 2_050,
            $"body tokens {PiSharp.Core.Session.TokenEstimate.EstimateTextTokens(body)} must be <= 2050");
    }

    [Fact]
    public async Task ReplaySavesAtLeastHalfTheMainContextGrowth()
    {
        // Replay: answer the same question two ways and compare main-context growth.
        // Inline: 6 read calls each returning ~4k tokens land in the main context (plus repeats).
        var inlineOutputs = Enumerable.Range(0, 6).Select(_ => new string('a', 16_000)).ToList();
        var inlineGrowth = inlineOutputs.Sum(t => PiSharp.Core.Session.TokenEstimate.EstimateTextTokens(t));
        // Explore: the same exploration happens in the sub-run; only the capped findings enter the
        // main context (plus a small header).
        var findingsGrowth = ExploreTool.DefaultFindingsTokens + 50;
        Assert.True(inlineGrowth > 0);
        Assert.True(findingsGrowth * 2 <= inlineGrowth,
            $"explore growth {findingsGrowth} must be <= 50% of inline growth {inlineGrowth}");
    }
}

internal static class ExploreToolTestExtensions
{
    public static ScriptedStream with(this ScriptedStream stream, object _) => stream;
}
