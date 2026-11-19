// Console REPL rendering and JSON-event mode. Ported from pi print-mode / json-event.ts @ 71dca871b
using System.Text.Json;
using System.Text.Json.Nodes;
using PiSharp.Core.Agent;
using PiSharp.Core.Harness;
using PiSharp.Core.Json;
using PiSharp.Core.Messages;

namespace PiSharp.Cli;

public interface IEventSink
{
    void OnEvent(AgentEvent evt);
    void WriteLine(string text);
    void WriteError(string text);

    /// <summary>
    /// Writes one already-serialized JSON object to the event channel (stdout). Used for
    /// lifecycle signals that have no <see cref="AgentEvent"/>, such as the context-overflow
    /// notice (wi-8): the supervisor reads the event channel, so the notice must land there
    /// rather than on stderr, and must be machine-parsable.
    /// </summary>
    void WriteStructured(string json)
    {
        // Default: the event channel is stdout verbatim.
        WriteLine(json);
    }
}

/// <summary>Streams assistant text, prints tool calls and truncated results, colors edit diffs.</summary>
public sealed class ConsoleRenderer : IEventSink
{
    private readonly TextWriter _output;
    private readonly TextWriter _error;
    private readonly Ansi _ansi;
    private readonly bool _hideThinking;
    private readonly bool _printFinalOnly;
    private readonly bool _interactive;
    private bool _inText;
    private bool _streamedText;
    private int? _activeToolCallIndex;
    private int _liveToolArgumentChars;
    private bool _liveToolArgumentTruncated;
    private readonly HashSet<string> _streamedToolCallIds = new(StringComparer.Ordinal);
    private readonly List<string> _finalText = [];

    private const int MaxLiveToolArgumentChars = 80;

    public ConsoleRenderer(
        TextWriter output,
        TextWriter error,
        Ansi ansi,
        bool hideThinking = false,
        bool printFinalOnly = false,
        bool interactive = false)
    {
        _output = output;
        _error = error;
        _ansi = ansi;
        _hideThinking = hideThinking;
        _printFinalOnly = printFinalOnly;
        _interactive = interactive;
    }

    public IReadOnlyList<string> FinalText => _finalText;
    public int ExitCode { get; private set; }

    public void WriteLine(string text) => _output.WriteLine(text);
    public void WriteError(string text) => _error.WriteLine(text);

    /// <summary>A plain console session has no JSON event channel; echo the structured notice.</summary>
    public void WriteStructured(string json) => _output.WriteLine(json);

