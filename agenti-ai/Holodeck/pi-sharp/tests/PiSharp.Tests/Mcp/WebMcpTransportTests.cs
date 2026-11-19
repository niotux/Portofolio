using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using PiSharp.Core.Mcp;

namespace PiSharp.Tests.Mcp;

public class WebMcpTransportTests
{
    [Fact]
    public async Task ConnectDiscoversAndCallsBothAuthenticatedWebTools()
    {
        var paths = new List<string>();
        using var http = new HttpClient(new StubHandler((request, _) =>
        {
            Assert.Equal("Bearer seat-token", request.Headers.Authorization?.ToString());
            paths.Add(request.RequestUri!.AbsolutePath);
            var body = request.RequestUri.AbsolutePath switch
            {
                "/mcp/tools" => """{"tools":[{"name":"web_search","description":"Search","inputSchema":{"type":"object"}},{"name":"web_fetch","description":"Fetch","inputSchema":{"type":"object"}}]}""",
                "/mcp/tools/web_search" => """{"results":[{"title":"A","url":"https://example.com","snippet":"result"}]}""",
                "/mcp/tools/web_fetch" => """{"url":"https://example.com","markdown":"page","truncated":false}""",
                _ => "{}",
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }));
        await using var client = await McpClient.ConnectAsync(
            "web",
            new WebMcpTransport("http://localhost:8082", new Dictionary<string, string>
            {
                ["Authorization"] = "Bearer seat-token",
            }, http));
        await using var manager = new McpManager();
        await manager.AddClientAsync(new McpServerConfig { Alias = "web", Kind = McpTransportKind.Http }, client);

        var search = Assert.Single(manager.Tools(), tool => tool.Name == "web__web_search");
        var fetch = Assert.Single(manager.Tools(), tool => tool.Name == "web__web_fetch");
        Assert.Contains("example.com", (await search.ExecuteAsync("s", new JsonObject { ["query"] = "test" }, null, CancellationToken.None)).Text);
        Assert.Contains("markdown", (await fetch.ExecuteAsync("f", new JsonObject { ["url"] = "https://example.com" }, null, CancellationToken.None)).Text);
        Assert.Equal(["/mcp/tools", "/mcp/tools/web_search", "/mcp/tools/web_fetch"], paths);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
