using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using PiSharp.Core.Agent;

namespace PiSharp.Core.Factory;

/// <summary>
/// Sends the agent's <c>chat_manage op=status</c> only when its state changes (digital-scribe, 2026-10-20: "status updates
/// only on a real state change"). In the 24 h before, 15 seats made 728 status calls, one every 13 turns: progress lines
/// ("working on X, step Y") where the rule asks for the state (chat-routing: "Status is not a message"). The state is the
/// status's first word (working, idle, blocked, waiting...) and the work items it names; the same state again within
/// <see cref="Refresh"/> is answered here without a call, saying where progress belongs. Clearing the status, any other
/// chat_manage op and a changed state go through.
/// </summary>
public sealed partial class StatusGateTool(IAgentTool inner, TimeProvider? time = null) : IAgentTool
{
    /// <summary>The same state is sent again after this long, so a long task's line stays fresh.</summary>
    public static readonly TimeSpan Refresh = TimeSpan.FromMinutes(60);

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private string? _lastKey;
    private DateTimeOffset _lastAt;

    public string Name => inner.Name;
    public string Label => inner.Label;
    public string Description => inner.Description;
    public JsonObject Parameters => inner.Parameters;
    public ToolExecutionMode? ExecutionMode => inner.ExecutionMode;
    public TimeSpan? MaxDuration => inner.MaxDuration;
    public JsonNode? PrepareArguments(JsonNode? arguments) => inner.PrepareArguments(arguments);

    public async Task<AgentToolResult> ExecuteAsync(string toolCallId, JsonObject arguments,
        Action<AgentToolResult>? onUpdate, CancellationToken cancellationToken)
    {
        if (arguments["op"] is not JsonValue op || !op.TryGetValue<string>(out var name) || name != "status")
            return await inner.ExecuteAsync(toolCallId, arguments, onUpdate, cancellationToken).ConfigureAwait(false);
        var text = arguments["status"] is JsonValue sv && sv.TryGetValue<string>(out var s) ? s : null;
        var key = StateKey(text);
        var now = _time.GetUtcNow();
        if (key is not null && key == _lastKey && now - _lastAt < Refresh)
            return AgentToolResult.FromText("Status not sent: it names the same state and work items as the line already set. "
                + "Set it again only when your state (working, idle, blocked, waiting) or your item changes; progress belongs in the item's notes.");
        var result = await inner.ExecuteAsync(toolCallId, arguments, onUpdate, cancellationToken).ConfigureAwait(false);
        if (!result.IsError) (_lastKey, _lastAt) = (key, now);
        return result;
    }

    /// <summary>The state a status line says: its first word and the work items it names, or null for a cleared status.</summary>
    public static string? StateKey(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return null;
        var lower = status.Trim().ToLowerInvariant();
        var word = FirstWord().Match(lower).Value;
        var items = Item().Matches(lower).Select(m => m.Value).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
        return word + "|" + string.Join(",", items);
    }

    [GeneratedRegex("^[a-z]+")] private static partial Regex FirstWord();
    /// <summary>A KB slug: wi-N, or three or more hyphenated parts (hc2000-disk-library-scan-homes).</summary>
    [GeneratedRegex(@"\b(?:wi-\d+|[a-z][a-z0-9]*(?:-[a-z0-9]+){2,})\b")] private static partial Regex Item();
}
