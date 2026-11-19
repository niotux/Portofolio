// Ported from pi: packages/ai/src/types.ts (Usage, StopReason) @ 71dca871b
// Cost fields dropped: local models have no per-token price.
using System.Text.Json.Serialization;

namespace PiSharp.Core.Messages;

/// <summary>Token accounting for one model response or one tool execution.</summary>
/// <param name="Reasoning">Reasoning tokens, a subset of Output. Null when the server does not report them.</param>
public sealed record Usage(
    int Input = 0,
    int Output = 0,
    int CacheRead = 0,
    int CacheWrite = 0,
    int TotalTokens = 0,
    int? Reasoning = null)
{
    public static readonly Usage Zero = new();

    public Usage Add(Usage other) => new(
        Input + other.Input,
        Output + other.Output,
        CacheRead + other.CacheRead,
        CacheWrite + other.CacheWrite,
        TotalTokens + other.TotalTokens,
        Reasoning is null && other.Reasoning is null ? null : (Reasoning ?? 0) + (other.Reasoning ?? 0));
}

/// <summary>Why an assistant message ended. pi's "deferred" is not needed for chat completions.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<StopReason>))]
public enum StopReason
{
    [JsonStringEnumMemberName("pending")] Pending,
    [JsonStringEnumMemberName("stop")] Stop,
    [JsonStringEnumMemberName("length")] Length,
    [JsonStringEnumMemberName("toolUse")] ToolUse,
    [JsonStringEnumMemberName("error")] Error,
    [JsonStringEnumMemberName("aborted")] Aborted,
}
