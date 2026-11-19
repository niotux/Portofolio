// New in pi-sharp (docs/01 §7, docs/03 Phase 7): small models often emit tool calls as
// text (`<tool_call>` XML, Qwen `<function=…>` tags, or a JSON blob) instead of native
// `tool_calls` deltas. Recover those into ToolCallContent so the loop can execute them.
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using PiSharp.Core.Messages;

namespace PiSharp.Core.Agent;

/// <summary>
/// Recovers tool calls from assistant text when the server returned plain content.
/// Conservative: only tagged blocks, Qwen function tags, or a whole-message JSON object
/// whose <c>name</c> matches a known tool. Prose that happens to contain JSON is left alone.
/// </summary>
public static partial class TextToolCallParser
{
    [GeneratedRegex(@"<tool_call>(.*?)</tool_call>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ToolCallBlock();

    [GeneratedRegex(@"<function\s*=\s*([^>]+)>(.*?)</function>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex FunctionBlock();

    [GeneratedRegex(@"<parameter\s*=\s*([^>]+)>(.*?)</parameter>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ParameterBlock();

    [GeneratedRegex(@"^```(?:json)?\s*\n?(.*?)\n?```$", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex FencedJson();

    /// <summary>
    /// Returns a copy of <paramref name="message"/> with recovered tool calls, or null
    /// when nothing could be parsed. Native tool calls already on the message win.
    /// </summary>
    public static AssistantMessage? TryRepair(AssistantMessage message, IReadOnlyList<ITool>? tools = null)
    {
        if (message.ToolCalls.Any()) return null;
        var text = message.Text;
        if (string.IsNullOrWhiteSpace(text)) return null;

        var known = tools is { Count: > 0 } ? tools.Select(t => t.Name) : null;
        var calls = Parse(text, known);
        if (calls.Count == 0) return null;

        var leftover = StripMarkup(text);
        var content = new List<ContentPart>();
        foreach (var part in message.Content)
        {
            if (part is TextContent) continue;
            content.Add(part);
        }
        if (!string.IsNullOrWhiteSpace(leftover)) content.Add(new TextContent(leftover.Trim()));
        content.AddRange(calls);

        return message with
        {
            Content = content,
            StopReason = StopReason.ToolUse,
        };
    }

    public static IReadOnlyList<ToolCallContent> Parse(string text, IEnumerable<string>? knownNames = null)
    {
        IReadOnlySet<string>? known = knownNames as IReadOnlySet<string>
            ?? (knownNames is null ? null : knownNames.ToHashSet(StringComparer.Ordinal));
        var calls = new List<ToolCallContent>();

        foreach (Match block in ToolCallBlock().Matches(text))
        {
            foreach (var call in ParseBlock(block.Groups[1].Value.Trim(), known, calls.Count))
            {
                calls.Add(call);
            }
        }

        if (calls.Count == 0)
        {
            foreach (var call in ParseFunctionTags(text, known, 0)) calls.Add(call);
        }

        if (calls.Count == 0)
        {
            var whole = UnwrapFence(text.Trim());
            if (TryParseJsonCall(whole, known) is { } jsonCall) calls.Add(jsonCall);
        }

        return calls;
    }

    private static IEnumerable<ToolCallContent> ParseBlock(string inner, IReadOnlySet<string>? known, int offset)
    {
        var fromTags = ParseFunctionTags(inner, known, offset).ToList();
        if (fromTags.Count > 0) return fromTags;

        var trimmed = UnwrapFence(inner.Trim());
        if (TryParseJsonCall(trimmed, known) is { } json) return [json];

        // Qwen / Hermes: first line is the tool name, rest is JSON arguments.
        var nl = trimmed.IndexOfAny(['\n', '\r']);
        if (nl > 0)
        {
            var name = trimmed[..nl].Trim();
            var rest = UnwrapFence(trimmed[(nl + 1)..].Trim());
            if (IsName(name, known) && TryParseObject(rest) is { } args)
            {
                return [new ToolCallContent(CallId(offset), name, args)];
            }
        }

        return [];
    }

    private static IEnumerable<ToolCallContent> ParseFunctionTags(string text, IReadOnlySet<string>? known, int offset)
    {
        var index = offset;
        foreach (Match fn in FunctionBlock().Matches(text))
        {
            var name = fn.Groups[1].Value.Trim();
            if (!IsName(name, known)) continue;
            var args = new JsonObject();
            foreach (Match p in ParameterBlock().Matches(fn.Groups[2].Value))
            {
                args[p.Groups[1].Value.Trim()] = CoerceParameter(p.Groups[2].Value.Trim());
            }
            yield return new ToolCallContent(CallId(index), name, args);
            index++;
        }
    }

    private static ToolCallContent? TryParseJsonCall(string text, IReadOnlySet<string>? known)
    {
        if (TryParseObject(text) is not { } obj) return null;

        string? name = null;
        if (obj["name"] is JsonValue nv && nv.TryGetValue(out string? n)) name = n;
        else if (obj["tool"] is JsonValue tv && tv.TryGetValue(out string? t)) name = t;
        if (string.IsNullOrEmpty(name) || !IsName(name, known)) return null;

        JsonObject args;
        if (obj["arguments"] is JsonObject a) args = (JsonObject)a.DeepClone();
        else if (obj["parameters"] is JsonObject p) args = (JsonObject)p.DeepClone();
        else if (obj["arguments"] is JsonValue av && av.TryGetValue(out string? as_) && TryParseObject(as_) is { } parsedA)
            args = parsedA;
        else if (obj["parameters"] is JsonValue pv && pv.TryGetValue(out string? ps) && TryParseObject(ps) is { } parsedP)
            args = parsedP;
        else args = new JsonObject();

        return new ToolCallContent(CallId(0), name, args);
    }

    private static JsonObject? TryParseObject(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            return JsonNode.Parse(text) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static JsonNode CoerceParameter(string raw)
    {
        if (TryParseObject(raw) is { } obj) return obj;
        try
        {
            if (JsonNode.Parse(raw) is { } node)
            {
                var kind = node.GetValueKind();
                if (kind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False
                    or JsonValueKind.Null or JsonValueKind.Array)
                {
                    return node;
                }
            }
        }
        catch (JsonException)
        {
            // fall through to scalar coercions
        }

        if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)) return JsonValue.Create(l);
        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return JsonValue.Create(d);
        if (raw == "true") return JsonValue.Create(true);
        if (raw == "false") return JsonValue.Create(false);
        return JsonValue.Create(raw);
    }

    private static bool IsName(string name, IReadOnlySet<string>? known) =>
        name.Length > 0 && (known is null || known.Contains(name));

    private static string UnwrapFence(string text)
    {
        var m = FencedJson().Match(text.Trim());
        return m.Success ? m.Groups[1].Value.Trim() : text.Trim();
    }

    private static string StripMarkup(string text)
    {
        var without = ToolCallBlock().Replace(text, "");
        without = FunctionBlock().Replace(without, "");
        return without;
    }

    private static string CallId(int index) => $"call_text_{index}";
}
