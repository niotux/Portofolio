// Connect every mcp.json server, wrap tools, collect prompts and skill:// resources.
// CLI Phase 5 wires /mcp to FormatStatus + ReconnectAsync.
using System.Text;
using PiSharp.Core.Agent;
using PiSharp.Core.Skills;

namespace PiSharp.Core.Mcp;

/// <summary>One live MCP connection plus the snapshots taken at connect (or last reconnect).</summary>
public sealed class McpConnection
{
    public required string Alias { get; init; }
    public required McpServerConfig Config { get; init; }
    public McpClient? Client { get; set; }
    public string? Error { get; set; }
    public IReadOnlyList<McpToolInfo> Tools { get; set; } = [];
    public IReadOnlyList<McpPromptInfo> Prompts { get; set; } = [];
    public IReadOnlyList<McpResourceInfo> Resources { get; set; } = [];
}

/// <summary>
/// Owns MCP clients for a session. Safe to construct with an empty config (no servers).
/// Implements <see cref="ISkillUriResolver"/> so <c>read</c> can fetch <c>skill://</c> URIs.
/// </summary>
public sealed class McpManager : IAsyncDisposable, ISkillUriResolver
{
    private readonly List<McpConnection> _connections = [];
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private volatile bool _disposed;

    public IReadOnlyList<McpConnection> Connections => _connections;

    /// <summary>Raised after a server list_changed notification has refreshed its snapshot.</summary>
    public event Action<string>? ListsChanged;

    public static async Task<McpManager> ConnectAllAsync(
        McpConfig config,
        CancellationToken cancellationToken = default)
    {
        var manager = new McpManager();
        foreach (var server in config.Servers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await manager.AddAsync(server, cancellationToken).ConfigureAwait(false);
        }
        return manager;
    }

