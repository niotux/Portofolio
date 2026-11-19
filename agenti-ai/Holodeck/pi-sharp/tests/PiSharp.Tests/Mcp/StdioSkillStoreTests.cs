using System.Text.Json.Nodes;
using PiSharp.Core.Mcp;
using PiSharp.Tests.Tools;

namespace PiSharp.Tests.Mcp;

public class StdioSkillStoreTests
{
    [Fact]
    public async Task SpawnsSkillStoreProcessOverStdio()
    {
        var dll = Path.Combine(AppContext.BaseDirectory, "PiSharp.SkillStore.dll");
        Assert.True(File.Exists(dll), $"SkillStore dll not copied to test output: {dll}");

        using var tmp = new TempWorkspace();
        McpClientTests.SeedStore(tmp);

        var config = new McpServerConfig
        {
            Alias = "store",
            Kind = McpTransportKind.Stdio,
            Command = "dotnet",
            Args = [dll, tmp.Path, "store"],
        };

        await using var client = await McpClient.ConnectAsync(config);
        var tools = await client.ListToolsAsync();
        Assert.Contains(tools, t => t.Name == "search_skills");

        var search = await client.CallToolAsync("search_skills", new JsonObject { ["query"] = "demo" });
        Assert.Contains("demo", Assert.IsType<McpTextPart>(search.Content[0]).Text);
    }
}
