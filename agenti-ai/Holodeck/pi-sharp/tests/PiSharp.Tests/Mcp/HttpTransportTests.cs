using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using PiSharp.Core.Mcp;

namespace PiSharp.Tests.Mcp;

public class HttpTransportTests
{
    [Fact]
    public async Task JsonPostInitializeAndCallTool()
    {
        var handler = new FakeMcpHandler { Mode = FakeMcpHandler.BodyMode.Json };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://mcp.test/") };
        await using var transport = new StreamableHttpTransport("http://mcp.test/mcp", http: http);
        await using var client = await McpClient.ConnectAsync("remote", transport);

        Assert.Equal("fake-http", client.ServerInfo.Name);
        Assert.Equal("sess-1", handler.LastSessionId);
        Assert.Equal("sess-1", transport.SessionId);

        var tools = await client.ListToolsAsync();
        Assert.Equal("echo", tools[0].Name);

        var result = await client.CallToolAsync("echo", new JsonObject { ["text"] = "hi" });
        Assert.Equal("hi", Assert.IsType<McpTextPart>(result.Content[0]).Text);
        Assert.Equal("sess-1", handler.LastSessionId);
    }

    [Fact]
    public async Task SsePostReturnsJsonRpcOnDataFrame()
    {
        var handler = new FakeMcpHandler { Mode = FakeMcpHandler.BodyMode.Sse };
        using var http = new HttpClient(handler);
        await using var transport = new StreamableHttpTransport("http://mcp.test/mcp", http: http);
        await using var client = await McpClient.ConnectAsync("remote", transport);

        Assert.Equal("fake-http", client.ServerInfo.Name);
        var tools = await client.ListToolsAsync();
        Assert.Single(tools);
    }

    [Fact]
    public async Task HeadersAreSentAndEnvExpanded()
    {
        Environment.SetEnvironmentVariable("PI_SHARP_TEST_TOKEN", "secret");
        try
        {
            var handler = new FakeMcpHandler();
            using var http = new HttpClient(handler);
            await using var transport = new StreamableHttpTransport(
                "http://mcp.test/mcp",
                new Dictionary<string, string> { ["Authorization"] = "Bearer ${PI_SHARP_TEST_TOKEN}" },
                http);
            await using var client = await McpClient.ConnectAsync("remote", transport);
            Assert.Equal("Bearer secret", handler.LastAuthorization);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PI_SHARP_TEST_TOKEN", null);
        }
    }
}

internal sealed class FakeMcpHandler : HttpMessageHandler
{
    public enum BodyMode { Json, Sse }

    public BodyMode Mode { get; set; } = BodyMode.Json;
    public string? LastSessionId { get; private set; }
    public string? LastAuthorization { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Headers.TryGetValues("Authorization", out var auth))
            LastAuthorization = auth.FirstOrDefault();
        if (request.Headers.TryGetValues("Mcp-Session-Id", out var sid))
            LastSessionId = sid.FirstOrDefault();

        var body = request.Content is null ? "{}" : await request.Content.ReadAsStringAsync(cancellationToken);
        var obj = JsonNode.Parse(body)!.AsObject();
        var method = obj["method"]?.GetValue<string>();
        var id = obj["id"]?.DeepClone();

        JsonNode result = method switch
        {
            "initialize" => new JsonObject
            {
                ["protocolVersion"] = McpClient.ProtocolVersion,
                ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                ["serverInfo"] = new JsonObject { ["name"] = "fake-http", ["version"] = "1" },
            },
            "tools/list" => new JsonObject
            {
                ["tools"] = new JsonArray(new JsonObject
                {
                    ["name"] = "echo",
                    ["description"] = "echo",
                    ["inputSchema"] = new JsonObject { ["type"] = "object" },
                }),
            },
            "tools/call" => new JsonObject
            {
                ["content"] = new JsonArray(new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = obj["params"]?["arguments"]?["text"]?.GetValue<string>() ?? "",
                }),
                ["isError"] = false,
            },
            _ => new JsonObject(),
        };

        var frame = new JsonObject { ["jsonrpc"] = "2.0", ["result"] = result };
        if (id is not null) frame["id"] = id;

        var resp = new HttpResponseMessage(HttpStatusCode.OK);
        resp.Headers.TryAddWithoutValidation("Mcp-Session-Id", "sess-1");
        LastSessionId = "sess-1";

        if (Mode == BodyMode.Sse)
        {
            var sse = $": keep-alive\n\ndata: {frame.ToJsonString()}\n\n";
            resp.Content = new StringContent(sse, Encoding.UTF8, "text/event-stream");
        }
        else
        {
            resp.Content = new StringContent(frame.ToJsonString(), Encoding.UTF8, "application/json");
        }
        return resp;
    }
}
