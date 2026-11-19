using System.Text.Json.Nodes;
using PiSharp.Core.Messages;

namespace PiSharp.Core.Compaction;

/// <summary>
/// The provider's own compaction (OpenAI Responses <c>context_management</c>, plan-pi-sharp-codex item 10; setting
/// serverCompaction, off by default). The provider compacts inside a turn and returns an opaque compaction item; pi-sharp
/// keeps it like a reasoning item (a <see cref="ThinkingContent"/> whose signature is the item), sends it back to the
/// same model, drops what came before it from requests, and counts its context from it.
/// </summary>
public static class ServerCompaction
{
    /// <summary>The output item types that carry a server compaction.</summary>
    public static bool IsItemType(string? type) => type is "compaction" or "context_compaction";

    /// <summary>True when the part is a stored server compaction item.</summary>
    public static bool IsItem(ContentPart part)
    {
        if (part is not ThinkingContent { Signature: { Length: > 0 } signature } || !signature.Contains("compaction", StringComparison.Ordinal))
            return false;
        try { return IsItemType(JsonNode.Parse(signature)?["type"]?.GetValue<string>()); }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException) { return false; }
    }

    /// <summary>The index of the last assistant message holding a server compaction item, or -1.</summary>
    public static int LatestIndex(IReadOnlyList<Message> messages)
    {
        for (var i = messages.Count - 1; i >= 0; i--)
            if (messages[i] is AssistantMessage assistant && assistant.Content.Any(IsItem)) return i;
        return -1;
    }

    /// <summary>The messages a request carries: from the latest server compaction on (that item holds what came before).</summary>
    public static IReadOnlyList<Message> FromLatest(IReadOnlyList<Message> messages) =>
        LatestIndex(messages) is var at and >= 0 ? [.. messages.Skip(at)] : messages;

    /// <summary>Opaque state may replace the prefix only for the model/provider that produced it.</summary>
    public static IReadOnlyList<Message> FromLatest(IReadOnlyList<Message> messages, string model, string provider)
    {
        // Do not select an older boundary behind incompatible opaque state: replay the full transcript instead.
        var at = LatestIndex(messages);
        return at >= 0 && messages[at] is AssistantMessage a && a.Model == model && a.Provider == provider
            ? [.. messages.Skip(at)] : messages;
    }
}
