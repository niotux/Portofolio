// Ported from pi: packages/ai/src/api/openai-completions.ts (streaming loop, mapStopReason,
// parseChunkUsage) @ 71dca871b. Pure state machine: feed it chat.completion.chunk JSON, get events.
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PiSharp.Core.Messages;
using PiSharp.Core.Models;
using PiSharp.Llm.Json;

namespace PiSharp.Llm.OpenAi;

/// <summary>
/// Accumulates streamed deltas into one <see cref="AssistantMessage"/>. Like pi, a response has
/// at most one text block and one thinking block; tool calls are keyed by stream index and id.
/// </summary>
public sealed class ChunkAssembler
{
    private static readonly string[] ReasoningFields = ["reasoning_content", "reasoning", "reasoning_text"];

    private readonly ModelConfig _model;
    private readonly DateTimeOffset _timestamp = DateTimeOffset.UtcNow;
    private readonly List<Block> _blocks = [];
    private readonly Dictionary<int, ToolCallBlock> _toolCallsByIndex = [];
    private readonly Dictionary<string, ToolCallBlock> _toolCallsById = new(StringComparer.Ordinal);
    private TextBlock? _text;
    private ThinkingBlock? _thinking;

    private string? _responseId;
    private string? _responseModel;
    private Usage _usage = Usage.Zero;
    private StopReason _stopReason = StopReason.Stop;
    private string? _rawStopReason;
    private string? _errorMessage;
    private string? _errorCode;
    private int? _providerHttpStatus;
    private bool _providerRequestFailed;

    public ChunkAssembler(ModelConfig model)
    {
        _model = model;
    }

    public bool HasFinishReason { get; private set; }
    public StopReason StopReason => _stopReason;

    /// <summary>Immutable view of the message accumulated so far.</summary>
    public AssistantMessage Snapshot() => new()
    {
        Content = _blocks.Select(b => b.ToPart()).ToList(),
        Model = _model.Id,
        Provider = _model.Provider,
        ProviderHttpStatus = _providerHttpStatus,
        ProviderRequestFailed = _providerRequestFailed,
        ResponseModel = _responseModel,
        ResponseId = _responseId,
        Usage = _usage,
        StopReason = _stopReason,
        ErrorMessage = _errorMessage,
        ErrorCode = _errorCode,
        RawStopReason = _rawStopReason,
        Timestamp = _timestamp,
    };

    /// <summary>Applies one SSE data payload. Throws <see cref="LlmProtocolException"/> for server error payloads.</summary>
    public IReadOnlyList<AssistantMessageEvent> Apply(string json)
    {
        var events = new List<AssistantMessageEvent>();
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new LlmProtocolException($"Malformed stream chunk: {ex.Message}");
        }
        if (node is not JsonObject chunk) return events;

        if (chunk["error"] is { } error)
        {
            throw new LlmProtocolException(DescribeError(error), ExtractErrorCode(error));
        }

        if (chunk["usage"] is JsonObject usage) _usage = ParseUsage(usage);

        if (chunk["choices"] is not JsonArray { Count: > 0 } choices || choices[0] is not JsonObject choice) return events;

        if (choice["usage"] is JsonObject choiceUsage && chunk["usage"] is null) _usage = ParseUsage(choiceUsage);

        if (GetString(choice, "finish_reason") is { } finishReason)
        {
            HasFinishReason = true;
            _rawStopReason = finishReason;
            (_stopReason, _errorMessage) = MapStopReason(finishReason);
        }

        if (GetString(chunk, "id") is { Length: > 0 } id) _responseId ??= id;
        if (GetString(chunk, "model") is { Length: > 0 } responseModel && responseModel != _model.Id) _responseModel = responseModel;

        if (choice["delta"] is not JsonObject delta) return events;

        if (GetString(delta, "content") is { Length: > 0 } content)
        {
            var block = EnsureText(events);
            block.Text.Append(content);
            events.Add(new TextDelta(IndexOf(block), content, Snapshot()));
        }

        foreach (var field in ReasoningFields)
        {
            if (GetString(delta, field) is { Length: > 0 } reasoning)
            {
                var block = EnsureThinking(field, events);
                block.Thinking.Append(reasoning);
                events.Add(new ThinkingDelta(IndexOf(block), reasoning, Snapshot()));
                break;
            }
        }

        if (delta["tool_calls"] is JsonArray toolCalls)
        {
            foreach (var item in toolCalls)
            {
                if (item is not JsonObject toolCall) continue;
                var block = EnsureToolCall(toolCall, events);

                var argsDelta = "";
                if (toolCall["function"] is JsonObject function && GetString(function, "arguments") is { Length: > 0 } args)
                {
                    argsDelta = args;
                    block.PartialArgs.Append(args);
                    block.Arguments = StreamingJson.ParseObject(block.PartialArgs.ToString());
                }
                events.Add(new ToolCallDelta(IndexOf(block), argsDelta, Snapshot()));
            }
        }

