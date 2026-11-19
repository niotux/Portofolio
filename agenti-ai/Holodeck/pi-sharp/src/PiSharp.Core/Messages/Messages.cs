// Ported from pi: packages/ai/src/types.ts (UserMessage, AssistantMessage, ToolResultMessage) @ 71dca871b
// and packages/agent/src/types.ts (AgentMessage). Custom app messages are subclasses of Message
// that ConvertToLlm must map to one of the three LLM roles or drop.
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace PiSharp.Core.Messages;

/// <summary>Base of every transcript entry. Discriminated by <c>role</c> when serialized.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "role")]
[JsonDerivedType(typeof(UserMessage), "user")]
[JsonDerivedType(typeof(AssistantMessage), "assistant")]
[JsonDerivedType(typeof(ToolResultMessage), "toolResult")]
[JsonDerivedType(typeof(CompactionSummaryMessage), "compactionSummary")]
public abstract record Message
{
    /// <summary>Creation time.</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>True for the three roles the model understands.</summary>
    [JsonIgnore]
    public bool IsLlmMessage => this is UserMessage or AssistantMessage or ToolResultMessage;
}

/// <summary>A user turn: text and optionally images.</summary>
public sealed record UserMessage(IReadOnlyList<ContentPart> Content) : Message
{
    /// <summary>Explicit task boundary for API callers; false marks harness reminders. Null supports legacy transcripts.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? TaskStart { get; init; }
    [JsonConstructor]
    public UserMessage(IReadOnlyList<ContentPart> content, DateTimeOffset timestamp) : this(content)
    {
        Timestamp = timestamp;
    }

    public UserMessage(string text) : this([new TextContent(text)]) { }

    /// <summary>Concatenated text parts, images excluded.</summary>
    [JsonIgnore]
    public string Text => string.Concat(Content.OfType<TextContent>().Select(t => t.Text));
}

/// <summary>A model turn: text, thinking and tool calls, plus stop reason and usage.</summary>
public sealed record AssistantMessage : Message
{
    public required IReadOnlyList<ContentPart> Content { get; init; }
    /// <summary>Requested model id.</summary>
    public required string Model { get; init; }
    /// <summary>Provider that produced replay signatures; null for legacy chat-completions messages.</summary>
    public string? Provider { get; init; }
    /// <summary>Concrete model id reported by the server when it differs from <see cref="Model"/>.</summary>
    public string? ResponseModel { get; init; }
    public string? ResponseId { get; init; }
    public Usage Usage { get; init; } = Usage.Zero;
    public StopReason StopReason { get; init; } = StopReason.Pending;
    public string? ErrorMessage { get; init; }
    /// <summary>
    /// Machine-readable error code from the provider response (<c>error.code</c>/<c>error.type</c>,
    /// e.g. <c>context_length_exceeded</c>). Set only on error turns. Recognition of a context
    /// overflow reads this before falling back to free-text patterns.
    /// </summary>
    public string? ErrorCode { get; init; }
    /// <summary>HTTP response status for a failed provider request; null when no response was received.</summary>
    public int? ProviderHttpStatus { get; init; }
    /// <summary>True only when the model provider request failed, not for a local harness rejection.</summary>
    public bool ProviderRequestFailed { get; init; }
    /// <summary>Broker classification carried as a response header, when present.</summary>
    public string? RateLimitReason { get; init; }
    /// <summary>Server-native finish reason, kept for debugging.</summary>
    public string? RawStopReason { get; init; }

    [JsonIgnore]
    public IEnumerable<ToolCallContent> ToolCalls => Content.OfType<ToolCallContent>();

    [JsonIgnore]
    public string Text => string.Concat(Content.OfType<TextContent>().Select(t => t.Text));
}

/// <summary>Result of one tool call, fed back to the model.</summary>
public sealed record ToolResultMessage : Message
{
    public required string ToolCallId { get; init; }
    public required string ToolName { get; init; }
    public required IReadOnlyList<ContentPart> Content { get; init; }
    /// <summary>Structured payload for logs and rendering. Never sent to the model.</summary>
    public JsonNode? Details { get; init; }
    /// <summary>Usage of the tool execution itself (e.g. a sub-agent). Not part of context accounting.</summary>
    public Usage? Usage { get; init; }
    /// <summary>Tools that became available after this result.</summary>
    public IReadOnlyList<string>? AddedToolNames { get; init; }
    public bool IsError { get; init; }
}

/// <summary>
/// Synthesized from a session <c>compaction</c> entry. ConvertToLlm wraps the summary in
/// pi's checkpoint prefix/suffix so the model treats it as prior context, not a user turn.
/// </summary>
public sealed record CompactionSummaryMessage(string Summary, int TokensBefore) : Message;

/// <summary>Concatenated text blocks; images, thinking and tool calls are ignored.</summary>
public static class ContentText
{
    public static string Of(IReadOnlyList<ContentPart>? content)
    {
        if (content is null || content.Count == 0) return "";
        return string.Concat(content.OfType<TextContent>().Select(t => t.Text));
    }
}
