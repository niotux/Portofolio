#pragma warning disable xUnit1031
using System.Text.Json.Nodes;
using PiSharp.Core.Agent;
using PiSharp.Core.Mcp;
using PiSharp.Core.Messages;
using PiSharp.SkillStore;
using PiSharp.Tests.Tools;

namespace PiSharp.Tests.Mcp;

public class McpClientTests
{
    [Fact]
    public async Task InitializeAndPingAgainstSkillStore()
    {
        using var tmp = new TempWorkspace();
        SeedStore(tmp);
        await using var pipes = new RpcPipes();
        var host = new SkillStoreHost(tmp.Path, "store");
        await using var server = host.Attach(pipes.ServerIn, pipes.ServerOut);
        await using var client = await McpClient.ConnectAsync(
            "store",
            new PeerTransport(new JsonRpcPeer(pipes.ClientIn, pipes.ClientOut)));

        Assert.Equal("pi-sharp-skill-store", client.ServerInfo.Name);
        Assert.Equal(McpClient.ProtocolVersion, client.NegotiatedVersion);
        await client.PingAsync();
    }

    [Fact]
    public async Task ListsToolsPromptsAndSkillResources()
    {
        using var tmp = new TempWorkspace();
        SeedStore(tmp);
        await using var session = await ConnectStore(tmp.Path);

        var tools = session.Client.ListToolsAsync();
        await tools;
        Assert.Contains(tools.Result, t => t.Name == "search_skills");
        Assert.Contains(tools.Result, t => t.Name == "install_skill");

        var prompts = await session.Client.ListPromptsAsync();
        Assert.Contains(prompts, p => p.Name == "review");

        var resources = await session.Client.ListResourcesAsync();
        Assert.Contains(resources, r => r.Uri == "skill://store/demo/SKILL.md");

        var templates = await session.Client.ListResourceTemplatesAsync();
        Assert.Contains(templates, t => t.UriTemplate.Contains("skill://store"));
    }

    [Fact]
    public async Task CallToolMapsTextAndPrefixedAgentToolName()
    {
        using var tmp = new TempWorkspace();
        SeedStore(tmp);
        await using var session = await ConnectStore(tmp.Path);

        var listed = await session.Client.ListToolsAsync();
        var search = listed.First(t => t.Name == "search_skills");
        var agentTool = new McpAgentTool(session.Client, search);
        Assert.Equal("store__search_skills", agentTool.Name);

        var result = await agentTool.ExecuteAsync(
            "c1",
            new JsonObject { ["query"] = "demo" },
            null,
            CancellationToken.None);

        Assert.Contains("demo", result.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MCP error", result.Text);
    }

    [Fact]
    public async Task CallToolMapsImageContent()
    {
        await using var pipes = new RpcPipes();
        await using var server = new JsonRpcPeer(pipes.ServerIn, pipes.ServerOut, (method, p, _) =>
        {
            JsonNode? r = method switch
            {
                "initialize" => new JsonObject
                {
                    ["protocolVersion"] = McpClient.ProtocolVersion,
                    ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                    ["serverInfo"] = new JsonObject { ["name"] = "img", ["version"] = "1" },
                },
                "tools/call" => new JsonObject
                {
                    ["content"] = new JsonArray(
                        new JsonObject { ["type"] = "text", ["text"] = "pic" },
                        new JsonObject { ["type"] = "image", ["data"] = ImageFixtures.Png().Data, ["mimeType"] = "image/png" }),
                    ["isError"] = false,
                },
                _ => new JsonObject(),
            };
            return Task.FromResult<JsonNode?>(r);
        });
        await using var client = await McpClient.ConnectAsync(
            "img",
            new PeerTransport(new JsonRpcPeer(pipes.ClientIn, pipes.ClientOut)));

        var call = await client.CallToolAsync("x", new JsonObject());
        var parts = McpAgentTool.MapContent(call.Content);
        Assert.Equal("pic", Assert.IsType<TextContent>(parts[0]).Text);
        var image = Assert.IsType<ImageContent>(parts[1]);
        Assert.Equal(ImageFixtures.Png().Data, image.Data);
        Assert.Equal("image/png", image.MimeType);
    }

    [Fact]
    public async Task HungToolCallIsCancelledByConfiguredDeadline()
    {
        await using var pipes = new RpcPipes();
        await using var server = new JsonRpcPeer(pipes.ServerIn, pipes.ServerOut, async (method, _, ct) =>
        {
            if (method == "initialize")
                return new JsonObject
                {
                    ["protocolVersion"] = McpClient.ProtocolVersion,
                    ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                    ["serverInfo"] = new JsonObject { ["name"] = "hung", ["version"] = "1" },
                };
            if (method == "tools/call") await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return new JsonObject();
        });
        var client = new McpClient("hung", new PeerTransport(new JsonRpcPeer(pipes.ClientIn, pipes.ClientOut)), TimeSpan.FromMilliseconds(50));
        await client.InitializeAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.CallToolAsync("web_fetch", new JsonObject()));
        await client.DisposeAsync();
    }