    public async Task AddAsync(McpServerConfig config, CancellationToken cancellationToken = default)
    {
        var conn = new McpConnection { Alias = config.Alias, Config = config };
        _connections.Add(conn);
        await ConnectOneAsync(conn, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Add an already initialized client, useful for hosts that provide a custom transport.</summary>
    public async Task AddClientAsync(
        McpServerConfig config,
        McpClient client,
        CancellationToken cancellationToken = default)
    {
        var conn = new McpConnection { Alias = config.Alias, Config = config };
        _connections.Add(conn);
        await AttachClientAsync(conn, client, cancellationToken).ConfigureAwait(false);
    }

    public async Task ReconnectAsync(string alias, CancellationToken cancellationToken = default)
    {
        var conn = _connections.FirstOrDefault(c => c.Alias == alias)
            ?? throw new InvalidOperationException($"No MCP server named \"{alias}\".");
        if (conn.Client is not null)
            await conn.Client.DisposeAsync().ConfigureAwait(false);
        conn.Client = null;
        conn.Error = null;
        await ConnectOneAsync(conn, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Agent tools from every connected server, names prefixed <c>{alias}__{tool}</c>.</summary>
    public IReadOnlyList<IAgentTool> Tools()
    {
        var list = new List<IAgentTool>();
        foreach (var conn in _connections)
        {
            if (conn.Client is null) continue;
            foreach (var tool in conn.Tools)
                list.Add(new McpAgentTool(conn.Client, tool));
        }
        return list;
    }

    /// <summary>Resources whose URI starts with <c>skill://</c>, for the skills index.</summary>
    public IReadOnlyList<(string Alias, McpResourceInfo Resource)> SkillResources()
    {
        var list = new List<(string, McpResourceInfo)>();
        foreach (var conn in _connections)
        {
            foreach (var r in conn.Resources)
            {
                if (r.Uri.StartsWith("skill://", StringComparison.OrdinalIgnoreCase))
                    list.Add((conn.Alias, r));
            }
        }
        return list;
    }

    public McpClient? Client(string alias) =>
        _connections.FirstOrDefault(c => c.Alias == alias)?.Client;

    public bool CanResolve(string uri) =>
        uri.StartsWith("skill://", StringComparison.OrdinalIgnoreCase)
        && FindOwner(uri) is not null;

    public async Task<string> ReadTextAsync(string uri, CancellationToken cancellationToken = default)
    {
        var owner = FindOwner(uri)
            ?? throw new InvalidOperationException($"No MCP server owns skill URI {uri}.");
        var contents = await owner.ReadResourceAsync(uri, cancellationToken).ConfigureAwait(false);
        var text = contents.FirstOrDefault(c => c.Text is not null)?.Text;
        if (text is not null) return text;
        if (contents.Count > 0 && contents[0].Blob is not null)
        {
            try { return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(contents[0].Blob!)); }
            catch (FormatException) { /* not utf-8 text */ }
        }
        throw new InvalidOperationException($"Resource {uri} had no text contents.");
    }

    /// <summary>Text for the <c>/mcp</c> slash command: alias, transport, server info, tool/prompt/resource counts.</summary>
    public string FormatStatus()
    {
        if (_connections.Count == 0) return "No MCP servers configured.";
        var sb = new StringBuilder();
        foreach (var conn in _connections)
        {
            sb.Append(conn.Alias).Append("  ");
            if (conn.Client is null)
            {
                sb.Append("disconnected");
                if (conn.Error is not null) sb.Append("  ").Append(conn.Error);
                sb.Append('\n');
                continue;
            }
            var kind = conn.Config.Kind == McpTransportKind.Http ? conn.Config.Url : conn.Config.Command;
            sb.Append(conn.Client.ServerInfo.Name).Append(' ').Append(conn.Client.ServerInfo.Version);
            sb.Append("  (").Append(kind).Append(')');
            sb.Append("  tools=").Append(conn.Tools.Count);
            sb.Append(" prompts=").Append(conn.Prompts.Count);
            sb.Append(" resources=").Append(conn.Resources.Count);
            sb.Append('\n');
        }
        return sb.ToString().TrimEnd();
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        foreach (var conn in _connections)
        {
            if (conn.Client is not null)
                await conn.Client.DisposeAsync().ConfigureAwait(false);
            conn.Client = null;
        }
        _refreshLock.Dispose();
    }

    private async Task ConnectOneAsync(McpConnection conn, CancellationToken cancellationToken)
    {
        try
        {
            var client = await McpClient.ConnectAsync(conn.Config, cancellationToken).ConfigureAwait(false);
            await AttachClientAsync(conn, client, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            conn.Error = ex.Message;
            conn.Client = null;
        }
    }

    private async Task AttachClientAsync(
        McpConnection conn,
        McpClient client,
        CancellationToken cancellationToken)
    {
        conn.Client = client;
        client.Notification += (method, _) => OnNotification(conn, method);
        conn.Tools = await client.ListToolsAsync(cancellationToken).ConfigureAwait(false);
        conn.Prompts = await client.ListPromptsAsync(cancellationToken).ConfigureAwait(false);
        conn.Resources = await client.ListResourcesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>How often <see cref="PollToolsAsync"/> re-lists an HTTP server's tools.</summary>
    public static readonly TimeSpan ToolPollInterval = TimeSpan.FromMinutes(1);
    private DateTime _lastToolPoll = DateTime.MinValue;

    /// <summary>
    /// Re-list the tools of every HTTP server, at most once per <see cref="ToolPollInterval"/>, and raise
    /// <see cref="ListsChanged"/> for each whose list differs from the snapshot. Team watch 2026-10-04: the KB serves MCP
    /// stateless, so it hands out no session to expire and never sends list_changed; after kb 1.0.80 added kb_decision
    /// every seat kept its 10-03 tool list, and the product owner could not file the one decision gating the wi-111 chain.
    /// A failed listing keeps the old snapshot. A server that could not be reached at start is connected again here.
    /// </summary>
    public async Task<bool> PollToolsAsync(DateTime now, CancellationToken cancellationToken = default)
    {
        if (now - _lastToolPoll < ToolPollInterval) return false;
        _lastToolPoll = now;
        var changed = new List<string>();
        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var conn in _connections)
            {
                if (_disposed || conn.Config.Kind != McpTransportKind.Http) continue;
                if (conn.Client is not { } client)
                {
                    // A server that was down when the seat started is tried again here: demo-02 and demo-03 started while
                    // strata's relay was restarting (2026-10-28 00:49Z), the KB forward refused, and they ran for hours with
                    // only chat and session until an operator restarted them.
                    if (conn.Error is null) continue;
                    await ConnectOneAsync(conn, cancellationToken).ConfigureAwait(false);
                    if (conn.Client is not null) { conn.Error = null; changed.Add(conn.Alias); }
                    continue;
                }
                try
                {
                    var tools = await client.ListToolsAsync(cancellationToken).ConfigureAwait(false);
                    if (SameTools(conn.Tools, tools)) continue;
                    conn.Tools = tools;
                    changed.Add(conn.Alias);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Unreachable for now: the next poll tries again, and the tools it had still work.
                }
            }
        }
        finally
        {
            try { _refreshLock.Release(); } catch (ObjectDisposedException) { }
        }
        foreach (var alias in changed) ListsChanged?.Invoke(alias);
        return changed.Count > 0;
    }

    private static bool SameTools(IReadOnlyList<McpToolInfo> a, IReadOnlyList<McpToolInfo> b) =>
        a.Count == b.Count && a.Zip(b).All(p => p.First.Name == p.Second.Name && p.First.Description == p.Second.Description
            && p.First.InputSchema.ToJsonString() == p.Second.InputSchema.ToJsonString());

    private void OnNotification(McpConnection connection, string method)
    {
        if (method is not ("notifications/tools/list_changed"
            or "notifications/prompts/list_changed"
            or "notifications/resources/list_changed"))
            return;

        _ = RefreshAfterNotificationAsync(connection, method);
    }

    private async Task RefreshAfterNotificationAsync(McpConnection connection, string method)
    {
        var entered = false;
        try
        {
            await _refreshLock.WaitAsync().ConfigureAwait(false);
            entered = true;
            if (_disposed || connection.Client is not { } client) return;

            switch (method)
            {
                case "notifications/tools/list_changed":
                    connection.Tools = await client.ListToolsAsync().ConfigureAwait(false);
                    break;
                case "notifications/prompts/list_changed":
                    connection.Prompts = await client.ListPromptsAsync().ConfigureAwait(false);
                    break;
                case "notifications/resources/list_changed":
                    connection.Resources = await client.ListResourcesAsync().ConfigureAwait(false);
                    break;
            }

            connection.Error = null;
            ListsChanged?.Invoke(connection.Alias);
        }
        catch (Exception ex)
        {
            connection.Error = $"list_changed refresh failed: {ex.Message}";
        }
        finally
        {
            if (entered)
            {
                try { _refreshLock.Release(); } catch (ObjectDisposedException) { }
            }
        }
    }

    private McpClient? FindOwner(string uri)
    {
        if (Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.Scheme == "skill")
        {
            var alias = parsed.Host;
            var match = _connections.FirstOrDefault(c =>
                c.Client is not null && c.Alias.Equals(alias, StringComparison.OrdinalIgnoreCase));
            if (match?.Client is not null) return match.Client;
        }

        foreach (var conn in _connections)
        {
            if (conn.Client is not null
                && conn.Resources.Any(r => string.Equals(r.Uri, uri, StringComparison.Ordinal)))
                return conn.Client;
        }
        return null;
    }
}