    public void OnEvent(AgentEvent evt)
    {
        switch (evt)
        {
            case MessageStart { Message: UserMessage user } when _interactive && user.Text.StartsWith("[steering from @", StringComparison.Ordinal):
                CloseActiveToolCall();
                if (_inText)
                {
                    _output.Write(_ansi.Reset);
                    _output.WriteLine();
                    _inText = false;
                }
                _output.WriteLine(_ansi.Dim + "[steering] " + user.Text + _ansi.Reset);
                break;
            case MessageUpdate { AssistantEvent: AssistantDiscardPartial }:
                // pi-sharp-hh-7: the stream dropped its partial content and re-issued the request.
                // Nothing already on screen comes back, so close the text run and dim-mark the
                // retry; the retried content streams over the same line.
                if (_printFinalOnly) break;
                CloseActiveToolCall();
                if (_inText)
                {
                    _output.Write(_ansi.Reset);
                    _inText = false;
                }
                _output.Write(_ansi.Dim + "[stream reconnected; content restarted]" + _ansi.Reset);
                break;
            case MessageUpdate { AssistantEvent: TextDelta delta }:
                if (_printFinalOnly) break;
                CloseActiveToolCall();
                if (!_inText)
                {
                    _inText = true;
                    _output.Write(_ansi.Bold);
                }
                _output.Write(delta.Delta);
                _streamedText = true;
                break;
            case MessageUpdate { AssistantEvent: ThinkingDelta delta }:
                if (_printFinalOnly || _hideThinking) break;
                CloseActiveToolCall();
                _output.Write(_ansi.Dim + delta.Delta + _ansi.Reset);
                break;
            case MessageUpdate { AssistantEvent: ToolCallStart start }:
                if (_printFinalOnly) break;
                StartToolCall(start.ContentIndex, ToolCallAt(start.Partial, start.ContentIndex));
                break;
            case MessageUpdate { AssistantEvent: ToolCallDelta delta }:
                if (_printFinalOnly) break;
                if (_activeToolCallIndex != delta.ContentIndex)
                    StartToolCall(delta.ContentIndex, ToolCallAt(delta.Partial, delta.ContentIndex));
                WriteLiveToolArguments(delta.Delta);
                break;
            case MessageUpdate { AssistantEvent: ToolCallEnd end }:
                if (_printFinalOnly) break;
                if (_activeToolCallIndex != end.ContentIndex)
                    StartToolCall(end.ContentIndex, end.ToolCall);
                if (end.ToolCall.Id.Length > 0) _streamedToolCallIds.Add(end.ToolCall.Id);
                CloseActiveToolCall();
                break;
            case MessageEnd { Message: AssistantMessage assistant }:
                CloseActiveToolCall();
                if (_inText)
                {
                    _output.Write(_ansi.Reset);
                    _output.WriteLine();
                    _inText = false;
                }
                if (assistant.StopReason is StopReason.Error or StopReason.Aborted)
                {
                    _error.WriteLine(_ansi.Red + (assistant.ErrorMessage ?? assistant.StopReason.ToString()) + _ansi.Reset);
                    ExitCode = 1;
                }
                else if (assistant.Text.Length > 0)
                {
                    _finalText.Clear();
                    _finalText.Add(assistant.Text);
                    // A provider that returns the message whole (no text deltas) would otherwise print nothing.
                    if (!_printFinalOnly && !_streamedText)
                        _output.WriteLine(_ansi.Bold + assistant.Text + _ansi.Reset);
                }
                _streamedText = false;
                break;
            case ToolExecutionStart start:
                if (_printFinalOnly) break;
                if (_streamedToolCallIds.Remove(start.ToolCallId)) break;
                if (_inText) { _output.Write(_ansi.Reset); _output.WriteLine(); _inText = false; }
                _output.WriteLine(_ansi.Cyan + "⏺ " + start.ToolName + _ansi.Reset + " " + _ansi.Dim + CompactArgs(start.Arguments) + _ansi.Reset);
                break;
            case ToolExecutionEnd end:
                if (_printFinalOnly) break;
                WriteToolResult(end);
                break;
            case AgentEnd:
                if (_printFinalOnly)
                {
                    foreach (var text in _finalText) _output.WriteLine(text);
                }
                break;
        }
    }

    private void WriteToolResult(ToolExecutionEnd end)
    {
        if (end.Result.Details is JsonObject details && TryString(details["diff"]) is { Length: > 0 } diff)
        {
            _output.WriteLine(_ansi.ColorizeDiff(Truncate(diff, 40, 4000)));
            return;
        }
        var text = end.Result.Text;
        if (string.IsNullOrWhiteSpace(text)) return;
        var color = end.IsError ? _ansi.Red : _ansi.Gray;
        foreach (var line in Truncate(text, 20, 2000).Replace("\r\n", "\n").Split('\n'))
            _output.WriteLine(color + "  " + line + _ansi.Reset);
    }

    private static string CompactArgs(JsonObject args)
    {
        if (TryString(args["path"]) is { } path) return path;
        if (TryString(args["command"]) is { } command)
            return command.Length > 80 ? command[..77] + "..." : command;
        var json = args.ToJsonString();
        return json.Length > 80 ? json[..77] + "..." : json;
    }

