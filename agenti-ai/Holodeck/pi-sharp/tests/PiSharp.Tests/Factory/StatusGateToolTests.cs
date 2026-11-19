using System.Text.Json.Nodes;
using PiSharp.Core.Agent;
using PiSharp.Core.Factory;

namespace PiSharp.Tests.Factory;

/// <summary>digital-scribe, 2026-10-20: "status updates only on a real state change".</summary>
public class StatusGateToolTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 20, 14, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class ChatManage : IAgentTool
    {
        public List<JsonObject> Sent { get; } = [];
        public string Name => "chat__chat_manage";
        public string Label => Name;
        public string Description => "chat_manage";
        public JsonObject Parameters => new() { ["type"] = "object" };
        public Task<AgentToolResult> ExecuteAsync(string toolCallId, JsonObject arguments, Action<AgentToolResult>? onUpdate, CancellationToken cancellationToken)
        {
            Sent.Add(arguments);
            return Task.FromResult(AgentToolResult.FromText("{\"ok\":true}"));
        }
    }

    private static JsonObject Status(string text) => new() { ["op"] = "status", ["status"] = text };

    [Fact]
    public async Task OnlyAChangedStateOrItemIsSent()
    {
        var clock = new Clock();
        var inner = new ChatManage();
        var gate = new StatusGateTool(inner, clock);
        await gate.ExecuteAsync("1", Status("working — hc2000-disk-library-scan-homes: running the App suite"), null, default);
        var same = await gate.ExecuteAsync("2", Status("working — hc2000-disk-library-scan-homes: suite 3/4 done, reading failures"), null, default);
        Assert.Contains("Status not sent", same.Text);
        Assert.False(same.IsError);
        await gate.ExecuteAsync("3", Status("blocked — hc2000-disk-library-scan-homes waits on the product owner"), null, default);
        await gate.ExecuteAsync("4", Status("working — hc2000-hdd-context-mount-menu"), null, default);
        await gate.ExecuteAsync("5", Status(""), null, default); // clearing always goes
        await gate.ExecuteAsync("6", new JsonObject { ["op"] = "who" }, null, default); // other ops go
        Assert.Equal(5, inner.Sent.Count);

        // The same state again after an hour is sent, so a long task's line stays fresh.
        await gate.ExecuteAsync("7", Status("idle; last finished hc2000-hdd-context-mount-menu"), null, default);
        await gate.ExecuteAsync("8", Status("idle; last finished hc2000-hdd-context-mount-menu at 14:10"), null, default);
        clock.Now += StatusGateTool.Refresh;
        await gate.ExecuteAsync("9", Status("idle; last finished hc2000-hdd-context-mount-menu"), null, default);
        Assert.Equal(7, inner.Sent.Count);
    }

    [Theory]
    [InlineData("Working on hc2000-a-b-c, step 2", "working|hc2000-a-b-c")]
    [InlineData("idle", "idle|")]
    [InlineData("waiting until 2026-10-20T15:00Z for wi-118 and hc2000-x-y-z", "waiting|hc2000-x-y-z,wi-118")]
    public void TheStateIsTheFirstWordAndTheItems(string status, string key) => Assert.Equal(key, StatusGateTool.StateKey(status));

    [Fact]
    public void TheToolsEverySeatUsesAreCoreAndChatManageIsNot()
    {
        foreach (var name in new[] { "kb__kb_get", "kb__kb_list", "kb__kb_write", "kb__kb_transition", "kb__kb_suite", "chat__chat_read", "chat__chat_post", "desktop__act" })
            Assert.True(ToolDiscovery.IsCore(name), name);
        Assert.False(ToolDiscovery.IsCore("chat__chat_manage"));
        Assert.False(ToolDiscovery.IsCore("kb__kb_ops"));
    }
}
