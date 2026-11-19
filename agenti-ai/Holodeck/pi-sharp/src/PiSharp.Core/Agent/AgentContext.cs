// Ported from pi: packages/agent/src/types.ts (AgentContext, StreamFn) and
// packages/ai/src/types.ts (Context, SimpleStreamOptions) @ 71dca871b
using PiSharp.Core.Messages;
using PiSharp.Core.Models;

namespace PiSharp.Core.Agent;

/// <summary>Mutable runtime state handed to the loop. The loop appends to <see cref="Messages"/>.</summary>
public sealed class AgentContext
{
    public string SystemPrompt { get; set; } = "";
    public List<Message> Messages { get; set; } = [];
    public List<IAgentTool> Tools { get; set; } = [];
    public ToolReadCache ReadCache { get; } = new();
    public PendingToolCleanup PendingCleanup { get; internal set; } = new();

    public IAgentTool? FindTool(string name) => Tools.Find(t => t.Name == name);
}

/// <summary>What one model request sees, after ConvertToLlm.</summary>
public sealed record LlmContext(string SystemPrompt, IReadOnlyList<Message> Messages, IReadOnlyList<ITool> Tools);

/// <summary>Per-request options passed to the stream function.</summary>
public sealed record StreamOptions
{
    public ThinkingLevel? Reasoning { get; init; }
    public int? MaxTokens { get; init; }
    public double? Temperature { get; init; }
    public string? ApiKey { get; init; }
    /// <summary>A stable key for one seat's conversation, so the provider routes its requests to the same prompt
    /// cache. Subscription transports send it; others ignore it.</summary>
    public string? CacheKey { get; init; }
    /// <summary>
    /// <c>auto</c>, <c>none</c>, <c>required</c>, or a tool name (sent as a named function choice).
    /// A named tool plus <see cref="ModelCompat.SupportsJsonSchemaResponseFormat"/> also attaches
    /// llama.cpp <c>response_format.json_schema</c>.
    /// </summary>
    public string? ToolChoice { get; init; }
    /// <summary>Tools a Responses transport sends as free-form (Codex's custom tools: the model writes the input as plain
    /// text under a grammar, not as JSON). Only <c>apply_patch</c> has a grammar today; others ignore it.</summary>
    public IReadOnlySet<string>? FreeformTools { get; init; }
    /// <summary>Ask the provider to compact past this many tokens (Responses context_management); null = never.</summary>
    public int? ServerCompactThreshold { get; init; }
}

/// <summary>
/// Produces one assistant response as a stream of events.
/// Contract (same as pi's StreamFn): must not throw for request, model or runtime failures.
/// Encode them as an <see cref="AssistantError"/> whose message has StopReason Error or Aborted.
/// The loop still guards against a throwing implementation and converts the exception into an error turn.
/// </summary>
public delegate IAsyncEnumerable<AssistantMessageEvent> StreamFunction(
    ModelConfig model,
    LlmContext context,
    StreamOptions options,
    CancellationToken cancellationToken);
