// Ported from pi packages/ai/src/api/openai-responses-shared.ts @ 71dca871b.
using System.Text.Json.Nodes;
using PiSharp.Core.Messages;
using PiSharp.Core.Models;
using PiSharp.Llm.Json;

namespace PiSharp.Llm.Responses;

internal sealed class ResponsesAssembler(ModelConfig model)
{
    private readonly List<ContentPart> _content = [];
    private readonly Dictionary<int, int> _slots = [];
    private readonly Dictionary<int, string> _arguments = [];
    private readonly HashSet<int> _ended = [];
    /// <summary>Slots holding a free-form (custom) tool call, whose input is plain text, not JSON.</summary>
    private readonly HashSet<int> _custom = [];
    private Usage _usage = Usage.Zero;
    private string? _responseId;
    public StopReason Reason { get; private set; } = StopReason.Pending;
    public string? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public int? ProviderHttpStatus { get; private set; }
    public bool ProviderRequestFailed { get; private set; }
    public string? RateLimitReason { get; private set; }
    public bool Terminal => Reason != StopReason.Pending;

    public AssistantMessage Snapshot() => new()
    {
        Model = model.Id, Provider = model.Provider, ResponseId = _responseId, Content = _content.ToArray(),
        Usage = _usage, StopReason = Reason, ErrorCode = ErrorCode, ErrorMessage = ErrorMessage,
        ProviderHttpStatus = ProviderHttpStatus, ProviderRequestFailed = ProviderRequestFailed, RateLimitReason = RateLimitReason,
    };

    public IReadOnlyList<AssistantMessageEvent> Push(JsonObject evt)
    {
        var events = new List<AssistantMessageEvent>();
        var type = Text(evt, "type");
        var index = evt["output_index"]?.GetValue<int>() ?? 0;
        if (type == "response.created") _responseId = Text(evt["response"] as JsonObject, "id");
        else if (type == "response.output_item.added") Add(index, evt["item"]!.AsObject(), events);
        else if (type is "response.output_text.delta" or "response.refusal.delta")
        {
            var slot = Slot(index);
            var delta = Text(evt, "delta") ?? "";
            var text = _content[slot] as TextContent ?? throw Protocol();
            _content[slot] = text with { Text = text.Text + delta };
            events.Add(new TextDelta(slot, delta, Snapshot()));
        }
        else if (type is "response.reasoning_summary_text.delta" or "response.reasoning_text.delta")
        {
            var slot = Slot(index);
            var delta = Text(evt, "delta") ?? "";
            var thinking = _content[slot] as ThinkingContent ?? throw Protocol();
            _content[slot] = thinking with { Thinking = thinking.Thinking + delta };
            events.Add(new ThinkingDelta(slot, delta, Snapshot()));
        }
        else if (type == "response.function_call_arguments.delta")
        {
            var slot = Slot(index);
            var delta = Text(evt, "delta") ?? "";
            _arguments[slot] = _arguments.GetValueOrDefault(slot, "") + delta;
            var call = _content[slot] as ToolCallContent ?? throw Protocol();
            _content[slot] = call with { Arguments = StreamingJson.ParseObject(_arguments[slot]) };
            events.Add(new ToolCallDelta(slot, delta, Snapshot()));
        }
        else if (type == "response.custom_tool_call_input.delta")
        {
            // A free-form tool's input arrives as plain text (Codex's apply_patch); the call keeps it as {"input": text}.
            var slot = Slot(index);
            var delta = Text(evt, "delta") ?? "";
            _arguments[slot] = _arguments.GetValueOrDefault(slot, "") + delta;
            var call = _content[slot] as ToolCallContent ?? throw Protocol();
            _content[slot] = call with { Arguments = new JsonObject { ["input"] = _arguments[slot] } };
            events.Add(new ToolCallDelta(slot, delta, Snapshot()));
        }
        else if (type == "response.output_item.done") Finish(index, evt["item"]!.AsObject(), events);
        else if (type is "response.completed" or "response.incomplete")
        {
            var response = evt["response"]!.AsObject();
            _responseId = Text(response, "id") ?? _responseId;
            if (response["output"] is JsonArray output)
                for (var i = 0; i < output.Count; i++) Finish(i, output[i]!.AsObject(), events);
            if (response["usage"] is JsonObject usage)
            {
                var input = usage["input_tokens"]?.GetValue<int>() ?? 0;
                var cached = usage["input_tokens_details"]?["cached_tokens"]?.GetValue<int>() ?? 0;
                var tokens = usage["output_tokens"]?.GetValue<int>() ?? 0;
                _usage = new(Math.Max(0, input - cached), tokens, cached, 0,
                    usage["total_tokens"]?.GetValue<int>() ?? input + tokens,
                    usage["output_tokens_details"]?["reasoning_tokens"]?.GetValue<int>());
            }
            var status = Text(response, "status");
            if (type == "response.incomplete" || status == "incomplete")
            {
                if (Text(response["incomplete_details"] as JsonObject, "reason") == "max_output_tokens") Reason = StopReason.Length;
                else Fail("response_incomplete", "Subscription response was incomplete.");
            }
            else if (status is null or "completed") Reason = _content.OfType<ToolCallContent>().Any() ? StopReason.ToolUse : StopReason.Stop;
            else Fail("response_failed", "Subscription response did not complete.");
        }
        else if (type is "response.failed" or "error")
        {
            var error = evt["response"]?["error"] as JsonObject ?? evt["error"] as JsonObject ?? evt;
            var code = SafeCode(Text(error, "code")) ?? "response_failed";
            // The provider's own words go with the code (2026-10-25: two Grok seats failed one request for over ten minutes and
            // their logs said only "Subscription provider returned an error."; neither the seat nor an operator could tell why).
            var why = Printable(Text(error, "message"), 400);
            // A failure on the provider's side counts as a failed provider request, so a seat whose retries run out
            // still shows "model unavailable" after a streak instead of looking idle.
            Fail(code, $"Subscription provider returned an error ({code}){(why is null ? "." : ": " + why)}",
                providerRequestFailed: SubscriptionResponsesClient.ProviderTransient(code));
        }
        return events;
    }

