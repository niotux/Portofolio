using System.Text.Json.Nodes;
using PiSharp.Core.Agent;
using PiSharp.Core.Messages;
using PiSharp.Tests.Tools;
using PiSharp.Tools;
using static PiSharp.Tests.Agent.Fakes;

namespace PiSharp.Tests.Agent;

public class ToolEfficiencyTests
{
    [Fact]
    public async Task ResourceMetadataPermitsIndependentWritesButOrdersAConflictingRead()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var aDone = false;
        var first = new ResourceTool("write-a", new(false, ["/workspace/a"]), async () =>
            { await gate.Task.WaitAsync(TimeSpan.FromSeconds(5)); aDone = true; });
        var second = new ResourceTool("write-b", new(false, ["/workspace/b"]), () => { gate.SetResult(); return Task.CompletedTask; });
        var read = new ResourceTool("read-a", new(true, ["/workspace/a"]), () => { Assert.True(aDone); return Task.CompletedTask; });
        var context = new AgentContext { Tools = [first, second, read] };
        var stream = new ScriptedStream().Then(ToolCalls(Call("a", first.Name), Call("b", second.Name), Call("c", read.Name))).Then(Text("done"));
        await Collect(AgentLoop.Run([new UserMessage("work")], context, Config(), stream.Stream, default));
        Assert.All(context.Messages.OfType<ToolResultMessage>(), r => Assert.False(r.IsError, ResultText(r)));
        Assert.Equal(["a", "b", "c"], context.Messages.OfType<ToolResultMessage>().Select(r => r.ToolCallId));
    }
    [Fact]
    public async Task MixedBatchRunsReadsInParallelWithoutCrossingMutationBarrier()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var starts = new List<string>(); var lockObject = new object();
        var mutationFinished = false;
        DelegateTool Read(string name, bool first) => new()
        {
            Name = name, ExecutionMode = ToolExecutionMode.Parallel,
            Execute = async (_, _, _, ct) =>
            {
                lock (lockObject) { Assert.False(mutationFinished); starts.Add(name); }
                if (first) await gate.Task.WaitAsync(TimeSpan.FromSeconds(5), ct); else gate.SetResult();
                return AgentToolResult.FromText(name);
            }
        };
        var mutation = new DelegateTool
        {
            Name = "mutate", ExecutionMode = ToolExecutionMode.Sequential,
            Execute = (_, _, _, _) => { Assert.Equal(2, starts.Count); mutationFinished = true; return Task.FromResult(AgentToolResult.FromText("mutated")); }
        };
        var after = new DelegateTool { Name = "after", ExecutionMode = ToolExecutionMode.Parallel,
            Execute = (_, _, _, _) => { Assert.True(mutationFinished); return Task.FromResult(AgentToolResult.FromText("after")); } };
        var context = new AgentContext { Tools = [Read("first", true), Read("second", false), mutation, after] };
        var stream = new ScriptedStream().Then(ToolCalls(Call("a", "first"), Call("b", "second"), Call("c", "mutate"), Call("d", "after"))).Then(Text("done"));
        await Collect(AgentLoop.Run([new UserMessage("work")], context, Config(), stream.Stream, default));
        Assert.Equal(["a", "b", "c", "d"], context.Messages.OfType<ToolResultMessage>().Select(r => r.ToolCallId));
        Assert.All(context.Messages.OfType<ToolResultMessage>(), r => Assert.False(r.IsError, r.Content.ToString()));
    }

    [Fact]
    public async Task ReadReferencesPriorOutputButRefreshAndFileChangesReturnFullContents()
    {
        using var tmp = new TempWorkspace(); tmp.File("file.txt", new string('x', 1000));
        var context = new AgentContext { Tools = [new ReadTool(tmp.Path)] };
        JsonObject Args(bool refresh = false) => new() { ["path"] = "file.txt", ["refresh"] = refresh };
        var stream = new ScriptedStream().Then(ToolCalls(Call("r1", "read", Args())))
            .Then(ToolCalls(Call("r2", "read", Args())))
            .Then(ToolCalls(Call("r3", "read", Args(true))))
            .Then(() => { tmp.File("file.txt", "new content"); return ToolCalls(Call("r4", "read", Args())); }).Then(Text("done"));
        await Collect(AgentLoop.Run([new UserMessage("work")], context, Config(), stream.Stream, default));
        var results = context.Messages.OfType<ToolResultMessage>().ToArray();
        Assert.Contains("r1", ResultText(results[1]));
        Assert.True(ResultText(results[1]).Length < ResultText(results[0]).Length);
        Assert.Equal(ResultText(results[0]), ResultText(results[2]));
        Assert.Contains("new content", ResultText(results[3]));
        Assert.Equal(1, context.ReadCache.Hits);
    }

    [Fact]
    public async Task CompactionDoesNotReferenceAnEvictedResult()
    {
        using var tmp = new TempWorkspace(); tmp.File("file.txt", "original content");
        var context = new AgentContext { Tools = [new ReadTool(tmp.Path)] };
        var arguments = new JsonObject { ["path"] = "file.txt" };
        var first = new ScriptedStream().Then(ToolCalls(Call("before", "read", arguments))).Then(Text("done"));
        await Collect(AgentLoop.Run([new UserMessage("read")], context, Config(), first.Stream, default));
        context.Messages = [new CompactionSummaryMessage("read a file earlier", 20)];
        var second = new ScriptedStream().Then(ToolCalls(Call("after", "read", arguments))).Then(Text("done"));
        await Collect(AgentLoop.Run([new UserMessage("read")], context, Config(), second.Stream, default));
        Assert.Contains("original content", ResultText(context.Messages.OfType<ToolResultMessage>().Single()));
        Assert.Equal(0, context.ReadCache.Hits);
    }

    private sealed class ResourceTool(string name, ToolAccess access, Func<Task> execute) : IAgentTool
    {
        public string Name => name;
        public string Label => name;
        public string Description => name;
        public JsonObject Parameters => new() { ["type"] = "object" };
        public ToolExecutionMode? ExecutionMode => ToolExecutionMode.Parallel;
        public ToolAccess Access(JsonObject arguments) => access;
        public async Task<AgentToolResult> ExecuteAsync(string id, JsonObject arguments, Action<AgentToolResult>? onUpdate, CancellationToken ct)
        { await execute(); return AgentToolResult.FromText("ok"); }
    }
}
