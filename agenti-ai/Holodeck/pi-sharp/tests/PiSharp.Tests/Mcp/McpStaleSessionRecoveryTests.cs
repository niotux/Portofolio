using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PiSharp.Core.Mcp;

namespace PiSharp.Tests.Mcp;

/// <summary>
/// Tests for stale-session recovery in StreamableHttpTransport (ADR pi-sharp-codeintel-mcp-stale-session-recovery).
/// Uses a handler that can simulate server restarts (clearing the session store) and the legacy 1.0.5
/// 200 + -32600 behavior.
/// </summary>
public class McpStaleSessionRecoveryTests
{
    [Fact]
    public async Task
        ServerRestartBetweenTwoCalls_SecondCallSucceeds_AfterTransparentReinit()
    {
        var handler = new StaleSessionHandler();
        using var http = new HttpClient(handler);
        await using var transport = new StreamableHttpTransport("http://mcp.test/mcp", http: http);

        // First call: initialize.
        await transport.SendRequestAsync("initialize", new JsonObject
        {
            ["protocolVersion"] = McpClient.ProtocolVersion,
            ["clientInfo"] = new JsonObject { ["name"] = "test", ["version"] = "0" },
            ["capabilities"] = new JsonObject(),
        }, CancellationToken.None);
        Assert.NotNull(transport.SessionId);
        Assert.Equal(1, handler.InitializeCount);

        // Simulate a server restart: the session is lost.
        handler.ResetSessions();

        // Second call: tools/list. The handler will 404 the first attempt (stale session),
        // then the transport re-initializes and replays; the second attempt should succeed.
        var result = await transport.SendRequestAsync("tools/list", null, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal(2, handler.InitializeCount); // initial + re-init
    }

    [Fact]
    public async Task
        ServerRestart_ReportsTheCachedListsChanged_SoANewReleasesSchemaIsRefetched()
    {
        var handler = new StaleSessionHandler();
        using var http = new HttpClient(handler);
        await using var transport = new StreamableHttpTransport("http://mcp.test/mcp", http: http);
        var raised = new List<string>();
        transport.Notification += (method, _) => raised.Add(method);

        await transport.SendRequestAsync("initialize", new JsonObject
        {
            ["protocolVersion"] = McpClient.ProtocolVersion,
            ["clientInfo"] = new JsonObject { ["name"] = "test", ["version"] = "0" },
            ["capabilities"] = new JsonObject(),
        }, CancellationToken.None);
        await transport.SendRequestAsync("tools/list", null, CancellationToken.None);
        Assert.Empty(raised);

        handler.ResetSessions();
        await transport.SendRequestAsync("tools/call", null, CancellationToken.None);

        Assert.Equal(
            ["notifications/tools/list_changed", "notifications/prompts/list_changed", "notifications/resources/list_changed"],
            raised);
    }

    [Fact]
    public async Task
        Transient500_DoesNotTriggerReinit()
    {
        var handler = new StaleSessionHandler();
        using var http = new HttpClient(handler);
        await using var transport = new StreamableHttpTransport("http://mcp.test/mcp", http: http);

        await transport.SendRequestAsync("initialize", new JsonObject
        {
            ["protocolVersion"] = McpClient.ProtocolVersion,
            ["clientInfo"] = new JsonObject { ["name"] = "test", ["version"] = "0" },
            ["capabilities"] = new JsonObject(),
        }, CancellationToken.None);
        Assert.Equal(1, handler.InitializeCount);

        // Inject a transient 500 on the next call.
        handler.FailNextWith500 = true;
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            transport.SendRequestAsync("tools/list", null, CancellationToken.None));

        // No re-init happened; session is still intact.
        Assert.NotNull(transport.SessionId);
        Assert.Equal(1, handler.InitializeCount);
    }