    private void Add(int index, JsonObject item, List<AssistantMessageEvent> events)
    {
        if (_slots.ContainsKey(index)) return;
        var slot = _content.Count;
        ContentPart content = Text(item, "type") switch
        {
            "message" => new TextContent(""),
            "reasoning" => new ThinkingContent(""),
            "compaction" or "context_compaction" => new ThinkingContent(""),
            "function_call" => new ToolCallContent(Required(item, "call_id") + "|" + Required(item, "id"), Required(item, "name"), new()),
            "custom_tool_call" => new ToolCallContent(Required(item, "call_id") + "|" + (Text(item, "id") ?? ""), Required(item, "name"), new()),
            _ => throw Protocol(),
        };
        _slots.Add(index, slot);
        _content.Add(content);
        if (Text(item, "type") == "custom_tool_call") _custom.Add(slot);
        events.Add(content switch
        {
            TextContent => new TextStart(slot, Snapshot()),
            ThinkingContent => new ThinkingStart(slot, Snapshot()),
            _ => new ToolCallStart(slot, Snapshot()),
        });
    }

    private void Finish(int index, JsonObject item, List<AssistantMessageEvent> events)
    {
        Add(index, item, events);
        var slot = Slot(index);
        var alreadyEnded = _ended.Contains(slot);
        if (_content[slot] is TextContent previous)
        {
            var text = string.Concat((item["content"] as JsonArray ?? []).OfType<JsonObject>()
                .Select(p => Text(p, "text") ?? Text(p, "refusal") ?? ""));
            _content[slot] = new TextContent(text)
            {
                Signature = new JsonObject { ["id"] = item["id"]?.DeepClone(), ["phase"] = item["phase"]?.DeepClone() }.ToJsonString(),
            };
            if (!alreadyEnded)
            {
                if (text.StartsWith(previous.Text, StringComparison.Ordinal) && text.Length > previous.Text.Length)
                    events.Add(new TextDelta(slot, text[previous.Text.Length..], Snapshot()));
                events.Add(new TextEnd(slot, text, Snapshot()));
            }
        }
        else if (_content[slot] is ThinkingContent && PiSharp.Core.Compaction.ServerCompaction.IsItemType(Text(item, "type")))
        {
            // The provider compacted the context: keep its opaque item to send back (plan-pi-sharp-codex item 10).
            _content[slot] = new ThinkingContent("[the provider compacted the context here]", item.ToJsonString());
            if (!alreadyEnded) events.Add(new ThinkingEnd(slot, "", Snapshot()));
        }
        else if (_content[slot] is ThinkingContent previousThinking)
        {
            if (item["encrypted_content"] is null && previousThinking.Signature is { } previousSignature
                && JsonNode.Parse(previousSignature)?["encrypted_content"] is { } encrypted)
            {
                item = (JsonObject)item.DeepClone();
                item["encrypted_content"] = encrypted.DeepClone();
            }
            var summary = string.Join("\n", (item["summary"] as JsonArray ?? []).OfType<JsonObject>().Select(p => Text(p, "text") ?? ""));
            if (summary.Length == 0) summary = previousThinking.Thinking;
            _content[slot] = new ThinkingContent(summary, item.ToJsonString());
            if (!alreadyEnded) events.Add(new ThinkingEnd(slot, summary, Snapshot()));
        }
        else if (_content[slot] is ToolCallContent previousFreeform && _custom.Contains(slot))
        {
            var call = previousFreeform with { Arguments = new JsonObject { ["input"] = Text(item, "input") ?? _arguments.GetValueOrDefault(slot, "") } };
            _content[slot] = call;
            if (!alreadyEnded) events.Add(new ToolCallEnd(slot, call, Snapshot()));
        }
        else if (_content[slot] is ToolCallContent previousCall)
        {
            var raw = Text(item, "arguments") ?? _arguments.GetValueOrDefault(slot, "");
            var arguments = JsonNode.Parse(raw, documentOptions: new() { AllowDuplicateProperties = false }) as JsonObject ?? throw Protocol();
            var call = previousCall with { Arguments = arguments };
            _content[slot] = call;
            if (!alreadyEnded) events.Add(new ToolCallEnd(slot, call, Snapshot()));
        }
        _ended.Add(slot);
    }