    private static string? TryString(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private void StartToolCall(int contentIndex, ToolCallContent? call)
    {
        if (_activeToolCallIndex == contentIndex) return;
        CloseActiveToolCall();
        if (_inText)
        {
            _output.Write(_ansi.Reset);
            _output.WriteLine();
            _inText = false;
        }
        _output.Write(_ansi.Cyan + "⏺ " + (call?.Name ?? "tool") + _ansi.Reset + " " + _ansi.Dim);
        _activeToolCallIndex = contentIndex;
        _liveToolArgumentChars = 0;
        _liveToolArgumentTruncated = false;
    }

    private void WriteLiveToolArguments(string delta)
    {
        if (_liveToolArgumentTruncated) return;
        var remaining = MaxLiveToolArgumentChars - _liveToolArgumentChars;
        if (remaining <= 0)
        {
            _output.Write("\u2026");
            _liveToolArgumentTruncated = true;
            return;
        }

        var count = Math.Min(remaining, delta.Length);
        if (count > 0)
        {
            _output.Write(delta[..count]);
            _liveToolArgumentChars += count;
        }
        if (count < delta.Length)
        {
            _output.Write("\u2026");
            _liveToolArgumentTruncated = true;
        }
    }

    private void CloseActiveToolCall()
    {
        if (_activeToolCallIndex is null) return;
        _output.Write(_ansi.Reset);
        _output.WriteLine();
        _activeToolCallIndex = null;
    }

    private static ToolCallContent? ToolCallAt(AssistantMessage partial, int contentIndex) =>
        contentIndex >= 0 && contentIndex < partial.Content.Count
            ? partial.Content[contentIndex] as ToolCallContent
            : null;

    private static string Truncate(string text, int maxLines, int maxChars)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        if (lines.Length > maxLines)
            return string.Join('\n', lines.Take(maxLines)) + $"\n… ({lines.Length - maxLines} more lines)";
        if (text.Length > maxChars) return text[..maxChars] + "…";
        return text;
    }
}

/// <summary>Emits one JSON object per agent event (JSONL). Useful for driving from another process.</summary>
public sealed class JsonEventSink : IEventSink
{
    private readonly TextWriter _output;
    private readonly TextWriter _error;

    public JsonEventSink(TextWriter output, TextWriter error)
    {
        _output = output;
        _error = error;
    }

    public void WriteLine(string text) => _output.WriteLine(text);
    public void WriteError(string text) => _error.WriteLine(text);

    public void WriteStructured(string json) => _output.WriteLine(json);

    public void OnEvent(AgentEvent evt)
    {
        var json = ToJson(evt);
        if (json is not null) _output.WriteLine(json.ToJsonString());
    }

    public static JsonObject? ToJson(AgentEvent evt) => evt switch
    {
        AgentStart => new JsonObject { ["type"] = "agent_start" },
        AgentEnd end => new JsonObject
        {
            ["type"] = "agent_end",
            ["outcome"] = end.Outcome,
            ["messages"] = SerializeMessages(end.Messages),
        },
        TurnStart => new JsonObject { ["type"] = "turn_start" },
        TurnEnd turn => new JsonObject
        {
            ["type"] = "turn_end",
            ["stopReason"] = turn.Message.StopReason.ToString().ToLowerInvariant(),
            ["toolResults"] = turn.ToolResults.Count,
        },
        MessageStart start => new JsonObject
        {
            ["type"] = "message_start",
            ["role"] = RoleOf(start.Message),
            ["message"] = SerializeMessage(start.Message),
        },
        MessageUpdate update => MessageUpdateJson(update),
        MessageEnd end => new JsonObject
        {
            ["type"] = "message_end",
            ["role"] = RoleOf(end.Message),
            ["message"] = SerializeMessage(end.Message),
        },
        ToolExecutionStart start => new JsonObject
        {
            ["type"] = "tool_execution_start",
            ["toolCallId"] = start.ToolCallId,
            ["toolName"] = start.ToolName,
            ["arguments"] = start.Arguments.DeepClone(),
        },
        ToolExecutionUpdate update => new JsonObject
        {
            ["type"] = "tool_execution_update",
            ["toolCallId"] = update.ToolCallId,
            ["toolName"] = update.ToolName,
            ["arguments"] = update.Arguments.DeepClone(),
            ["text"] = update.PartialResult.Text,
        },
        ToolExecutionEnd end => new JsonObject
        {
            ["type"] = "tool_execution_end",
            ["toolCallId"] = end.ToolCallId,
            ["toolName"] = end.ToolName,
            ["isError"] = end.IsError,
            ["text"] = end.Result.Text,
        },
        _ => new JsonObject { ["type"] = evt.GetType().Name },
    };