    [Fact]
    public async Task
        LegacyMinus32600MissingSession_TriggersReinit()
    {
        var handler = new StaleSessionHandler { UseLegacyMinus32600 = true };
        using var http = new HttpClient(handler);
        await using var transport = new StreamableHttpTransport("http://mcp.test/mcp", http: http);

        await transport.SendRequestAsync("initialize", new JsonObject
        {
            ["protocolVersion"] = McpClient.ProtocolVersion,
            ["clientInfo"] = new JsonObject { ["name"] = "test", ["version"] = "0" },
            ["capabilities"] = new JsonObject(),
        }, CancellationToken.None);
        Assert.Equal(1, handler.InitializeCount);

        // Legacy server: 200 + -32600 "unknown or missing session" for stale session.
        handler.ResetSessions();

        var result = await transport.SendRequestAsync("tools/list", null, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal(2, handler.InitializeCount); // initial + re-init
    }

    [Fact]
    public async Task
        GenericMinus32600_DoesNotTriggerReinit()
    {
        var handler = new StaleSessionHandler { UseLegacyMinus32600 = false };
        using var http = new HttpClient(handler);
        await using var transport = new StreamableHttpTransport("http://mcp.test/mcp", http: http);

        await transport.SendRequestAsync("initialize", new JsonObject
        {
            ["protocolVersion"] = McpClient.ProtocolVersion,
            ["clientInfo"] = new JsonObject { ["name"] = "test", ["version"] = "0" },
            ["capabilities"] = new JsonObject(),
        }, CancellationToken.None);
        Assert.Equal(1, handler.InitializeCount);

        // Inject a generic -32600 (not the missing-session marker).
        handler.FailNextWithGenericMinus32600 = true;
        var ex = await Assert.ThrowsAsync<JsonRpcException>(() =>
            transport.SendRequestAsync("tools/list", null, CancellationToken.None));
        Assert.Equal(-32600, ex.Code);
        Assert.DoesNotContain("unknown or missing session", ex.Message, StringComparison.Ordinal);

        // No re-init happened.
        Assert.Equal(1, handler.InitializeCount);
    }
}

/// <summary>
/// A fake MCP server that can simulate restarts (404 or legacy -32600 for stale sessions)
/// and transient 500s.
/// </summary>
internal sealed class StaleSessionHandler : HttpMessageHandler
{
    private string? _currentSessionId;
    private int _sessionCounter;
    public int InitializeCount { get; private set; }
    public bool UseLegacyMinus32600 { get; set; }
    public bool FailNextWith500 { get; set; }
    public bool FailNextWithGenericMinus32600 { get; set; }

    public void ResetSessions() => _currentSessionId = null;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (FailNextWith500)
        {
            FailNextWith500 = false;
            return new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("transient failure"),
            };
        }

        if (FailNextWithGenericMinus32600)
        {
            FailNextWithGenericMinus32600 = false;
            var rawBody = request.Content is null ? "{}" : await request.Content.ReadAsStringAsync(cancellationToken);
            var rawId = JsonNode.Parse(rawBody)?["id"]?.DeepClone();
            var errFrame = new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["error"] = new JsonObject { ["code"] = -32600, ["message"] = "not a JSON-RPC object" },
            };
            if (rawId is not null) errFrame["id"] = rawId;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(errFrame.ToJsonString(), Encoding.UTF8, "application/json"),
            };
        }

        string? sessionId = null;
        if (request.Headers.TryGetValues("Mcp-Session-Id", out var ids))
            sessionId = ids.FirstOrDefault();

        var body = request.Content is null ? "{}" : await request.Content.ReadAsStringAsync(cancellationToken);
        var obj = JsonNode.Parse(body)!.AsObject();
        var method = obj["method"]?.GetValue<string>();
        var id = obj["id"]?.DeepClone();

        // Handle initialize (always succeeds, mints a new session).
        if (method == "initialize")
        {
            InitializeCount++;
            _currentSessionId = $"session-{++_sessionCounter}";
            var result = new JsonObject
            {
                ["protocolVersion"] = McpClient.ProtocolVersion,
                ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                ["serverInfo"] = new JsonObject { ["name"] = "stale-test", ["version"] = "1" },
            };
            var frame = new JsonObject { ["jsonrpc"] = "2.0", ["result"] = result };
            if (id is not null) frame["id"] = id;
            var resp = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(frame.ToJsonString(), Encoding.UTF8, "application/json"),
            };
            resp.Headers.TryAddWithoutValidation("Mcp-Session-Id", _currentSessionId);
            return resp;
        }

        // Handle notifications/initialized (no response needed, but confirm session is valid).
        if (method == "notifications/initialized")
        {
            if (sessionId is not null && sessionId != _currentSessionId)
                return BuildStaleResponse(id);
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        }

        // Any other method: check session.
        if (sessionId is not null && sessionId != _currentSessionId)
            return BuildStaleResponse(id);

        // Valid session: return a simple result.
        var toolResult = new JsonObject
        {
            ["tools"] = new JsonArray(new JsonObject
            {
                ["name"] = "echo",
                ["description"] = "echo",
                ["inputSchema"] = new JsonObject { ["type"] = "object" },
            }),
        };
        var toolFrame = new JsonObject { ["jsonrpc"] = "2.0", ["result"] = toolResult };
        if (id is not null) toolFrame["id"] = id;
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(toolFrame.ToJsonString(), Encoding.UTF8, "application/json"),
        };
    }

    private HttpResponseMessage BuildStaleResponse(JsonNode? id)
    {
        if (UseLegacyMinus32600)
        {
            var frame = new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["error"] = new JsonObject { ["code"] = -32600, ["message"] = "unknown or missing session" },
            };
            if (id is not null) frame["id"] = id;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(frame.ToJsonString(), Encoding.UTF8, "application/json"),
            };
        }
        else
        {
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }
}