    public void Fail(string code, string message, bool aborted = false, int? httpStatus = null,
        bool providerRequestFailed = false, string? rateLimitReason = null)
    {
        Reason = aborted ? StopReason.Aborted : StopReason.Error;
        ErrorCode = code;
        ErrorMessage = message;
        ProviderHttpStatus = httpStatus;
        ProviderRequestFailed = providerRequestFailed;
        RateLimitReason = rateLimitReason;
    }
    private int Slot(int index) => _slots.TryGetValue(index, out var slot) ? slot : throw Protocol();
    private static string? Text(JsonObject? obj, string name) => obj?[name] is JsonValue value && value.TryGetValue<string>(out var s) ? s : null;
    private static string Required(JsonObject obj, string name) => Text(obj, name) is { Length: > 0 } s ? s : throw Protocol();
    /// <summary>One line of plain text, at most <paramref name="max"/> characters, for a log line or an error text.</summary>
    internal static string? Printable(string? text, int max)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var flat = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        flat = new string([.. flat.Where(c => !char.IsControl(c))]);
        return flat.Length <= max ? flat : flat[..max] + "...";
    }

    internal static string? SafeCode(string? code) => code is { Length: > 0 and <= 80 } && code.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') ? code : null;
    private static InvalidDataException Protocol() => new("Invalid Responses stream item.");
}
