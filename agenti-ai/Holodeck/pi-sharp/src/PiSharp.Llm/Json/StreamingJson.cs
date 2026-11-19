// Ported from pi: packages/ai/src/utils/json-parse.ts @ 71dca871b (repairJson, parseJsonWithRepair,
// parseStreamingJson) plus a hand-written replacement for the `partial-json` npm package.
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PiSharp.Llm.Json;

/// <summary>
/// Lenient JSON parsing for tool-call arguments as they stream in. Small models produce
/// truncated JSON mid-stream and occasionally malformed escapes; this never throws.
/// </summary>
public static class StreamingJson
{
    /// <summary>
    /// Parses possibly incomplete JSON into the best available object. Returns an empty object
    /// when nothing can be recovered.
    /// </summary>
    public static JsonObject ParseObject(string text)
    {
        var node = Parse(text);
        return node as JsonObject ?? new JsonObject();
    }

    /// <summary>Strict parse, then repaired parse, then partial parse, then partial of repaired.</summary>
    public static JsonNode? Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        if (TryStrict(text, out var strict)) return strict;

        var repaired = Repair(text);
        if (!ReferenceEquals(repaired, text) && TryStrict(repaired, out var strictRepaired)) return strictRepaired;

        if (TryPartial(text, out var partial)) return partial;
        if (!ReferenceEquals(repaired, text) && TryPartial(repaired, out var partialRepaired)) return partialRepaired;