    [Fact]
    public async Task InitializeIsCancelledByDefaultRpcDeadline()
    {
        await using var pipes = new RpcPipes();
        await using var server = new JsonRpcPeer(pipes.ServerIn, pipes.ServerOut, async (_, _, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return null;
        });
        var client = new McpClient("silent", new PeerTransport(new JsonRpcPeer(pipes.ClientIn, pipes.ClientOut)), rpcTimeout: TimeSpan.FromMilliseconds(40));
        var timer = System.Diagnostics.Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.InitializeAsync());

        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(2));
        await client.DisposeAsync();
    }

    [Fact]
    public void WebToolsAreDiscoverableAndParallelSafe()
    {
        Assert.Equal(TimeSpan.FromSeconds(60), McpClient.DefaultToolCallTimeout);
        Assert.True(ParallelSafeTools.IsParallelSafe("web__web_search"));
        Assert.True(ParallelSafeTools.IsParallelSafe("web__web_fetch"));
        Assert.False(ParallelSafeTools.IsParallelSafe("web__submit"));
    }

    [Fact]
    public async Task PromptsGetExpandsArguments()
    {
        using var tmp = new TempWorkspace();
        SeedStore(tmp);
        await using var session = await ConnectStore(tmp.Path);

        var got = await session.Client.GetPromptAsync(
            "review",
            new JsonObject { ["ARGUMENTS"] = "PR-9" });
        Assert.Single(got.Messages);
        var text = Assert.IsType<McpTextPart>(got.Messages[0].Content[0]).Text;
        Assert.Contains("PR-9", text);
    }

    [Fact]
    public async Task ResourcesReadReturnsSkillMarkdown()
    {
        using var tmp = new TempWorkspace();
        SeedStore(tmp);
        await using var session = await ConnectStore(tmp.Path);

        var contents = await session.Client.ReadResourceAsync("skill://store/demo/SKILL.md");
        Assert.Contains("Do the demo thing", contents[0].Text);
    }

    [Fact]
    public async Task PaginationWalksNextCursor()
    {
        await using var pipes = new RpcPipes();
        var calls = 0;
        await using var server = new JsonRpcPeer(pipes.ServerIn, pipes.ServerOut, (method, p, _) =>
        {
            if (method == "initialize")
            {
                return Task.FromResult<JsonNode?>(new JsonObject
                {
                    ["protocolVersion"] = McpClient.ProtocolVersion,
                    ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                    ["serverInfo"] = new JsonObject { ["name"] = "p", ["version"] = "1" },
                });
            }
            if (method != "tools/list") return Task.FromResult<JsonNode?>(new JsonObject());
            calls++;
            var cursor = p?["cursor"]?.GetValue<string>();
            if (cursor is null)
            {
                return Task.FromResult<JsonNode?>(new JsonObject
                {
                    ["tools"] = new JsonArray(new JsonObject { ["name"] = "a", ["inputSchema"] = new JsonObject { ["type"] = "object" } }),
                    ["nextCursor"] = "2",
                });
            }
            return Task.FromResult<JsonNode?>(new JsonObject
            {
                ["tools"] = new JsonArray(new JsonObject { ["name"] = "b", ["inputSchema"] = new JsonObject { ["type"] = "object" } }),
            });
        });
        await using var client = await McpClient.ConnectAsync(
            "p",
            new PeerTransport(new JsonRpcPeer(pipes.ClientIn, pipes.ClientOut)));

        var tools = await client.ListToolsAsync();
        Assert.Equal(2, calls);
        Assert.Equal(["a", "b"], tools.Select(t => t.Name));
    }

    internal static void SeedStore(TempWorkspace tmp)
    {
        tmp.File("demo/SKILL.md", """
            ---
            name: demo
            description: A demo skill
            version: 1.0.0
            tags: [test, demo]
            ---
            # Demo
            Do the demo thing.
            """);
        tmp.File("demo/notes.md", "extra file\n");
        tmp.File("prompts/review.md", """
            ---
            description: Review a change
            argument-hint: <id>
            ---
            Please review $ARGUMENTS
            """);
    }

    internal static async Task<StoreSession> ConnectStore(string root, string alias = "store")
    {
        var pipes = new RpcPipes();
        var host = new SkillStoreHost(root, alias);
        var server = host.Attach(pipes.ServerIn, pipes.ServerOut);
        var client = await McpClient.ConnectAsync(
            alias,
            new PeerTransport(new JsonRpcPeer(pipes.ClientIn, pipes.ClientOut)));
        return new StoreSession(pipes, server, client);
    }

    internal sealed class StoreSession : IAsyncDisposable
    {
        private readonly RpcPipes _pipes;
        private readonly JsonRpcPeer _server;
        public McpClient Client { get; }

        public StoreSession(RpcPipes pipes, JsonRpcPeer server, McpClient client)
        {
            _pipes = pipes;
            _server = server;
            Client = client;
        }

        public async ValueTask DisposeAsync()
        {
            await Client.DisposeAsync();
            await _server.DisposeAsync();
            await _pipes.DisposeAsync();
        }
    }
}
