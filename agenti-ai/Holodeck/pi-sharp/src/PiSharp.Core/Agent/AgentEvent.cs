// Ported from pi: packages/agent/src/types.ts (AgentEvent) @ 71dca871b
using System.Text.Json.Nodes;
using PiSharp.Core.Messages;

namespace PiSharp.Core.Agent;

/// <summary>Events emitted by <see cref="AgentLoop"/> for rendering and persistence.</summary>
public abstract record AgentEvent;

public sealed record AgentStart : AgentEvent;
/// <summary>Last event of a run. <see cref="Messages"/> are the messages this run added.</summary>
public sealed record AgentEnd(IReadOnlyList<Message> Messages) : AgentEvent
{
    public string Outcome => Messages.OfType<AssistantMessage>().LastOrDefault() switch
    {
        { ErrorCode: "budget_exhausted" } => "budget_exhausted",
        { StopReason: StopReason.Aborted } => "cancelled",
        { StopReason: StopReason.Error } => "failed",
        { StopReason: StopReason.Length } => "output_limit",
        { } message when message.ToolCalls.Any() => "stalled",
        _ => "model_completed",
    };
}

/// <summary>A turn is one assistant response plus its tool calls and results.</summary>
public sealed record TurnStart : AgentEvent;
/// <param name="Final">Same as <see cref="TurnContext.Final"/>: the loop will not call the model again on its own.</param>
public sealed record TurnEnd(AssistantMessage Message, IReadOnlyList<ToolResultMessage> ToolResults, bool Final = false) : AgentEvent;

/// <summary>Emitted for user, assistant and tool-result messages.</summary>
public sealed record MessageStart(Message Message) : AgentEvent;
/// <summary>Only for assistant messages while streaming.</summary>
public sealed record MessageUpdate(AssistantMessage Message, AssistantMessageEvent AssistantEvent) : AgentEvent;
public sealed record MessageEnd(Message Message) : AgentEvent;

public sealed record ToolExecutionStart(string ToolCallId, string ToolName, JsonObject Arguments) : AgentEvent;
public sealed record ToolExecutionUpdate(string ToolCallId, string ToolName, JsonObject Arguments, AgentToolResult PartialResult) : AgentEvent;
public sealed record ToolExecutionDeadline(string ToolCallId, string ToolName, TimeSpan Deadline) : AgentEvent;
public sealed record ToolExecutionEnd(string ToolCallId, string ToolName, AgentToolResult Result, bool IsError) : AgentEvent;