    private static JsonObject? MessageUpdateJson(MessageUpdate update)
    {
        // Skip cumulative snapshots; emit only text/thinking deltas so the stream stays small.
        return update.AssistantEvent switch
        {
            TextDelta d => new JsonObject { ["type"] = "text_delta", ["delta"] = d.Delta },
            ThinkingDelta d => new JsonObject { ["type"] = "thinking_delta", ["delta"] = d.Delta },
            ToolCallStart start => new JsonObject
            {
                ["type"] = "toolcall_start",
                ["contentIndex"] = start.ContentIndex,
                ["toolCall"] = ToolCallJson(ToolCallAt(start.Partial, start.ContentIndex)),
            },
            ToolCallDelta delta => new JsonObject
            {
                ["type"] = "toolcall_delta",
                ["contentIndex"] = delta.ContentIndex,
                ["delta"] = delta.Delta,
            },
            ToolCallEnd end => new JsonObject
            {
                ["type"] = "toolcall_end",
                ["contentIndex"] = end.ContentIndex,
                ["toolCall"] = ToolCallJson(end.ToolCall),
            },
            AssistantDone done => new JsonObject { ["type"] = "assistant_done", ["reason"] = done.Reason.ToString().ToLowerInvariant() },
            AssistantError err => new JsonObject { ["type"] = "assistant_error", ["error"] = err.Error.ErrorMessage },
            AssistantDiscardPartial => new JsonObject { ["type"] = "assistant_discard_partial" },
            _ => null,
        };
    }

    private static JsonObject ToolCallJson(ToolCallContent? call) => new()
    {
        ["id"] = call?.Id,
        ["name"] = call?.Name,
        ["arguments"] = call?.Arguments.DeepClone() ?? new JsonObject(),
    };

    private static ToolCallContent? ToolCallAt(AssistantMessage partial, int contentIndex) =>
        contentIndex >= 0 && contentIndex < partial.Content.Count
            ? partial.Content[contentIndex] as ToolCallContent
            : null;

    private static string RoleOf(Message message) => message switch
    {
        UserMessage => "user",
        AssistantMessage => "assistant",
        ToolResultMessage => "toolResult",
        _ => "other",
    };

    private static JsonNode? SerializeMessage(Message message) =>
        JsonSerializer.SerializeToNode(message, CoreJsonContext.Default.Message);

    private static JsonArray SerializeMessages(IReadOnlyList<Message> messages)
    {
        var array = new JsonArray();
        foreach (var message in messages)
        {
            var node = SerializeMessage(message);
            if (node is not null) array.Add(node);
        }
        return array;
    }
}

/// <summary>Forwards every event to the user-facing sink and, without ever throwing, to the cubicle tracer.</summary>
public sealed class TracingSink(IEventSink inner, CubicleTracer tracer) : IEventSink
{
    public IEventSink Inner { get; } = inner;

    public static IEventSink Unwrap(IEventSink sink) => sink is TracingSink tracing ? tracing.Inner : sink;

    public void OnEvent(AgentEvent evt)
    {
        Inner.OnEvent(evt);
        tracer.OnEvent(evt);
    }

    public void WriteLine(string text) => Inner.WriteLine(text);

    public void WriteError(string text) => Inner.WriteError(text);

    public void WriteStructured(string json) => Inner.WriteStructured(json);
}
