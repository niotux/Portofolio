using System.Text.Json.Nodes;
using PiSharp.Core.Mcp;
using PiSharp.Core.Skills;
using PiSharp.Tests.Tools;

namespace PiSharp.Tests.Mcp;

public class McpManagerTests
{
    [Fact]
    public void FormatStatusEmpty()
    {
        var mcp = new McpManager();
        Assert.Equal("No MCP servers configured.", mcp.FormatStatus());
    }

    [Fact]
    public async Task ConnectAllStdioAndFormatStatus()
    {
        var dll = Path.Combine(AppContext.BaseDirectory, "PiSharp.SkillStore.dll");
        using var tmp = new TempWorkspace();
        McpClientTests.SeedStore(tmp);
        var cfg = new McpConfig([
            new McpServerConfig
            {
                Alias = "store",
                Kind = McpTransportKind.Stdio,
                Command = "dotnet",
                Args = [dll, tmp.Path, "store"],
            },
        ]);

        await using var manager = await McpManager.ConnectAllAsync(cfg);
        var status = manager.FormatStatus();
        Assert.Contains("store", status);
        Assert.Contains("tools=", status);
        Assert.Contains(manager.Tools(), t => t.Name == "store__search_skills");

        var skills = SkillLoader.FromMcpResources(manager.SkillResources());
        Assert.Contains(skills, s => s.Name == "demo" && s.IsRemote);

        Assert.True(manager.CanResolve("skill://store/demo/SKILL.md"));
        var text = await manager.ReadTextAsync("skill://store/demo/SKILL.md");
        Assert.Contains("Do the demo thing", text);
    }

