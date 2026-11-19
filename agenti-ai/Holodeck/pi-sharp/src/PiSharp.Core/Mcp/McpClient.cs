// Hand-written MCP client: initialize, tools/*, prompts/*, resources/*, ping.
// No SDK. Transports in McpTransport.cs. See docs/02-library-map.md and docs/04.
using System.Text.Json.Nodes;

namespace PiSharp.Core.Mcp;

public sealed record McpImplementation(string Name, string Version);

public sealed record McpToolInfo(string Name, string? Description, JsonObject InputSchema);

public sealed record McpPromptArgument(string Name, string? Description, bool Required);

public sealed record McpPromptInfo(
    string Name,
    string? Title,
    string? Description,
    IReadOnlyList<McpPromptArgument> Arguments);

public sealed record McpPromptMessage(string Role, IReadOnlyList<McpContentPart> Content);

public sealed record McpGetPromptResult(string? Description, IReadOnlyList<McpPromptMessage> Messages);

public sealed record McpResourceInfo(
    string Uri,
    string Name,
    string? Title,
    string? Description,
    string? MimeType);

public sealed record McpResourceTemplateInfo(string UriTemplate, string Name, string? Description, string? MimeType);

public sealed record McpResourceContents(string Uri, string? MimeType, string? Text, string? Blob);

public abstract record McpContentPart;

public sealed record McpTextPart(string Text) : McpContentPart;
public sealed record McpImagePart(string Data, string MimeType) : McpContentPart;
public sealed record McpResourcePart(McpResourceContents Resource) : McpContentPart;
public sealed record McpResourceLinkPart(string Uri, string? Name, string? MimeType) : McpContentPart;

public sealed record McpCallToolResult(IReadOnlyList<McpContentPart> Content, bool IsError);

/// <summary>One connected MCP server after a successful initialize handshake.</summary>
public sealed class McpClient : IAsyncDisposable
{
    public const string ProtocolVersion = "2025-06-18";
    public static readonly TimeSpan DefaultToolCallTimeout = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan DefaultRpcTimeout = TimeSpan.FromSeconds(30);
    public const string ClientName = "pi-sharp";
    public const string ClientVersion = "0.1.0";

    private readonly IMcpTransport _transport;
    private readonly TimeSpan _toolCallTimeout;
    private readonly TimeSpan _rpcTimeout;

    public string Alias { get; }
    /// <summary>The per-tool-call deadline this client enforces (<see cref="DefaultToolCallTimeout"/> unless a shorter
    /// one was passed to the constructor); callers layer their own budgets on top of it.</summary>
    public TimeSpan ToolCallTimeout => _toolCallTimeout;
    public McpImplementation ServerInfo { get; private set; } = new("unknown", "0");
    public JsonObject Capabilities { get; private set; } = new();
    public string NegotiatedVersion { get; private set; } = ProtocolVersion;

    public event Action<string, JsonNode?>? Notification
    {
        add => _transport.Notification += value;
        remove => _transport.Notification -= value;
    }

    public McpClient(string alias, IMcpTransport transport, TimeSpan? toolCallTimeout = null, TimeSpan? rpcTimeout = null)
    {
        Alias = alias;
        _transport = transport;
        _toolCallTimeout = toolCallTimeout ?? DefaultToolCallTimeout;
        _rpcTimeout = rpcTimeout ?? DefaultRpcTimeout;
    }

