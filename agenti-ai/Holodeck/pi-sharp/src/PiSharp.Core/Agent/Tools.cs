// Ported from pi: packages/ai/src/types.ts (Tool) and packages/agent/src/types.ts
// (AgentTool, AgentToolResult, ToolExecutionMode) @ 71dca871b
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using PiSharp.Core.Messages;

namespace PiSharp.Core.Agent;

/// <summary>What the model sees: name, description and a JSON Schema for the arguments.</summary>
public interface ITool
{
    string Name { get; }
    string Description { get; }
    /// <summary>JSON Schema object describing the arguments.</summary>
    JsonObject Parameters { get; }
    /// <summary>Compact description for small-context models. Defaults to <see cref="Description"/>.</summary>
    string ShortDescription => Description;
}

[JsonConverter(typeof(JsonStringEnumConverter<ToolExecutionMode>))]
public enum ToolExecutionMode
{
    /// <summary>Each call is prepared, executed and finalized before the next starts.</summary>
    [JsonStringEnumMemberName("sequential")] Sequential,
    /// <summary>Calls are prepared in order, then executed concurrently.</summary>
    [JsonStringEnumMemberName("parallel")] Parallel,
}

/// <summary>Final or partial result of a tool execution.</summary>
/// <param name="Terminate">Stop after this tool batch. Honored only when every result in the batch sets it.</param>
public sealed record AgentToolResult(
    IReadOnlyList<ContentPart> Content,
    JsonNode? Details = null,
    Usage? Usage = null,
    IReadOnlyList<string>? AddedToolNames = null,
    bool Terminate = false,
    bool IsError = false)
{
    public static AgentToolResult FromText(string text, JsonNode? details = null) =>
        new([new TextContent(text)], details);

    public static AgentToolResult Error(string message) => new([new TextContent(message)], IsError: true);

    /// <summary>Concatenated text parts.</summary>
    public string Text => string.Concat(Content.OfType<TextContent>().Select(t => t.Text));
}

/// <summary>A tool the agent runtime can execute.</summary>
public sealed record ToolAccess(bool ReadOnly, IReadOnlyList<string> Resources);

public interface IAgentTool : ITool
{
    /// <summary>Short human-readable label.</summary>
    string Label { get; }

    /// <summary>Per-tool override of the loop's execution mode. Null = use the loop default.</summary>
    ToolExecutionMode? ExecutionMode => null;
    /// <summary>Resources read or written by this call. Null means the tool cannot describe its effects.</summary>
    ToolAccess? Access(JsonObject arguments) => null;
    /// <summary>A versioned identity for a safe read. Null disables memoization.</summary>
    ValueTask<string?> ReadCacheKeyAsync(JsonObject arguments, CancellationToken cancellationToken) => ValueTask.FromResult<string?>(null);
    ValueTask<bool> AcceptCachedReadAsync(JsonObject arguments, string key, CancellationToken cancellationToken) => ValueTask.FromResult(true);

    /// <summary>Optional maximum runtime; the loop's finite deadline applies when unset.</summary>
    TimeSpan? MaxDuration => null;

    /// <summary>
    /// An input action on a screen (a key, a click): the same call repeated is often the next step, not a loop (an arrow
    /// key through a list). The identical-call guard gives a batch of only these <see cref="AgentLoopConfig.MaxIdenticalInputCalls"/>.
    /// </summary>
    bool InputAction => false;

    /// <summary>
    /// Compatibility shim applied to raw arguments before validation, e.g. lifting a
    /// flat <c>{oldText,newText}</c> into <c>{edits:[...]}</c>. Return the input to skip.
    /// </summary>
    JsonNode? PrepareArguments(JsonNode? arguments) => arguments;

    /// <summary>
    /// Run the tool. Throw on failure; the loop turns the exception message into an error result.
    /// <paramref name="onUpdate"/> streams partial results while running.
    /// </summary>
    Task<AgentToolResult> ExecuteAsync(
        string toolCallId,
        JsonObject arguments,
        Action<AgentToolResult>? onUpdate,
        CancellationToken cancellationToken);
}

/// <summary>
/// Base class for tools with a typed argument record. Arguments are validated against
/// <see cref="Parameters"/> by the loop and then deserialized with the supplied source-generated type info.
/// </summary>
public abstract class AgentTool<TArgs> : IAgentTool
{
    private readonly JsonTypeInfo<TArgs> _argsTypeInfo;

    protected AgentTool(string name, string label, string description, JsonObject parameters, JsonTypeInfo<TArgs> argsTypeInfo)
    {
        Name = name;
        Label = label;
        Description = description;
        Parameters = parameters;
        _argsTypeInfo = argsTypeInfo;
    }

    public string Name { get; }
    public string Label { get; }
    public string Description { get; }
    public JsonObject Parameters { get; }
    public virtual ToolExecutionMode? ExecutionMode => null;
    public virtual ToolAccess? Access(JsonObject arguments) => null;
    public virtual ValueTask<string?> ReadCacheKeyAsync(JsonObject arguments, CancellationToken cancellationToken) => ValueTask.FromResult<string?>(null);
    public virtual ValueTask<bool> AcceptCachedReadAsync(JsonObject arguments, string key, CancellationToken cancellationToken) => ValueTask.FromResult(true);
    public virtual TimeSpan? MaxDuration => null;

    public virtual JsonNode? PrepareArguments(JsonNode? arguments) => arguments;

    public Task<AgentToolResult> ExecuteAsync(string toolCallId, JsonObject arguments, Action<AgentToolResult>? onUpdate, CancellationToken cancellationToken)
    {
        var typed = JsonSerializer.Deserialize(arguments, _argsTypeInfo)
            ?? throw new ToolArgumentException($"Tool \"{Name}\" received null arguments");
        return ExecuteAsync(toolCallId, typed, onUpdate, cancellationToken);
    }

    protected abstract Task<AgentToolResult> ExecuteAsync(string toolCallId, TArgs args, Action<AgentToolResult>? onUpdate, CancellationToken cancellationToken);
}

/// <summary>Thrown when tool-call arguments do not match the tool schema.</summary>
public sealed class ToolArgumentException(string message) : Exception(message);