        return events;
    }

    /// <summary>
    /// Closes open blocks and validates the stream ended properly. Returns the end events followed by
    /// <see cref="AssistantDone"/>. Throws <see cref="LlmProtocolException"/> when the server reported
    /// an error finish reason or never sent one.
    /// </summary>
    public IReadOnlyList<AssistantMessageEvent> Finish()
    {
        var events = new List<AssistantMessageEvent>();
        foreach (var block in _blocks) FinishBlock(block, events);

        var compat = _model.Compat;
        if (!HasFinishReason && !compat.SupportsFinishReason)
        {
            _stopReason = _blocks.Any(b => b is ToolCallBlock) ? StopReason.ToolUse : StopReason.Stop;
        }
        if (_stopReason == StopReason.Error)
        {
            throw new LlmProtocolException(_errorMessage ?? "Provider returned an error stop reason");
        }
        if ((compat.SupportsFinishReason && !HasFinishReason) || _stopReason == StopReason.Pending)
        {
            throw new LlmProtocolException("Stream ended without finish_reason");
        }

        events.Add(new AssistantDone(_stopReason, Snapshot()));
        return events;
    }

    /// <summary>Terminal error event for a failed stream. <paramref name="errorCode"/> is the
    /// machine-readable provider code parsed from the failure, carried onto the message so overflow
    /// recognition does not have to re-read the human text.</summary>
    public AssistantError Fail(StopReason reason, string message, string? errorCode = null, int? providerHttpStatus = null, bool providerRequestFailed = false)
    {
        _stopReason = reason;
        _errorMessage = message;
        _errorCode = errorCode;
        _providerHttpStatus = providerHttpStatus;
        _providerRequestFailed = providerRequestFailed;
        return new AssistantError(reason, Snapshot());
    }

    private void FinishBlock(Block block, List<AssistantMessageEvent> events)
    {
        var index = IndexOf(block);
        switch (block)
        {
            case TextBlock text:
                events.Add(new TextEnd(index, text.Text.ToString(), Snapshot()));
                break;
            case ThinkingBlock thinking:
                events.Add(new ThinkingEnd(index, thinking.Thinking.ToString(), Snapshot()));
                break;
            case ToolCallBlock call:
                call.Arguments = StreamingJson.ParseObject(call.PartialArgs.ToString());
                // Freeze the block: the end event and every later snapshot share one ToolCallContent instance,
                // so record equality (which compares the JsonObject by reference) holds across them.
                call.Final = new ToolCallContent(call.Id, call.Name, call.Arguments);
                events.Add(new ToolCallEnd(index, call.Final, Snapshot()));
                break;
        }
    }

    private TextBlock EnsureText(List<AssistantMessageEvent> events)
    {
        if (_text is null)
        {
            _text = new TextBlock();
            _blocks.Add(_text);
            events.Add(new TextStart(IndexOf(_text), Snapshot()));
        }
        return _text;
    }

    private ThinkingBlock EnsureThinking(string signature, List<AssistantMessageEvent> events)
    {
        if (_thinking is null)
        {
            _thinking = new ThinkingBlock(signature);
            _blocks.Add(_thinking);
            events.Add(new ThinkingStart(IndexOf(_thinking), Snapshot()));
        }
        return _thinking;
    }

    private ToolCallBlock EnsureToolCall(JsonObject toolCall, List<AssistantMessageEvent> events)
    {
        int? streamIndex = toolCall["index"] is JsonValue iv && iv.TryGetValue(out int i) ? i : null;
        var id = GetString(toolCall, "id");
        var name = toolCall["function"] is JsonObject fn ? GetString(fn, "name") : null;

        ToolCallBlock? block = null;
        if (streamIndex is { } si) _toolCallsByIndex.TryGetValue(si, out block);
        if (block is null && id is { Length: > 0 }) _toolCallsById.TryGetValue(id, out block);

        if (block is null)
        {
            block = new ToolCallBlock { Id = id ?? "", Name = name ?? "", StreamIndex = streamIndex };
            if (streamIndex is { } newIndex) _toolCallsByIndex[newIndex] = block;
            if (id is { Length: > 0 }) _toolCallsById[id] = block;
            _blocks.Add(block);
            events.Add(new ToolCallStart(IndexOf(block), Snapshot()));
        }

        if (streamIndex is { } idx && block.StreamIndex is null)
        {
            block.StreamIndex = idx;
            _toolCallsByIndex[idx] = block;
        }
        if (id is { Length: > 0 })
        {
            if (block.Id.Length == 0) block.Id = id;
            _toolCallsById[id] = block;
        }
        if (block.Name.Length == 0 && name is { Length: > 0 }) block.Name = name;

        return block;
    }

    private int IndexOf(Block block) => _blocks.IndexOf(block);

    public static (StopReason Reason, string? Error) MapStopReason(string? finishReason) => finishReason switch
    {
        null or "stop" or "end" => (StopReason.Stop, null),
        "length" => (StopReason.Length, null),
        "function_call" or "tool_calls" => (StopReason.ToolUse, null),
        _ => (StopReason.Error, $"Provider finish_reason: {finishReason}"),
    };

    public static Usage ParseUsage(JsonObject usage)
    {
        var promptTokens = GetInt(usage, "prompt_tokens") ?? 0;
        var details = usage["prompt_tokens_details"] as JsonObject;
        var cacheRead = (details is null ? null : GetInt(details, "cached_tokens"))
            ?? GetInt(usage, "prompt_cache_hit_tokens")
            ?? GetInt(usage, "cached_tokens")
            ?? 0;
        var cacheWrite = (details is null ? null : GetInt(details, "cache_write_tokens")) ?? 0;
        var input = Math.Max(0, promptTokens - cacheRead - cacheWrite);
        var output = GetInt(usage, "completion_tokens") ?? 0;
        var reasoning = usage["completion_tokens_details"] is JsonObject completion ? GetInt(completion, "reasoning_tokens") : null;
        return new Usage(input, output, cacheRead, cacheWrite, input + output + cacheRead + cacheWrite, reasoning);
    }

    /// <summary>
    /// Pulls the machine-readable code out of an <c>error</c> payload: <c>error.code</c>, then
    /// <c>error.type</c>, then the same keys in a nested <c>error.error</c> (the shape proxies use).
    /// Null when the server gave none, which is when free-text recognition is the only option left.
    /// </summary>
    public static string? ExtractErrorCode(JsonNode error)
    {
        if (error is JsonObject obj)
        {
            var code = FirstOf(obj, "code", "type");
            if (!string.IsNullOrEmpty(code)) return code;
            if (obj["error"] is JsonObject inner) return ExtractErrorCode(inner);
        }
        return null;
    }

    private static string? FirstOf(JsonObject obj, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = GetString(obj, key);
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        return null;
    }

    /// <summary>Formats an <c>error</c> payload the way pi's SDK surfaced it: the message, falling back to raw JSON.</summary>
    public static string DescribeError(JsonNode error)
    {
        if (error is JsonObject obj)
        {
            if (GetString(obj, "message") is { Length: > 0 } message) return message;
            if (obj["error"] is JsonObject inner) return DescribeError(inner);
        }
        else if (error is JsonValue value && value.TryGetValue(out string? text))
        {
            return text;
        }
        return error.ToJsonString();
    }

    private static string? GetString(JsonObject obj, string key) =>
        obj[key] is JsonValue v && v.TryGetValue(out string? s) ? s : null;

    private static int? GetInt(JsonObject obj, string key)
    {
        if (obj[key] is not JsonValue v) return null;
        if (v.TryGetValue(out int i)) return i;
        if (v.TryGetValue(out long l)) return (int)Math.Clamp(l, int.MinValue, int.MaxValue);
        if (v.TryGetValue(out double d)) return (int)d;
        return null;
    }

    private abstract class Block
    {
        public abstract ContentPart ToPart();
    }

    private sealed class TextBlock : Block
    {
        public StringBuilder Text { get; } = new();
        public override ContentPart ToPart() => new TextContent(Text.ToString());
    }

    private sealed class ThinkingBlock(string signature) : Block
    {
        public StringBuilder Thinking { get; } = new();
        public override ContentPart ToPart() => new ThinkingContent(Thinking.ToString(), signature);
    }

    private sealed class ToolCallBlock : Block
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public int? StreamIndex { get; set; }
        public StringBuilder PartialArgs { get; } = new();
        public JsonObject Arguments { get; set; } = new();
        public ToolCallContent? Final { get; set; }
        public override ContentPart ToPart() => Final ?? new ToolCallContent(Id, Name, (JsonObject)Arguments.DeepClone());
    }
}

/// <summary>The server violated the chat-completions stream contract or reported an error payload.</summary>
/// <summary>Server error payload or a stream that violates the OpenAI completions protocol.</summary>
public sealed class LlmProtocolException(string message, string? errorCode = null) : Exception(message)
{
    /// <summary>Parsed machine-readable error code from the server payload, when present.</summary>
    public string? ErrorCode { get; } = errorCode;
}
