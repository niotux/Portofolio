// Ported from pi: packages/ai/src/types.ts (AssistantMessageEvent) @ 71dca871b
namespace PiSharp.Core.Messages;

/// <summary>
/// Streaming events for one assistant response. A stream must end with exactly one
/// <see cref="AssistantDone"/> or <see cref="AssistantError"/>. Every other event carries
/// the partial message accumulated so far.
/// </summary>
public abstract record AssistantMessageEvent;

/// <summary>Events that carry the accumulating partial message.</summary>
public abstract record AssistantPartialEvent(AssistantMessage Partial) : AssistantMessageEvent;

public sealed record AssistantStart(AssistantMessage Partial) : AssistantPartialEvent(Partial);

public sealed record TextStart(int ContentIndex, AssistantMessage Partial) : AssistantPartialEvent(Partial);
public sealed record TextDelta(int ContentIndex, string Delta, AssistantMessage Partial) : AssistantPartialEvent(Partial);
public sealed record TextEnd(int ContentIndex, string Content, AssistantMessage Partial) : AssistantPartialEvent(Partial);

public sealed record ThinkingStart(int ContentIndex, AssistantMessage Partial) : AssistantPartialEvent(Partial);
public sealed record ThinkingDelta(int ContentIndex, string Delta, AssistantMessage Partial) : AssistantPartialEvent(Partial);
public sealed record ThinkingEnd(int ContentIndex, string Content, AssistantMessage Partial) : AssistantPartialEvent(Partial);

public sealed record ToolCallStart(int ContentIndex, AssistantMessage Partial) : AssistantPartialEvent(Partial);
/// <summary>A fragment of the tool-call arguments JSON as sent by the server.</summary>
public sealed record ToolCallDelta(int ContentIndex, string Delta, AssistantMessage Partial) : AssistantPartialEvent(Partial);
public sealed record ToolCallEnd(int ContentIndex, ToolCallContent ToolCall, AssistantMessage Partial) : AssistantPartialEvent(Partial);

/// <summary>Terminal event. <see cref="Reason"/> is Stop, Length or ToolUse.</summary>
/// <summary>
/// The assistant drops the partial message accumulated so far and re-issues the same request
/// (pi-sharp-hh-7: an in-turn model stream retry after a transport error mid-content). Consumers
/// that mirror or trace the message state must reset to <see cref="Partial"/> — the content the
/// message held before this event is gone, not a prefix of what follows.
/// </summary>
public sealed record AssistantDiscardPartial(AssistantMessage Partial) : AssistantPartialEvent(Partial);

public sealed record AssistantDone(StopReason Reason, AssistantMessage Message) : AssistantMessageEvent;

/// <summary>Terminal event. <see cref="Reason"/> is Error or Aborted; <see cref="Error"/> carries ErrorMessage.</summary>
public sealed record AssistantError(StopReason Reason, AssistantMessage Error) : AssistantMessageEvent;