    [Fact]
    public async Task WebMcpToolsAreDiscoveredAndCallableThroughTheSessionManager()
    {
        await using var pipes = new RpcPipes();
        await using var server = new JsonRpcPeer(pipes.ServerIn, pipes.ServerOut, (method, p, _) =>
        {
            JsonNode? result = method switch
            {
                "initialize" => new JsonObject
                {
                    ["protocolVersion"] = McpClient.ProtocolVersion,
                    ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                    ["serverInfo"] = new JsonObject { ["name"] = "web", ["version"] = "1" },
                },
                "tools/list" => new JsonObject(),
                "tools/call" => new JsonObject
                {
                    ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = p?["name"]?.GetValue<string>() == "web_search" ? "search results" : "fetched page" }),
                },
                _ => new JsonObject(),
            };
            if (method == "tools/list")
                result = new JsonObject { ["tools"] = new JsonArray(new[] { "web_search", "web_fetch" }.Select(name => (JsonNode)new JsonObject
                {
                    ["name"] = name,
                    ["description"] = name,
                    ["inputSchema"] = new JsonObject { ["type"] = "object" },
                }).ToArray()) };
            return Task.FromResult<JsonNode?>(result);
        });
        var client = await McpClient.ConnectAsync("web", new PeerTransport(new JsonRpcPeer(pipes.ClientIn, pipes.ClientOut)));
        await using var manager = new McpManager();
        await manager.AddClientAsync(new McpServerConfig { Alias = "web", Kind = McpTransportKind.Http, Url = "http://localhost/mcp" }, client);

        var search = Assert.Single(manager.Tools(), tool => tool.Name == "web__web_search");
        var fetch = Assert.Single(manager.Tools(), tool => tool.Name == "web__web_fetch");
        Assert.Contains("search results", (await search.ExecuteAsync("s", new JsonObject { ["query"] = "test" }, null, CancellationToken.None)).Text);
        Assert.Contains("fetched page", (await fetch.ExecuteAsync("f", new JsonObject { ["url"] = "https://example.com" }, null, CancellationToken.None)).Text);
    }

    [Fact]
    public async Task FailedServerIsRecordedNotThrown()
    {
        var cfg = new McpConfig([
            new McpServerConfig
            {
                Alias = "gone",
                Kind = McpTransportKind.Stdio,
                Command = "this-binary-does-not-exist-pi-sharp",
            },
        ]);
        await using var manager = await McpManager.ConnectAllAsync(cfg);
        var conn = Assert.Single(manager.Connections);
        Assert.Null(conn.Client);
        Assert.NotNull(conn.Error);
        Assert.Contains("disconnected", manager.FormatStatus());
    }

    [Fact]
    public async Task ListChangedNotificationRefreshesSnapshot()
    {
        await using var pipes = new RpcPipes();
        var listChanged = false;
        var refreshed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = new JsonRpcPeer(pipes.ServerIn, pipes.ServerOut, (method, _, _) =>
        {
            if (method == "initialize")
            {
                return Task.FromResult<JsonNode?>(new JsonObject
                {
                    ["protocolVersion"] = McpClient.ProtocolVersion,
                    ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = true } },
                    ["serverInfo"] = new JsonObject { ["name"] = "dynamic", ["version"] = "1" },
                });
            }
            if (method == "tools/list")
            {
                var names = listChanged ? new[] { "first", "second" } : new[] { "first" };
                return Task.FromResult<JsonNode?>(new JsonObject
                {
                    ["tools"] = new JsonArray(names.Select(name => (JsonNode)new JsonObject
                    {
                        ["name"] = name,
                        ["inputSchema"] = new JsonObject { ["type"] = "object" },
                    }).ToArray()),
                });
            }
            return Task.FromResult<JsonNode?>(new JsonObject());
        });
        var peer = new JsonRpcPeer(pipes.ClientIn, pipes.ClientOut);
        var connected = await McpClient.ConnectAsync("dynamic", new PeerTransport(peer));
        await using var manager = new McpManager();
        manager.ListsChanged += _ => refreshed.TrySetResult();
        await manager.AddClientAsync(
            new McpServerConfig { Alias = "dynamic", Kind = McpTransportKind.Http, Url = "http://unused" },
            connected);
        Assert.Single(manager.Connections[0].Tools);

        listChanged = true;
        await server.NotifyAsync("notifications/tools/list_changed", null);
        await refreshed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(["first", "second"], manager.Connections[0].Tools.Select(tool => tool.Name));
    }

    [Fact]
    public async Task AStatelessServerThatNeverNotifies_IsPolled_AtMostOncePerInterval()
    {
        await using var pipes = new RpcPipes();
        var grown = false;
        var lists = 0;
        await using var server = new JsonRpcPeer(pipes.ServerIn, pipes.ServerOut, (method, _, _) =>
        {
            if (method == "initialize")
            {
                return Task.FromResult<JsonNode?>(new JsonObject
                {
                    ["protocolVersion"] = McpClient.ProtocolVersion,
                    ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                    ["serverInfo"] = new JsonObject { ["name"] = "kb", ["version"] = "1.0.79" },
                });
            }
            if (method == "tools/list")
            {
                Interlocked.Increment(ref lists);
                var names = grown ? new[] { "kb_list", "kb_decision" } : new[] { "kb_list" };
                return Task.FromResult<JsonNode?>(new JsonObject
                {
                    ["tools"] = new JsonArray(names.Select(name => (JsonNode)new JsonObject
                    {
                        ["name"] = name,
                        ["inputSchema"] = new JsonObject { ["type"] = "object" },
                    }).ToArray()),
                });
            }
            return Task.FromResult<JsonNode?>(new JsonObject());
        });
        var peer = new JsonRpcPeer(pipes.ClientIn, pipes.ClientOut);
        var connected = await McpClient.ConnectAsync("kb", new PeerTransport(peer));
        await using var manager = new McpManager();
        var raised = new List<string>();
        manager.ListsChanged += raised.Add;
        await manager.AddClientAsync(new McpServerConfig { Alias = "kb", Kind = McpTransportKind.Http, Url = "http://unused" }, connected);
        var t0 = new DateTime(2026, 10, 4, 7, 0, 0, DateTimeKind.Utc);

        Assert.False(await manager.PollToolsAsync(t0));
        Assert.Empty(raised);

        // The server is redeployed with a new tool and says nothing.
        grown = true;
        var before = lists;
        Assert.False(await manager.PollToolsAsync(t0.AddSeconds(30)));
        Assert.Equal(before, lists);

        Assert.True(await manager.PollToolsAsync(t0 + McpManager.ToolPollInterval));
        Assert.Equal(["kb"], raised);
        Assert.Equal(["kb_list", "kb_decision"], manager.Connections[0].Tools.Select(tool => tool.Name));
    }

    // demo-02/demo-03, 2026-10-28: started while strata's relay was restarting, the KB forward refused, and the seat ran
    // without its KB tools until an operator restarted it. The tool poll now connects such a server once it answers.
    [Fact]
    public async Task AServerDownAtStartIsConnectedByTheToolPollOnceItAnswers()
    {
        using var server = new System.Net.HttpListener();
        string url = "";
        for (var tries = 0; ; tries++)
        {
            var probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            probe.Start();
            url = $"http://127.0.0.1:{((System.Net.IPEndPoint)probe.LocalEndpoint).Port}/mcp/";
            probe.Stop();
            server.Prefixes.Clear();
            server.Prefixes.Add(url);
            try { server.Start(); break; }
            catch (System.Net.HttpListenerException) when (tries < 5) { }
        }
        var up = false;
        using var stop = new CancellationTokenSource();
        var serving = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                System.Net.HttpListenerContext context;
                try { context = await server.GetContextAsync(); } catch { return; }
                if (!Volatile.Read(ref up)) { context.Response.StatusCode = 503; context.Response.Close(); continue; }
                using var reader = new StreamReader(context.Request.InputStream);
                var request = JsonNode.Parse(await reader.ReadToEndAsync())!.AsObject();
                if (request["id"] is not { } id) { context.Response.StatusCode = 202; context.Response.Close(); continue; }
                JsonNode result = request["method"]!.GetValue<string>() switch
                {
                    "initialize" => new JsonObject
                    {
                        ["protocolVersion"] = McpClient.ProtocolVersion,
                        ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                        ["serverInfo"] = new JsonObject { ["name"] = "kb", ["version"] = "1.0.312" },
                    },
                    "tools/list" => new JsonObject { ["tools"] = new JsonArray(new JsonObject { ["name"] = "kb_list", ["inputSchema"] = new JsonObject { ["type"] = "object" } }) },
                    _ => new JsonObject(),
                };
                var body = System.Text.Encoding.UTF8.GetBytes(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id.DeepClone(), ["result"] = result }.ToJsonString());
                context.Response.ContentType = "application/json";
                await context.Response.OutputStream.WriteAsync(body);
                context.Response.Close();
            }
        });

        var config = new McpServerConfig { Alias = "kb", Kind = McpTransportKind.Http, Url = url };
        await using var manager = await McpManager.ConnectAllAsync(new McpConfig([config]));
        Assert.NotNull(manager.Connections[0].Error);
        Assert.Empty(manager.Tools());
        var raised = new List<string>();
        manager.ListsChanged += raised.Add;

        Volatile.Write(ref up, true);
        // The poll comes back every minute; under a loaded test run one connect may time out, as it may on a seat.
        var t0 = DateTime.UtcNow;
        var connected = false;
        for (var round = 0; round < 5 && !connected; round++)
            connected = await manager.PollToolsAsync(t0 + McpManager.ToolPollInterval * round);
        Assert.True(connected);
        Assert.Null(manager.Connections[0].Error);
        Assert.Equal(["kb"], raised);
        Assert.Contains(manager.Tools(), tool => tool.Name == "kb__kb_list");
        stop.Cancel();
        server.Stop();
    }
}