    public static async Task<McpClient> ConnectAsync(McpServerConfig config, CancellationToken cancellationToken = default)
    {
        IMcpTransport transport = config.Kind == McpTransportKind.Http
            ? string.Equals(config.Protocol, "web", StringComparison.OrdinalIgnoreCase)
                ? new WebMcpTransport(config.Url!, config.Headers)
                : new StreamableHttpTransport(config.Url!, config.Headers)
            : StdioTransport.Start(config);
        var client = new McpClient(config.Alias, transport);
        try
        {
            await client.InitializeAsync(cancellationToken).ConfigureAwait(false);
            return client;
        }
        catch
        {
            await client.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public static async Task<McpClient> ConnectAsync(
        string alias,
        IMcpTransport transport,
        CancellationToken cancellationToken = default)
    {
        var client = new McpClient(alias, transport);
        await client.InitializeAsync(cancellationToken).ConfigureAwait(false);
        return client;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var result = await SendRequestWithTimeoutAsync("initialize", new JsonObject
        {
            ["protocolVersion"] = ProtocolVersion,
            ["capabilities"] = new JsonObject(),
            ["clientInfo"] = new JsonObject
            {
                ["name"] = ClientName,
                ["version"] = ClientVersion,
            },
        }, cancellationToken).ConfigureAwait(false);

        var obj = result as JsonObject
            ?? throw new JsonRpcException(JsonRpcException.InvalidParams, "initialize returned no object");

        NegotiatedVersion = obj["protocolVersion"]?.GetValue<string>() ?? ProtocolVersion;
        if (obj["capabilities"] is JsonObject caps) Capabilities = caps;
        if (obj["serverInfo"] is JsonObject info)
        {
            ServerInfo = new McpImplementation(
                info["name"]?.GetValue<string>() ?? "unknown",
                info["version"]?.GetValue<string>() ?? "0");
        }

        await _transport.SendNotificationAsync("notifications/initialized", null, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<McpToolInfo>> ListToolsAsync(CancellationToken cancellationToken = default)
    {
        if (!HasCapability("tools")) return [];
        try
        {
            return await ListPagesAsync("tools/list", "tools", ParseTool, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonRpcException ex) when (ex.Code == JsonRpcException.MethodNotFound)
        {
            return [];
        }
    }

    public async Task<McpCallToolResult> CallToolAsync(
        string name,
        JsonObject? arguments,
        CancellationToken cancellationToken = default)
    {
        // Use a bare CTS instead of CreateLinkedTokenSource: CancelAfter on a linked source whose
        // caller token is already canceled fires its callback immediately (it was scheduled at
        // construction), canceling the request before the server even answers. The registration
        // keeps the deadline CTS in sync with the caller's cancellation; CancelAfter layers the
        // per-call deadline on top.
        var timeout = new CancellationTokenSource();
        using var reg = cancellationToken.Register(() =>
        {
            try { timeout.Cancel(); } catch { /* disposed */ }
        });
        timeout.CancelAfter(_toolCallTimeout);
        try
        {
            var result = await _transport.SendRequestAsync("tools/call", new JsonObject
            {
                ["name"] = name,
                ["arguments"] = arguments ?? new JsonObject(),
            }, timeout.Token).ConfigureAwait(false);


            var obj = result as JsonObject ?? new JsonObject();
            var isError = obj["isError"]?.GetValue<bool>() ?? false;
            return new McpCallToolResult(ParseContent(obj["content"]), isError);
        }
        finally
        {
            timeout.Dispose();
        }
    }

    public async Task<IReadOnlyList<McpPromptInfo>> ListPromptsAsync(CancellationToken cancellationToken = default)
    {
        if (!HasCapability("prompts")) return [];
        try
        {
            return await ListPagesAsync("prompts/list", "prompts", ParsePrompt, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonRpcException ex) when (ex.Code == JsonRpcException.MethodNotFound)
        {
            return [];
        }
    }

    public async Task<McpGetPromptResult> GetPromptAsync(
        string name,
        JsonObject? arguments,
        CancellationToken cancellationToken = default)
    {
        var p = new JsonObject { ["name"] = name };
        if (arguments is not null) p["arguments"] = arguments;
        var result = await SendRequestWithTimeoutAsync("prompts/get", p, cancellationToken).ConfigureAwait(false);
        var obj = result as JsonObject ?? new JsonObject();
        var messages = new List<McpPromptMessage>();
        if (obj["messages"] is JsonArray arr)
        {
            foreach (var item in arr)
            {
                if (item is not JsonObject m) continue;
                var role = m["role"]?.GetValue<string>() ?? "user";
                messages.Add(new McpPromptMessage(role, ParsePromptContent(m["content"])));
            }
        }
        return new McpGetPromptResult(obj["description"]?.GetValue<string>(), messages);
    }

    public async Task<IReadOnlyList<McpResourceInfo>> ListResourcesAsync(CancellationToken cancellationToken = default)
    {
        if (!HasCapability("resources")) return [];
        try
        {
            return await ListPagesAsync("resources/list", "resources", ParseResource, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonRpcException ex) when (ex.Code == JsonRpcException.MethodNotFound)
        {
            return [];
        }
    }

    public async Task<IReadOnlyList<McpResourceTemplateInfo>> ListResourceTemplatesAsync(
        CancellationToken cancellationToken = default)
    {
        if (!HasCapability("resources")) return [];
        try
        {
            return await ListPagesAsync(
                "resources/templates/list",
                "resourceTemplates",
                ParseResourceTemplate,
                cancellationToken).ConfigureAwait(false);
        }
        catch (JsonRpcException ex) when (ex.Code == JsonRpcException.MethodNotFound)
        {
            return [];
        }
    }

    public async Task<IReadOnlyList<McpResourceContents>> ReadResourceAsync(
        string uri,
        CancellationToken cancellationToken = default)
    {
        var result = await SendRequestWithTimeoutAsync(
            "resources/read",
            new JsonObject { ["uri"] = uri },
            cancellationToken).ConfigureAwait(false);
        var obj = result as JsonObject ?? new JsonObject();
        var list = new List<McpResourceContents>();
        if (obj["contents"] is JsonArray arr)
        {
            foreach (var item in arr)
            {
                if (item is not JsonObject c) continue;
                list.Add(new McpResourceContents(
                    c["uri"]?.GetValue<string>() ?? uri,
                    c["mimeType"]?.GetValue<string>(),
                    c["text"]?.GetValue<string>(),
                    c["blob"]?.GetValue<string>()));
            }
        }
        return list;
    }

    public Task PingAsync(CancellationToken cancellationToken = default) =>
        SendRequestWithTimeoutAsync("ping", new JsonObject(), cancellationToken);

    public ValueTask DisposeAsync() => _transport.DisposeAsync();

    /// <summary>
    /// True when the server advertised <paramref name="name"/>, or advertised nothing
    /// (some servers omit the capabilities object; we try the method and swallow method-not-found).
    /// </summary>
    public bool HasCapability(string name) =>
        Capabilities.Count == 0
        || (Capabilities.ContainsKey(name) && Capabilities[name] is not null);

    private async Task<JsonNode?> SendRequestWithTimeoutAsync(string method, JsonNode? parameters, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_rpcTimeout);
        return await _transport.SendRequestAsync(method, parameters, timeout.Token).ConfigureAwait(false);
    }

    private async Task<List<T>> ListPagesAsync<T>(
        string method,
        string arrayKey,
        Func<JsonObject, T?> parse,
        CancellationToken cancellationToken)
    {
        var all = new List<T>();
        string? cursor = null;
        do
        {
            JsonObject? p = cursor is null ? null : new JsonObject { ["cursor"] = cursor };
            var result = await SendRequestWithTimeoutAsync(method, p, cancellationToken).ConfigureAwait(false);
            var obj = result as JsonObject ?? new JsonObject();
            if (obj[arrayKey] is JsonArray arr)
            {
                foreach (var item in arr)
                {
                    if (item is JsonObject o)
                    {
                        var parsed = parse(o);
                        if (parsed is not null) all.Add(parsed);
                    }
                }
            }
            cursor = obj["nextCursor"]?.GetValue<string>();
            if (string.IsNullOrEmpty(cursor)) cursor = null;
        } while (cursor is not null);
        return all;
    }

    private static McpToolInfo ParseTool(JsonObject o)
    {
        var schema = o["inputSchema"] as JsonObject ?? new JsonObject { ["type"] = "object" };
        return new McpToolInfo(
            o["name"]?.GetValue<string>() ?? "",
            o["description"]?.GetValue<string>(),
            (JsonObject)schema.DeepClone());
    }

    private static McpPromptInfo ParsePrompt(JsonObject o)
    {
        var args = new List<McpPromptArgument>();
        if (o["arguments"] is JsonArray arr)
        {
            foreach (var item in arr)
            {
                if (item is not JsonObject a) continue;
                args.Add(new McpPromptArgument(
                    a["name"]?.GetValue<string>() ?? "",
                    a["description"]?.GetValue<string>(),
                    a["required"]?.GetValue<bool>() ?? false));
            }
        }
        return new McpPromptInfo(
            o["name"]?.GetValue<string>() ?? "",
            o["title"]?.GetValue<string>(),
            o["description"]?.GetValue<string>(),
            args);
    }

    private static McpResourceInfo ParseResource(JsonObject o) => new(
        o["uri"]?.GetValue<string>() ?? "",
        o["name"]?.GetValue<string>() ?? "",
        o["title"]?.GetValue<string>(),
        o["description"]?.GetValue<string>(),
        o["mimeType"]?.GetValue<string>());

    private static McpResourceTemplateInfo ParseResourceTemplate(JsonObject o) => new(
        o["uriTemplate"]?.GetValue<string>() ?? "",
        o["name"]?.GetValue<string>() ?? "",
        o["description"]?.GetValue<string>(),
        o["mimeType"]?.GetValue<string>());

    internal static IReadOnlyList<McpContentPart> ParseContent(JsonNode? node)
    {
        var list = new List<McpContentPart>();
        if (node is not JsonArray arr) return list;
        foreach (var item in arr)
        {
            if (item is not JsonObject o) continue;
            var type = o["type"]?.GetValue<string>();
            switch (type)
            {
                case "text":
                    list.Add(new McpTextPart(o["text"]?.GetValue<string>() ?? ""));
                    break;
                case "image":
                    list.Add(new McpImagePart(
                        o["data"]?.GetValue<string>() ?? "",
                        o["mimeType"]?.GetValue<string>() ?? "image/png"));
                    break;
                case "resource":
                    if (o["resource"] is JsonObject r)
                    {
                        list.Add(new McpResourcePart(new McpResourceContents(
                            r["uri"]?.GetValue<string>() ?? "",
                            r["mimeType"]?.GetValue<string>(),
                            r["text"]?.GetValue<string>(),
                            r["blob"]?.GetValue<string>())));
                    }
                    break;
                case "resource_link":
                    list.Add(new McpResourceLinkPart(
                        o["uri"]?.GetValue<string>() ?? "",
                        o["name"]?.GetValue<string>(),
                        o["mimeType"]?.GetValue<string>()));
                    break;
            }
        }
        return list;
    }

    private static IReadOnlyList<McpContentPart> ParsePromptContent(JsonNode? node)
    {
        if (node is JsonValue v && v.GetValueKind() == System.Text.Json.JsonValueKind.String)
            return [new McpTextPart(v.GetValue<string>())];
        if (node is JsonObject obj)
        {
            var type = obj["type"]?.GetValue<string>();
            if (type == "text" || type is null)
                return [new McpTextPart(obj["text"]?.GetValue<string>() ?? "")];
            return ParseContent(new JsonArray(obj.DeepClone()));
        }
        return ParseContent(node);
    }
}
