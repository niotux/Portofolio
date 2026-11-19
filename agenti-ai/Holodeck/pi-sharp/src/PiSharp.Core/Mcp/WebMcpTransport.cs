using System.Text;
using System.Text.Json.Nodes;

namespace PiSharp.Core.Mcp;

/// <summary>Adapts the web service's authenticated REST endpoints to PiSharp's MCP client surface.</summary>
public sealed class WebMcpTransport : IMcpTransport
{
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly Uri _baseUri;
    private readonly IReadOnlyDictionary<string, string> _headers;

    public event Action<string, JsonNode?>? Notification
    {
        add { }
        remove { }
    }

    public WebMcpTransport(string url, IReadOnlyDictionary<string, string>? headers = null, HttpClient? http = null)
    {
        _baseUri = new Uri(url.TrimEnd('/') + "/", UriKind.Absolute);
        _headers = headers ?? new Dictionary<string, string>();
        _http = http ?? new HttpClient();
        _ownsHttp = http is null;
    }

    public async Task<JsonNode?> SendRequestAsync(string method, JsonNode? @params, CancellationToken cancellationToken)
    {
        switch (method)
        {
            case "initialize":
                return new JsonObject
                {
                    ["protocolVersion"] = McpClient.ProtocolVersion,
                    ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                    ["serverInfo"] = new JsonObject { ["name"] = "pi-sharp-web-mcp", ["version"] = "1" },
                };
            case "tools/list":
            {
                using var response = await SendAsync(HttpMethod.Get, "mcp/tools", null, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                var tools = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)) as JsonObject;
                return tools ?? new JsonObject { ["tools"] = new JsonArray() };
            }
            case "tools/call":
            {
                var name = @params?["name"]?.GetValue<string>() ?? throw new InvalidOperationException("MCP tool name is required.");
                if (name is not ("web_search" or "web_fetch"))
                    throw new InvalidOperationException($"Unsupported web MCP tool: {name}");
                var args = @params?["arguments"]?.DeepClone() ?? new JsonObject();
                using var response = await SendAsync(HttpMethod.Post, $"mcp/tools/{name}", args, cancellationToken).ConfigureAwait(false);
                var responseText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var result = string.IsNullOrWhiteSpace(responseText) ? null : JsonNode.Parse(responseText);
                var error = !response.IsSuccessStatusCode || result?["error"] is not null;
                return new JsonObject
                {
                    ["isError"] = error,
                    ["content"] = new JsonArray(new JsonObject
                    {
                        ["type"] = "text",
                        ["text"] = result?.ToJsonString() ?? $"HTTP {(int)response.StatusCode}",
                    }),
                };
            }
            default:
                throw new InvalidOperationException($"Unsupported web MCP request: {method}");
        }
    }

    public Task SendNotificationAsync(string method, JsonNode? @params, CancellationToken cancellationToken) => Task.CompletedTask;

    public ValueTask DisposeAsync()
    {
        if (_ownsHttp) _http.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, JsonNode? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(_baseUri, path));
        if (body is not null) request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        foreach (var (key, value) in _headers)
            request.Headers.TryAddWithoutValidation(key, McpConfig.ExpandEnv(value));
        return await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
