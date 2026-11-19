using System.Text.Json.Nodes;
using PiSharp.Core.Agent;
using PiSharp.Core.Mcp;
using PiSharp.Core.Messages;
using PiSharp.Core.Models;
using PiSharp.SkillStore;
using PiSharp.Tests.Agent;
using PiSharp.Tests.Tools;
using PiSharp.Tools;

namespace PiSharp.Tests.Mcp;

public sealed class Phase6SkillRetrievalTests
{
    [Fact]
    public async Task SmallContextModelSearchesThenReadsRemoteSkill()
    {
        using var tmp = new TempWorkspace();
        McpClientTests.SeedStore(tmp);
        var dll = Path.Combine(AppContext.BaseDirectory, "PiSharp.SkillStore.dll");
        var config = new McpConfig([
            new McpServerConfig
            {
                Alias = "store",
                Kind = McpTransportKind.Stdio,
                Command = "dotnet",
                Args = [dll, tmp.Path, "store"],
            },
        ]);

        await using var manager = await McpManager.ConnectAllAsync(config);
        var read = new ReadTool(tmp.Path) { SkillUriResolver = manager };
        var tools = new List<IAgentTool> { read };
        tools.AddRange(manager.Tools());
        var model = new ModelConfig
        {
            Id = "small-model",
            BaseUrl = "http://localhost:1234/v1",
            ContextWindow = 8192,
            MaxOutputTokens = 2048,
            MaxTools = 2,
        };
        var stream = new ScriptedStream()
            .Then(Fakes.ToolCalls(Fakes.Call(
                "search-1",
                "store__search_skills",
                JsonNode.Parse("{\"query\":\"demo\"}")!.AsObject())))
            .Then(Fakes.ToolCalls(Fakes.Call(
                "read-1",
                "read",
                JsonNode.Parse("{\"path\":\"skill://store/demo/SKILL.md\"}")!.AsObject())))
            .Then(Fakes.Text("The demo skill says to do the demo thing."));
        var context = new AgentContext
        {
            SystemPrompt = "Search for a skill, read it, then answer.",
            Tools = tools,
        };

        await Fakes.Collect(AgentLoop.Run(
            [new UserMessage("Find and read the demo skill.")],
            context,
            new AgentLoopConfig { Model = model },
            stream.Stream));

        Assert.Equal(3, stream.Calls);
        Assert.Contains(stream.Contexts[0].Tools, tool => tool.Name == "store__search_skills");
        Assert.Contains(stream.Contexts[1].Tools, tool => tool.Name == "read");
        var results = context.Messages.OfType<ToolResultMessage>().ToList();
        Assert.Equal(["store__search_skills", "read"], results.Select(result => result.ToolName));
        Assert.Contains("demo", Fakes.ResultText(results[0]), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Do the demo thing", Fakes.ResultText(results[1]));
    }
}
