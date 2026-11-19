// Wrap an MCP tools/call as an IAgentTool. Names are prefixed with the server alias.
// Result mapping: MCP text/image/resource → ContentPart (docs/04, docs/01 Images).
using System.Text.Json.Nodes;
using PiSharp.Core.Agent;
using PiSharp.Core.Messages;

namespace PiSharp.Core.Mcp;

/// <summary>An MCP tool exposed to the agent loop. Name is <c>{alias}__{tool}</c>.</summary>
public interface IRemoteMcpAgentTool : IAgentTool
{
    string RemoteName { get; }
}

public sealed class McpAgentTool : IRemoteMcpAgentTool
{
    private readonly McpClient _client;
    private readonly string _remoteName;

    public McpAgentTool(McpClient client, McpToolInfo info)
    {
        _client = client;
        _remoteName = info.Name;
        Name = Prefix(client.Alias, info.Name);
        Label = Name;
        Description = string.IsNullOrEmpty(info.Description)
            ? $"MCP tool {info.Name} from {client.Alias}"
            : $"[{client.Alias}] {info.Description}";
        Parameters = info.InputSchema;
    }

    public string Name { get; }
    public string Label { get; }
    public string Description { get; }
    public JsonObject Parameters { get; }
    public string ServerAlias => _client.Alias;
    public string RemoteName => _remoteName;

    public static string Prefix(string alias, string toolName) => $"{alias}__{toolName}";

    public async Task<AgentToolResult> ExecuteAsync(
        string toolCallId,
        JsonObject arguments,
        Action<AgentToolResult>? onUpdate,
        CancellationToken cancellationToken)
    {
        var result = await _client.CallToolAsync(_remoteName, arguments, cancellationToken).ConfigureAwait(false);
        var parts = MapContent(result.Content, out var rejected, $"{Name} call {toolCallId}");
        if (parts.Count == 0)
            parts.Add(new TextContent(result.IsError ? "(MCP error with empty content)" : "(empty MCP result)"));
        else if (result.IsError && parts[0] is TextContent t && !t.Text.StartsWith("MCP error", StringComparison.Ordinal))
            parts[0] = new TextContent("MCP error: " + t.Text);
        return new AgentToolResult(parts, IsError: result.IsError || rejected);
    }

    public static List<ContentPart> MapContent(IReadOnlyList<McpContentPart> content) => MapContent(content, out _);

    private static List<ContentPart> MapContent(IReadOnlyList<McpContentPart> content, out bool rejected, string reference = "MCP")
    {
        var hadRejected = false;
        var parts = new List<ContentPart>();
        void AddImage(string data, string mime, string source)
        {
            var image = new ImageContent(data, mime);
            if (ImageValidation.Problem(image) is { } problem)
            {
                hadRejected = true;
                parts.Add(new TextContent(ImageValidation.Diagnostic(source, problem)));
            }
            else parts.Add(image);
        }
        foreach (var item in content)
        {
            switch (item)
            {
                case McpTextPart text:
                    parts.Add(new TextContent(text.Text));
                    break;
                case McpImagePart image:
                    AddImage(image.Data, image.MimeType, $"{reference} image part {parts.Count}");
                    break;
                case McpResourcePart resource:
                    if (resource.Resource.Text is not null)
                        parts.Add(new TextContent(resource.Resource.Text));
                    else if (resource.Resource.Blob is not null && IsImage(resource.Resource.MimeType))
                        AddImage(resource.Resource.Blob, resource.Resource.MimeType ?? "image/png", $"{reference} resource part {parts.Count} uri-sha256 {UriFingerprint(resource.Resource.Uri)}");
                    else
                        parts.Add(new TextContent($"[resource {resource.Resource.Uri}]"));
                    break;
                case McpResourceLinkPart link:
                    parts.Add(new TextContent($"[resource {link.Uri}]"));
                    break;
            }
        }
        rejected = hadRejected;
        return parts;
    }

    private static string UriFingerprint(string uri) => Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(uri ?? "")));

    private static bool IsImage(string? mime) =>
        mime is not null && mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
}
