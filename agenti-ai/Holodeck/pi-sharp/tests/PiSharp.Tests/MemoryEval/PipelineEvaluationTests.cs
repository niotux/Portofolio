using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using PiSharp.Core.Agent;
using PiSharp.Core.Messages;
using PiSharp.Core.Models;
using PiSharp.MemoryEval;
using PiSharp.Tests.Tools;

namespace PiSharp.Tests.MemoryEval;

public class PipelineEvaluationTests
{
    [Fact]
    public async Task RestartedNotebookChangesActualCodingOutcomeAndExcludesStaleAndForeignPolicies()
    {
        using var tmp = new TempWorkspace();
        var report = await PipelineEvaluation.RunAsync(tmp.Path, new ModelConfig { Id = "offline-policy-reader", BaseUrl = "http://unused", ContextWindow = 32768, MaxOutputTokens = 8192 }, PolicyReader);
        var cases = report["cases"]!.AsArray();
        var memory = cases.Where(c => c!["memoryEnabled"]!.GetValue<bool>()).ToArray();
        var baseline = cases.Where(c => !c!["memoryEnabled"]!.GetValue<bool>()).ToArray();
        Assert.Equal(3, memory.Length); Assert.Equal(3, baseline.Length);
        Assert.All(memory, c =>
        {
            Assert.True(c!["success"]!.GetValue<bool>(), c.ToJsonString());
            Assert.True(c["retrievedExpectedMemory"]!.GetValue<bool>());
            Assert.False(c["forbiddenMemoryRetrieved"]!.GetValue<bool>());
            Assert.Equal(1, c["toolCalls"]!.GetValue<int>());
        });
        Assert.All(baseline, c => Assert.False(c!["success"]!.GetValue<bool>()));
        Assert.Null(report["summary"]!["memory"]!["reportedTokensPerSuccess"]); // offline usage is unreported, not zero cost
    }

    // Deterministic policy interpreter for CI, not a claim about model quality. It consumes the actual retrieved block.
    private static async IAsyncEnumerable<AssistantMessageEvent> PolicyReader(ModelConfig model, LlmContext context, StreamOptions options,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.Yield(); ct.ThrowIfCancellationRequested();
        var empty = new AssistantMessage { Model = model.Id, Content = [] };
        yield return new AssistantStart(empty);
        var recalled = string.Join("\n", context.Messages.OfType<UserMessage>().Where(m => m.Text.StartsWith("<recalled-notes>")).Select(m => m.Text));
        var formula = recalled.Contains("min(7, max(0, attempt) * 3)") ? "min(7, max(0, attempt) * 3)"
            : recalled.Contains("min(20, 2 ** max(0, attempt))") ? "min(20, 2 ** max(0, attempt))"
            : recalled.Contains("max(0, attempt) * 4") ? "max(0, attempt) * 4" : null;
        if (formula is null || context.Messages.OfType<ToolResultMessage>().Any())
        {
            yield return new AssistantDone(StopReason.Stop, empty with { StopReason = StopReason.Stop, Content = [new TextContent(formula is null ? "Historical policy is missing." : "Implemented.")] });
            yield break;
        }
        yield return new AssistantDone(StopReason.ToolUse, empty with
        {
            StopReason = StopReason.ToolUse,
            Content = [new ToolCallContent("write-policy", "write", new JsonObject { ["path"] = "solution.py", ["content"] = "def wait_seconds(attempt):\n    return " + formula + "\n" })],
        });
    }
}
