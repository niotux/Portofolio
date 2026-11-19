using System.Text.Json.Nodes;
using PiSharp.Core.Config;
using PiSharp.Core.Notebook;
using PiSharp.Tests.Tools;

namespace PiSharp.Tests.Notebook;

public class PlanToolTests
{
    private static Task<PiSharp.Core.Agent.AgentToolResult> Call(PlanTool tool, JsonObject args) => tool.ExecuteAsync("call", args, null, default);

    [Fact]
    public async Task SetSupersedesDroppedStepWithoutDeletingAndRepairsStepAliases()
    {
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Path, "seat");
        var tool = new PlanTool(store);
        await Call(tool, new() { ["op"] = "set", ["task"] = "chat-1", ["steps"] = new JsonArray
        {
            new JsonObject { ["step"] = "keep", ["status"] = "in_progress" },
            new JsonObject { ["content"] = "drop", ["status"] = "pending" }
        }});
        var dropped = "plan-chat-1-02";
        await Call(tool, new() { ["op"] = "set", ["task"] = "chat-1", ["steps"] = new JsonArray
        { new JsonObject { ["title"] = "keep", ["status"] = "in_progress" } }});
        Assert.Equal("superseded", store.Get(dropped)!["status"]!.GetValue<string>());
        Assert.Equal("plan-step", store.Get(dropped)!["kind"]!.GetValue<string>());
        Assert.Equal("keep", store.Get("plan-chat-1-01")!["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task DoneWithoutIdCompletesLowestOrderedOpenStepAndStatusAliasesAreAccepted()
    {
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Path, "seat");
        var tool = new PlanTool(store);
        await Call(tool, new() { ["op"] = "set", ["task"] = "chat-2", ["steps"] = new JsonArray
        {
            new JsonObject { ["taskId"] = "lower", ["content"] = "second", ["status"] = "pending" },
            new JsonObject { ["stepId"] = "higher", ["step"] = "first", ["status"] = "in_progress" }
        }});
        await Call(tool, new() { ["op"] = "done", ["task"] = "chat-2" });
        Assert.Equal("completed", store.Get("lower")!["status"]!.GetValue<string>());
        Assert.Equal("open", store.Get("higher")!["status"]!.GetValue<string>());
    }

    [Fact]
    public async Task RefusesMoreThanTwentyAndPlanToolIsAnExplicitSetting()
    {
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Path, "seat");
        var tool = new PlanTool(store);
        var steps = new JsonArray(Enumerable.Range(0, 21).Select(i => (JsonNode?)new JsonObject { ["content"] = $"s{i}" }).ToArray());
        var error = await Assert.ThrowsAsync<ArgumentException>(() => Call(tool, new() { ["op"] = "set", ["task"] = "t", ["steps"] = steps }));
        Assert.Contains("split into subplans", error.Message);
        Assert.Equal(true, SettingsLoader.Parse("{\"planTool\":true}").PlanTool);
        Assert.Null(SettingsLoader.Parse("{}").PlanTool);
    }
}
