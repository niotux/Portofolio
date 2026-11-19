using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PiSharp.CodeIntel;
using PiSharp.CodeIntel.Auth;
using PiSharp.CodeIntel.Mcp;

namespace PiSharp.Tests.CodeIntel;

public sealed class McpSessionTests
{
    private static readonly CodeIntelActor Actor = new("seat-test", "Test seat", [], null);

    [Fact]
    public async Task Initialize_ReturnsUniqueSessionHeader_AndSessionAcceptsToolsList()
    {
        await using var endpoint = new McpEndpoint(
            null!, null!, Options.Create(new CodeIntelOptions()), NullLogger<McpEndpoint>.Instance);
        const string initialize = """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""";

        var first = await endpoint.HandlePostAsync(null, Actor, initialize, CancellationToken.None);
        var second = await endpoint.HandlePostAsync(null, Actor, initialize, CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(first.SessionId));
        Assert.False(string.IsNullOrWhiteSpace(second.SessionId));
        Assert.NotEqual(first.SessionId, second.SessionId);

        var http = new DefaultHttpContext();
        first.ApplyHeaders(http.Response);
        Assert.Equal(first.SessionId, http.Response.Headers["Mcp-Session-Id"].ToString());

        const string toolsList = """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""";
        var listed = await endpoint.HandlePostAsync(first.SessionId, Actor, toolsList, CancellationToken.None);
        Assert.DoesNotContain("\"error\"", listed.Body, StringComparison.Ordinal);
        Assert.Contains("code_definition", listed.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownSession_Returns404_ForNonInitialize()
    {
        await using var endpoint = new McpEndpoint(
            null!, null!, Options.Create(new CodeIntelOptions()), NullLogger<McpEndpoint>.Instance);
        const string toolsList = """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""";

        var resp = await endpoint.HandlePostAsync("stale-session-id", Actor, toolsList, CancellationToken.None);
        Assert.Equal(404, resp.Status);
        Assert.Null(resp.SessionId);
    }

    [Fact]
    public async Task UnknownSession_ReturnsError_Not404_ForInitialize()
    {
        // initialize is always accepted (it's how sessions are created); no 404 for unknown session.
        await using var endpoint = new McpEndpoint(
            null!, null!, Options.Create(new CodeIntelOptions()), NullLogger<McpEndpoint>.Instance);
        const string initialize = """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""";

        var resp = await endpoint.HandlePostAsync("stale-session-id", Actor, initialize, CancellationToken.None);
        Assert.Equal(200, resp.Status);
        Assert.False(string.IsNullOrWhiteSpace(resp.SessionId));
    }

    [Fact]
    public async Task NoSession_ReturnsMinus32600_ForNonInitialize()
    {
        // A request with no session id and a method other than initialize is a 200 + -32600 error,
        // not a 404 (which means a session we had lost, per the ADR).
        await using var endpoint = new McpEndpoint(
            null!, null!, Options.Create(new CodeIntelOptions()), NullLogger<McpEndpoint>.Instance);
        const string toolsList = """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""";

        var resp = await endpoint.HandlePostAsync(null, Actor, toolsList, CancellationToken.None);
        Assert.Equal(200, resp.Status);
        Assert.Contains("-32600", resp.Body, StringComparison.Ordinal);
        Assert.Contains("unknown or missing session", resp.Body, StringComparison.Ordinal);
    }
}
