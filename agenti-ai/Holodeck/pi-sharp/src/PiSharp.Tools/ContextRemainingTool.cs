// pi-sharp-hh-6: read-only view of the logical model-call budget and the estimated context left.
// The context figure is the same estimate the compaction gate measures on the session, so both
// answer from one formula (TokenEstimate over the working messages plus the notebook share).
using System.Text.Json.Nodes;
using PiSharp.Core.Agent;
using PiSharp.Core.Messages;
using PiSharp.Core.Models;
using PiSharp.Core.Notebook;

namespace PiSharp.Tools;

/// <summary>Read-only: what the task budget has left (turns, output tokens, tool calls) and the
/// estimated context tokens the compaction gate would see right now.</summary>
public sealed class ContextRemainingTool : IAgentTool
{
    private readonly Func<(int Turns, int OutputTokens, int ToolCalls)?> _budget;
    private readonly Func<(int MaxTurns, int MaxOutputTokens, int MaxToolCalls)?> _limits;
    private readonly Func<int> _contextTokens;
    private readonly int _contextWindow;

    public ContextRemainingTool(
        Func<(int Turns, int OutputTokens, int ToolCalls)?> budget,
        Func<(int MaxTurns, int MaxOutputTokens, int MaxToolCalls)?> limits,
        Func<int> contextTokens,
        int contextWindow)
    {
        _budget = budget;
        _limits = limits;
        _contextTokens = contextTokens;
        _contextWindow = contextWindow;
    }

    public string Name => "context_remaining";
    public string Label => Name;
    public string Description =>
        "Read-only: the task budget counters (turns, output tokens, tool calls used vs limit) and the estimated context tokens " +
        "left of the model's window, measured with the same estimate the compaction gate uses. Takes no arguments.";
    public JsonObject Parameters => JsonNode.Parse("""{"type":"object","properties":{}}""")!.AsObject();
    public ToolExecutionMode? ExecutionMode => ToolExecutionMode.Sequential;

    public Task<AgentToolResult> ExecuteAsync(string toolCallId, JsonObject arguments, Action<AgentToolResult>? onUpdate, CancellationToken cancellationToken)
    {
        var contextTokens = _contextTokens();
        var limits = _limits();
        var text = limits is { } l
            ? BudgetText(this, l, _budget(), contextTokens)
            : "Task budget not configured. Context: " + contextTokens + " tokens used of a " + _contextWindow + " token window (~" +
              Math.Max(0, _contextWindow - contextTokens) + " left).";
        return Task.FromResult(AgentToolResult.FromText(text));
    }

    private static string BudgetText(ContextRemainingTool tool, (int MaxTurns, int MaxOutputTokens, int MaxToolCalls) limits, (int Turns, int OutputTokens, int ToolCalls)? counters, int contextTokens)
    {
        var (turns, tokens, calls) = counters ?? (0, 0, 0);
        return "Task budget: turns " + turns + "/" + limits.MaxTurns + " (" + Left(turns, limits.MaxTurns) + "% left), output tokens "
            + tokens + "/" + limits.MaxOutputTokens + " (" + Left(tokens, limits.MaxOutputTokens) + "% left), tool calls "
            + calls + "/" + limits.MaxToolCalls + " (" + Left(calls, limits.MaxToolCalls) + "% left). "
            + "Context: " + contextTokens + " tokens used of a " + tool._contextWindow + " token window (~" + Math.Max(0, tool._contextWindow - contextTokens) + " left).";
    }

    private static int Left(int used, int max) => max <= 0 ? 0 : (int)Math.Clamp((max - used) * 100.0 / max, 0, 100);
}
