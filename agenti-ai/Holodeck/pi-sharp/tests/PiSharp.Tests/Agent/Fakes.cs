// Test doubles shared by the agent loop tests: a scripted stream function and a delegate-based tool.
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using PiSharp.Core.Agent;
using PiSharp.Core.Messages;
using PiSharp.Core.Models;

namespace PiSharp.Tests.Agent;

internal static class Fakes
{
    /// <summary>The test model, with the window and output every model must now configure (no defaults since 2026-10-20).</summary>
    public static readonly ModelConfig Model = new() { Id = "test-model", BaseUrl = "http://localhost:1234/v1", ContextWindow = 32768, MaxOutputTokens = 8192 };

    public static AssistantMessage Assistant(StopReason stop, params ContentPart[] content) => new()
    {
        Content = content,
        Model = "test-model",
        StopReason = stop,
    };

    public static AssistantMessage Text(string text) => Assistant(StopReason.Stop, new TextContent(text));

    public static AssistantMessage ToolCalls(params ToolCallContent[] calls) => Assistant(StopReason.ToolUse, calls);

    public static AssistantMessage Error(string message) =>
        Assistant(StopReason.Error) with { ErrorMessage = message };

    public static ToolCallContent Call(string id, string name, JsonObject? args = null) => new(id, name, args ?? new JsonObject());

    public static JsonObject Json(string json) => JsonNode.Parse(json)!.AsObject();

    public static AgentLoopConfig Config(ScriptedStream? _ = null) => new() { Model = Model };

    public static async Task<List<AgentEvent>> Collect(IAsyncEnumerable<AgentEvent> events)
    {
        var list = new List<AgentEvent>();
        await foreach (var e in events) list.Add(e);
        return list;
    }

    public static string ResultText(ToolResultMessage m) =>
        string.Concat(m.Content.OfType<TextContent>().Select(t => t.Text));

    public static string Role(Message m) => m switch
    {
        UserMessage => "user",
        AssistantMessage => "assistant",
        ToolResultMessage => "toolResult",
        _ => m.GetType().Name,
    };
}

/// <summary>Returns pre-scripted assistant messages, one per stream call, as start + terminal event.</summary>
internal sealed class ScriptedStream
{
    private readonly Queue<Func<AssistantMessage>> _responses = new();

    public int Calls { get; private set; }
    public List<LlmContext> Contexts { get; } = [];
    public List<StreamOptions> Options { get; } = [];

    /// <summary>When set, the stream throws this after emitting the start event.</summary>
    public Exception? ThrowAfterStart { get; init; }

    public ScriptedStream Then(AssistantMessage message) => Then(() => message);

    public ScriptedStream Then(Func<AssistantMessage> factory)
    {
        _responses.Enqueue(factory);
        return this;
    }

    public async IAsyncEnumerable<AssistantMessageEvent> Stream(
        ModelConfig model,
        LlmContext context,
        StreamOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Calls++;
        Contexts.Add(context);
        Options.Add(options);

        var empty = new AssistantMessage { Content = [], Model = model.Id };
        yield return new AssistantStart(empty);
        await Task.Yield();

        if (ThrowAfterStart is not null) throw ThrowAfterStart;

        if (cancellationToken.IsCancellationRequested)
        {
            yield return new AssistantError(StopReason.Aborted, empty with { StopReason = StopReason.Aborted, ErrorMessage = "Operation aborted" });
            yield break;
        }

        if (_responses.Count == 0) throw new InvalidOperationException("Scripted stream exhausted");
        var final = _responses.Dequeue()();

        if (final.StopReason is StopReason.Error or StopReason.Aborted)
        {
            yield return new AssistantError(final.StopReason, final);
        }
        else
        {
            yield return new AssistantDone(final.StopReason, final);
        }
    }
}

internal sealed class DelegateTool : IAgentTool
{
    public string Name { get; init; } = "test_tool";
    public string Label { get; init; } = "Test Tool";
    public string Description { get; init; } = "A test tool";
    public JsonObject Parameters { get; init; } = Fakes.Json("""{"type":"object","properties":{"value":{"type":"string"}}}""");
    public ToolExecutionMode? ExecutionMode { get; init; }
    public bool InputAction { get; init; }

    public Func<JsonNode?, JsonNode?>? Prepare { get; init; }

    public Func<string, JsonObject, Action<AgentToolResult>?, CancellationToken, Task<AgentToolResult>> Execute { get; init; } =
        (_, _, _, _) => Task.FromResult(AgentToolResult.FromText("ok"));

    public JsonNode? PrepareArguments(JsonNode? arguments) => Prepare is null ? arguments : Prepare(arguments);

    public Task<AgentToolResult> ExecuteAsync(string toolCallId, JsonObject arguments, Action<AgentToolResult>? onUpdate, CancellationToken cancellationToken) =>
        Execute(toolCallId, arguments, onUpdate, cancellationToken);
}

/// <summary>A non-LLM message type, like pi's custom messages, used to test ConvertToLlm.</summary>
internal sealed record NoteMessage(string Note) : Message;
