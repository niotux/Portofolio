// Minimal JSON-RPC MCP server used by tools/PiSharp.SkillStore and in-process tests.
// Handles initialize, notifications/initialized, ping; other methods are registered by the host.
using System.Text.Json.Nodes;

namespace PiSharp.Core.Mcp;

/// <summary>Dispatches MCP methods over a <see cref="JsonRpcPeer"/>.</summary>
public sealed class McpServer
{
    private readonly Dictionary<string, Func<JsonNode?, CancellationToken, Task<JsonNode?>>> _handlers =
        new(StringComparer.Ordinal);

    public string Name { get; }
    public string Version { get; }
    public JsonObject Capabilities { get; set; } = new();
    public string ProtocolVersion { get; set; } = McpClient.ProtocolVersion;

    /// <summary>Raised when the client sends <c>notifications/initialized</c>.</summary>
    public event Action? Initialized;

    public McpServer(string name, string version = "0.1.0")
    {
        Name = name;
        Version = version;
        Handle("initialize", InitializeAsync);
        Handle("ping", (_, _) => Task.FromResult<JsonNode?>(new JsonObject()));
    }

    public void Handle(string method, Func<JsonNode?, CancellationToken, Task<JsonNode?>> handler) =>
        _handlers[method] = handler;

    public void Handle(string method, Func<JsonNode?, JsonNode?> handler) =>
        _handlers[method] = (p, _) => Task.FromResult(handler(p));

    public JsonRpcPeer Attach(Stream input, Stream output)
    {
        var peer = new JsonRpcPeer(input, output, DispatchAsync);
        peer.Notification += (method, _) =>
        {
            if (method == "notifications/initialized") Initialized?.Invoke();
        };
        return peer;
    }

    public async Task ServeAsync(Stream input, Stream output, CancellationToken cancellationToken = default)
    {
        await using var peer = Attach(input, output);
        try
        {
            await peer.Completion.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // shut down
        }
    }

    private Task<JsonNode?> DispatchAsync(string method, JsonNode? @params, CancellationToken cancellationToken)
    {
        if (_handlers.TryGetValue(method, out var handler))
            return handler(@params, cancellationToken);

        throw new JsonRpcException(JsonRpcException.MethodNotFound, $"Method not found: {method}");
    }

    private Task<JsonNode?> InitializeAsync(JsonNode? _, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        JsonNode result = new JsonObject
        {
            ["protocolVersion"] = ProtocolVersion,
            ["capabilities"] = Capabilities.DeepClone(),
            ["serverInfo"] = new JsonObject
            {
                ["name"] = Name,
                ["version"] = Version,
            },
        };
        return Task.FromResult<JsonNode?>(result);
    }
}