        return null;
    }

    /// <summary>Strict parse with a repair fallback. Throws on failure like pi's parseJsonWithRepair.</summary>
    public static JsonNode? ParseWithRepair(string text)
    {
        if (TryStrict(text, out var strict)) return strict;
        var repaired = Repair(text);
        return JsonNode.Parse(repaired);
    }

    // JsonNode.Parse accepts duplicate keys and only throws (ArgumentException, not JsonException)
    // when the object is first read, which took down a seat in ToolCatalog.Canonical. Rejecting
    // them here sends the text to the partial parser, which keeps the last value like JSON.parse.
    private static readonly JsonDocumentOptions StrictOptions = new() { AllowDuplicateProperties = false };

    private static bool TryStrict(string text, out JsonNode? node)
    {
        try
        {
            node = JsonNode.Parse(text, documentOptions: StrictOptions);
            return true;
        }
        catch (JsonException)
        {
            node = null;
            return false;
        }
    }

    private static bool TryPartial(string text, out JsonNode? node)
    {
        try
        {
            var parser = new PartialParser(text);
            node = parser.ParseValue();
            return node is not null;
        }
        catch (FormatException)
        {
            node = null;
            return false;
        }
    }

    /// <summary>
    /// Fixes the escape problems models commonly produce inside string literals: raw control
    /// characters, invalid backslash escapes, and a trailing lone backslash. Content outside
    /// strings is untouched. Returns the same instance when no change was needed.
    /// </summary>
    public static string Repair(string text)
    {
        StringBuilder? sb = null;
        var inString = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (!inString)
            {
                if (c == '"') inString = true;
                sb?.Append(c);
                continue;
            }

            if (c == '"')
            {
                inString = false;
                sb?.Append(c);
                continue;
            }

            if (c == '\\')
            {
                if (i + 1 >= text.Length)
                {
                    sb ??= new StringBuilder(text, 0, i, text.Length + 8);
                    sb.Append("\\\\");
                    break;
                }
                var next = text[i + 1];
                if (next is '"' or '\\' or '/' or 'b' or 'f' or 'n' or 'r' or 't')
                {
                    sb?.Append(c).Append(next);
                    i++;
                    continue;
                }
                if (next == 'u' && i + 5 < text.Length && IsHex(text, i + 2, 4))
                {
                    sb?.Append(text, i, 6);
                    i += 5;
                    continue;
                }
                // Invalid escape: keep the following character literally by doubling the backslash.
                sb ??= new StringBuilder(text, 0, i, text.Length + 8);
                sb.Append("\\\\");
                continue;
            }

            if (c < 0x20)
            {
                sb ??= new StringBuilder(text, 0, i, text.Length + 8);
                sb.Append(c switch
                {
                    '\n' => "\\n",
                    '\r' => "\\r",
                    '\t' => "\\t",
                    '\b' => "\\b",
                    '\f' => "\\f",
                    _ => "\\u" + ((int)c).ToString("x4", CultureInfo.InvariantCulture),
                });
                continue;
            }

            sb?.Append(c);
        }
        return sb?.ToString() ?? text;
    }

    private static bool IsHex(string s, int start, int count)
    {
        for (var i = start; i < start + count; i++)
        {
            if (!Uri.IsHexDigit(s[i])) return false;
        }
        return true;
    }

    /// <summary>
    /// Recursive-descent parser that accepts a truncated document: an unterminated object, array
    /// or string is closed as-is, a truncated literal or number is dropped, and the last key of an
    /// object whose value has not started yet is omitted.
    /// </summary>
    private sealed class PartialParser(string text)
    {
        private int _pos;

        public JsonNode? ParseValue()
        {
            SkipWhitespace();
            if (_pos >= text.Length) return null;
            var node = Value(out _);
            return node;
        }

        // `complete` is false when the value was cut off by end of input.
        private JsonNode? Value(out bool complete)
        {
            SkipWhitespace();
            if (_pos >= text.Length)
            {
                complete = false;
                return null;
            }

            var c = text[_pos];
            switch (c)
            {
                case '{': return Object(out complete);
                case '[': return Array(out complete);
                case '"': return String(out complete);
                case 't': return Literal("true", JsonValue.Create(true), out complete);
                case 'f': return Literal("false", JsonValue.Create(false), out complete);
                case 'n': return Literal("null", null, out complete);
                default:
                    if (c == '-' || char.IsAsciiDigit(c)) return Number(out complete);
                    throw new FormatException($"Unexpected character '{c}' at {_pos}");
            }
        }

        private JsonObject Object(out bool complete)
        {
            var obj = new JsonObject();
            _pos++; // {
            while (true)
            {
                SkipWhitespace();
                if (_pos >= text.Length) { complete = false; return obj; }
                if (text[_pos] == '}') { _pos++; complete = true; return obj; }
                if (text[_pos] == ',') { _pos++; continue; }
                if (text[_pos] != '"') throw new FormatException($"Expected key at {_pos}");

                var keyNode = String(out var keyComplete);
                if (!keyComplete) { complete = false; return obj; }
                var key = keyNode.GetValue<string>();

                SkipWhitespace();
                if (_pos >= text.Length) { complete = false; return obj; }
                if (text[_pos] != ':') throw new FormatException($"Expected ':' at {_pos}");
                _pos++;

                SkipWhitespace();
                if (_pos >= text.Length) { complete = false; return obj; }

                var value = Value(out var valueComplete);
                if (value is null && !valueComplete)
                {
                    // Truncated literal/number: nothing usable for this key.
                    complete = false;
                    return obj;
                }
                obj[key] = value;
                if (!valueComplete) { complete = false; return obj; }
            }
        }

        private JsonArray Array(out bool complete)
        {
            var arr = new JsonArray();
            _pos++; // [
            while (true)
            {
                SkipWhitespace();
                if (_pos >= text.Length) { complete = false; return arr; }
                if (text[_pos] == ']') { _pos++; complete = true; return arr; }
                if (text[_pos] == ',') { _pos++; continue; }

                var value = Value(out var valueComplete);
                if (value is null && !valueComplete) { complete = false; return arr; }
                arr.Add(value);
                if (!valueComplete) { complete = false; return arr; }
            }
        }

        private JsonValue String(out bool complete)
        {
            var sb = new StringBuilder();
            _pos++; // opening quote
            while (_pos < text.Length)
            {
                var c = text[_pos];
                if (c == '"')
                {
                    _pos++;
                    complete = true;
                    return JsonValue.Create(sb.ToString());
                }
                if (c == '\\')
                {
                    if (_pos + 1 >= text.Length) break; // truncated escape
                    var next = text[_pos + 1];
                    switch (next)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (_pos + 5 >= text.Length)
                            {
                                complete = false;
                                return JsonValue.Create(sb.ToString());
                            }
                            if (!IsHex(text, _pos + 2, 4)) throw new FormatException($"Bad unicode escape at {_pos}");
                            sb.Append((char)int.Parse(text.AsSpan(_pos + 2, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            _pos += 4;
                            break;
                        default:
                            throw new FormatException($"Bad escape '\\{next}' at {_pos}");
                    }
                    _pos += 2;
                    continue;
                }
                sb.Append(c);
                _pos++;
            }
            complete = false;
            return JsonValue.Create(sb.ToString());
        }

        private JsonNode? Literal(string word, JsonNode? value, out bool complete)
        {
            var remaining = text.Length - _pos;
            if (remaining >= word.Length && string.CompareOrdinal(text, _pos, word, 0, word.Length) == 0)
            {
                _pos += word.Length;
                complete = true;
                return value;
            }
            if (string.CompareOrdinal(text, _pos, word, 0, remaining) == 0)
            {
                _pos = text.Length;
                complete = false;
                return null;
            }
            throw new FormatException($"Bad literal at {_pos}");
        }

        private JsonNode? Number(out bool complete)
        {
            var start = _pos;
            while (_pos < text.Length && (char.IsAsciiDigit(text[_pos]) || text[_pos] is '-' or '+' or '.' or 'e' or 'E')) _pos++;
            var slice = text.AsSpan(start, _pos - start);
            complete = _pos < text.Length; // more input follows, so the number was delimited
            if (!complete)
            {
                // Cut off at end of input: the number may continue. pi's partial-json drops it.
                return null;
            }
            if (long.TryParse(slice, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var l)) return JsonValue.Create(l);
            if (double.TryParse(slice, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return JsonValue.Create(d);
            throw new FormatException($"Bad number at {start}");
        }

        private void SkipWhitespace()
        {
            while (_pos < text.Length && char.IsWhiteSpace(text[_pos])) _pos++;
        }
    }
}
