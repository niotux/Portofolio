using PiSharp.Core.Agent;
using PiSharp.Core.Messages;
using PiSharp.Core.Notebook;
using PiSharp.Tests.Tools;
using static PiSharp.Tests.Agent.Fakes;

namespace PiSharp.Tests.Agent;

public class ParallelSafeToolsTests
{
    [Theory]
    [InlineData("read", true)]
    [InlineData("grep", true)]
    [InlineData("kb__kb_get", true)]
    [InlineData("kb__kb_search", true)]
    [InlineData("kb__kb_list", true)]
    [InlineData("chat__chat_read", false)] // moves the read cursor
    [InlineData("chat__chat_search", true)]
    [InlineData("chat__chat_post", false)]
    [InlineData("kb__kb_write", false)]
    [InlineData("edit", false)]
    [InlineData("apply_patch", false)]
    [InlineData("bash", false)]
    public void BoundedToolModeFollowsTheReadOnlySet(string name, bool parallel)
    {
        using var tmp = new TempWorkspace();
        var bounded = new BoundedTool(new DelegateTool { Name = name }, new ArtifactStore(tmp.Path));
        Assert.Equal(parallel ? ToolExecutionMode.Parallel : ToolExecutionMode.Sequential, bounded.ExecutionMode);
    }

    [Fact]
    public async Task ReadOnlyMcpCallsRunConcurrentlyWithResultsInCallOrder()
    {
        using var tmp = new TempWorkspace();
        var artifacts = new ArtifactStore(tmp.Path);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // The first call can only finish once the second has started: run in sequence, it would time out
        // into an error result.
        var slow = new BoundedTool(new DelegateTool
        {
            Name = "kb__kb_get",
            Execute = async (_, _, _, _) => { await gate.Task.WaitAsync(TimeSpan.FromSeconds(10)); return AgentToolResult.FromText("entity"); },
        }, artifacts);
        var fast = new BoundedTool(new DelegateTool
        {
            Name = "chat__chat_search",
            Execute = (_, _, _, _) => { gate.TrySetResult(); return Task.FromResult(AgentToolResult.FromText("messages")); },
        }, artifacts);
        var ctx = new AgentContext { SystemPrompt = "system", Tools = [slow, fast] };
        var stream = new ScriptedStream()
            .Then(ToolCalls(Call("c-get", "kb__kb_get"), Call("c-read", "chat__chat_search")))
            .Then(Text("done"));

        var events = await Collect(AgentLoop.Run([new UserMessage("hello")], ctx, Config(), stream.Stream, default));

        Assert.Equal(2, events.OfType<ToolExecutionEnd>().Count());
        var results = ctx.Messages.OfType<ToolResultMessage>().ToList();
        Assert.Equal(["c-get", "c-read"], results.Select(m => m.ToolCallId));
        Assert.All(results, r => Assert.False(r.IsError));
    }
}
