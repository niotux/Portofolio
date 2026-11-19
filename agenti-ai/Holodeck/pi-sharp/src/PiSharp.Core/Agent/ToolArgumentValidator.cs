// Replaces pi: packages/ai/src/utils/validation.ts (TypeBox-based) @ 71dca871b
// Hand-written subset: required properties, primitive type checks with the same lenient
// coercions pi applies (small models send "5" for 5, "true" for true), enum membership,
// nested objects and arrays. Unknown keywords (anyOf, patterns, ranges) are accepted as-is.
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PiSharp.Core.Agent;

public static class ToolArgumentValidator
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>
    /// Returns a coerced copy of <paramref name="arguments"/> that satisfies the tool schema,
    /// or throws <see cref="ToolArgumentException"/> with a message the model can act on.
    /// </summary>
    public static JsonObject Validate(ITool tool, JsonNode? arguments)
    {
        var errors = new List<string>();
        JsonObject result;

        // The whole arguments object sent as one JSON string.
        if (arguments is JsonValue rootValue && rootValue.TryGetValue(out string? rootText) && ParseObject(rootText) is { } parsedRoot)
            arguments = parsedRoot;

        if (arguments is JsonObject obj)
        {
            result = (JsonObject)obj.DeepClone();
            CoerceObject(result, tool.Parameters, "", errors);
        }
        else
        {
            result = new JsonObject();
            errors.Add("root: Expected object");
        }

        if (errors.Count == 0) return result;

        var received = arguments?.ToJsonString(Indented) ?? "null";
        var list = string.Join("\n", errors.Select(e => "  - " + e));
        throw new ToolArgumentException($"Validation failed for tool \"{tool.Name}\":\n{list}\n\nReceived arguments:\n{received}");
    }

    private static void CoerceObject(JsonObject value, JsonObject schema, string path, List<string> errors)
    {
        var required = new HashSet<string>(StringComparer.Ordinal);
        if (schema["required"] is JsonArray req)
        {
            foreach (var r in req)
            {
                if (r is JsonValue v && v.TryGetValue(out string? name)) required.Add(name);
            }
        }

        var props = schema["properties"] as JsonObject;
        if (props is not null)
        {
            RenameAliases(value, props);
            FoldIntoPayload(value, props);
            foreach (var (key, propSchemaNode) in props)
            {
                var propPath = path.Length == 0 ? key : $"{path}.{key}";
                var propSchema = propSchemaNode as JsonObject ?? new JsonObject();
                var present = value.TryGetPropertyValue(key, out var current);

                if (!present || (current is null && !required.Contains(key)))
                {
                    // pi's normalizeOptionalNulls: an explicit null for an optional property means "absent".
                    if (present) value.Remove(key);
                    if (required.Contains(key)) errors.Add($"{propPath}: Expected required property");
                    continue;
                }

                if (current is null && required.Contains(key))
                {
                    // An explicit null for a REQUIRED property is "no value", even when the schema's type union
                    // includes null (e.g. ["string","null"] generated for nullable record fields). Coercing it to
                    // a default ("" / 0 / false) would pass client validation and fail later, opaquely, at the
                    // server. Reject it here with an actionable message.
                    value.Remove(key);
                    errors.Add($"{propPath}: Required property is missing");
                    continue;
                }

                var coerced = CoerceValue(current, propSchema, propPath, errors);
                if (!ReferenceEquals(coerced, current))
                {
                    value.Remove(key);
                    value[key] = coerced;
                }
            }
        }

        foreach (var name in required)
        {
            if (props is not null && props.ContainsKey(name)) continue;
            if (!value.ContainsKey(name))
            {
                errors.Add($"{(path.Length == 0 ? name : $"{path}.{name}")}: Expected required property");
            }
        }
    }

    // Names models use for a property the schema spells differently (seat session logs, 48 h to 2026-09-27: channel
    // for room on chat_post, slug/id/status/assignTo on kb_transition). An alias applies only when the schema has
    // the target, does not have the alias itself, and the call did not also send the target.
    private static readonly (string From, string To)[] Aliases =
    [
        ("channel", "room"), ("slug", "work_item"), ("id", "work_item"), ("wi", "work_item"), ("item", "work_item"),
        ("status", "to_status"), ("to", "to_status"), ("state", "to_status"), ("assignee", "assign_to"), ("text", "body"),
        ("message", "body"), ("content", "body"),
    ];

    private static void RenameAliases(JsonObject value, JsonObject props)
    {
        foreach (var key in value.Select(p => p.Key).ToList())
        {
            if (props.ContainsKey(key)) continue;
            var target = SnakeCase(key);
            if (!props.ContainsKey(target) || value.ContainsKey(target))
                target = Aliases.FirstOrDefault(a => a.From == key && props.ContainsKey(a.To) && !value.ContainsKey(a.To)).To;
            if (target is null || value.ContainsKey(target)) continue;
            var node = value[key];
            value.Remove(key);
            value[target] = node;
        }
    }

    private static string SnakeCase(string key)
    {
        var sb = new System.Text.StringBuilder(key.Length + 4);
        foreach (var c in key)
        {
            if (char.IsUpper(c)) { if (sb.Length > 0) sb.Append('_'); sb.Append(char.ToLowerInvariant(c)); }
            else sb.Append(c);
        }
        return sb.ToString();
    }

    // A tool whose op names a payload object (kb_write op=doc takes doc={entity,key,title,body}) called flat, with
    // the payload's fields at the top level (48 kb_write failures in 48 h to 2026-09-27): the fields move into the
    // payload. A top-level field moves only when the payload declares it and the top level either does not or got
    // a value that is not an object (entity="wi-12" is the doc's entity string, not op=entity's payload).
    private static void FoldIntoPayload(JsonObject value, JsonObject props)
    {
        if (value["op"] is not JsonValue opValue || !opValue.TryGetValue(out string? op) || value.ContainsKey(op)) return;
        if (props[op] is not JsonObject payloadSchema || payloadSchema["properties"] is not JsonObject payloadProps) return;
        var payload = new JsonObject();
        foreach (var key in value.Select(p => p.Key).ToList())
        {
            if (key == "op" || !payloadProps.ContainsKey(key)) continue;
            if (props.ContainsKey(key) && value[key] is JsonObject) continue;
            var node = value[key];
            value.Remove(key);
            payload[key] = node;
        }
        if (payload.Count > 0) value[op] = payload;
    }

    private static JsonNode? CoerceValue(JsonNode? value, JsonObject schema, string path, List<string> errors)
    {
        var types = GetTypes(schema);

        if (types.Count > 0)
        {
            // Try each declared type in order; the first coercion that matches wins.
            JsonNode? coerced = value;
            var matched = false;
            foreach (var type in types)
            {
                coerced = CoercePrimitive(value, type, schema);
                if (Matches(coerced, type))
                {
                    matched = true;
                    break;
                }
            }
            if (!matched)
            {
                errors.Add($"{path}: Expected {string.Join(" | ", types)}, received {Describe(value)}");
                return value;
            }
            value = coerced;
        }

        if (schema["enum"] is JsonArray allowed && value is not null)
        {
            var hit = allowed.Any(a => a is not null && JsonNode.DeepEquals(a, value));
            if (!hit)
            {
                var list = string.Join(", ", allowed.Select(a => a?.ToJsonString() ?? "null"));
                errors.Add($"{path}: Expected one of {list}, received {value.ToJsonString()}");
                return value;
            }
        }

        switch (value)
        {
            case JsonObject obj when schema["properties"] is not null || schema["required"] is not null:
                CoerceObject(obj, schema, path, errors);
                break;
            case JsonArray arr when schema["items"] is JsonObject itemSchema:
                for (var i = 0; i < arr.Count; i++)
                {
                    var item = arr[i];
                    var coercedItem = CoerceValue(item, itemSchema, $"{path}.{i}", errors);
                    if (!ReferenceEquals(coercedItem, item))
                    {
                        arr[i] = coercedItem;
                    }
                }
                break;
        }

        return value;
    }

    private static List<string> GetTypes(JsonObject schema)
    {
        var list = new List<string>();
        switch (schema["type"])
        {
            case JsonValue single when single.TryGetValue(out string? s):
                list.Add(s);
                break;
            case JsonArray many:
                foreach (var t in many)
                {
                    if (t is JsonValue v && v.TryGetValue(out string? s2)) list.Add(s2);
                }
                break;
        }
        return list;
    }

    /// <summary>
    /// Mirror of pi's coercePrimitiveByType, plus the shapes that can only mean one thing ("5.0" for an
    /// integer, "True" for a boolean, one object where a list of objects is expected). Returns the input
    /// when no coercion applies.
    /// </summary>
    private static JsonNode? CoercePrimitive(JsonNode? value, string type, JsonObject schema)
    {
        var kind = value?.GetValueKind() ?? JsonValueKind.Null;
        switch (type)
        {
            case "number":
                if (kind == JsonValueKind.Null) return JsonValue.Create(0);
                if (kind == JsonValueKind.String && double.TryParse(value!.GetValue<string>().Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && double.IsFinite(d))
                    return JsonValue.Create(d);
                if (kind is JsonValueKind.True or JsonValueKind.False) return JsonValue.Create(kind == JsonValueKind.True ? 1 : 0);
                return value;
            case "integer":
                if (kind == JsonValueKind.Null) return JsonValue.Create(0);
                if (kind == JsonValueKind.String && long.TryParse(value!.GetValue<string>().Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var l))
                    return JsonValue.Create(l);
                // "5.0" is 5; "5.5" stays a string and fails, as 5.5 fails.
                if (kind == JsonValueKind.String
                    && double.TryParse(value!.GetValue<string>().Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var whole)
                    && double.IsFinite(whole) && Math.Floor(whole) == whole && Math.Abs(whole) <= long.MaxValue)
                    return JsonValue.Create((long)whole);
                if (kind is JsonValueKind.True or JsonValueKind.False) return JsonValue.Create(kind == JsonValueKind.True ? 1 : 0);
                return value;
            case "boolean":
                if (kind == JsonValueKind.Null) return JsonValue.Create(false);
                if (kind == JsonValueKind.String)
                {
                    var s = value!.GetValue<string>().Trim();
                    if (s.Equals("true", StringComparison.OrdinalIgnoreCase) || s == "1") return JsonValue.Create(true);
                    if (s.Equals("false", StringComparison.OrdinalIgnoreCase) || s == "0") return JsonValue.Create(false);
                }
                if (kind == JsonValueKind.Number && TryGetDouble(value!, out var n))
                {
                    if (n == 1) return JsonValue.Create(true);
                    if (n == 0) return JsonValue.Create(false);
                }
                return value;
            case "string":
                if (kind == JsonValueKind.Null) return JsonValue.Create("");
                if (kind == JsonValueKind.Number) return JsonValue.Create(value!.ToJsonString());
                if (kind is JsonValueKind.True or JsonValueKind.False) return JsonValue.Create(kind == JsonValueKind.True ? "true" : "false");
                return value;
            case "null":
                if (kind == JsonValueKind.String && value!.GetValue<string>().Length == 0) return null;
                if (kind == JsonValueKind.Number && TryGetDouble(value!, out var z) && z == 0) return null;
                if (kind == JsonValueKind.False) return null;
                return value;
            case "object":
                // Double-serialized JSON: the model sent an object as a string (e.g. KB server
                // declares entity as ["object","null"] and the model passes "{\"slug\":\"wi-12\"}").
                if (kind == JsonValueKind.String && ParseObject(value!.GetValue<string>()) is { } parsedObject) return parsedObject;
                return value;
            case "array":
                if (kind == JsonValueKind.String)
                {
                    var s = value!.GetValue<string>().Trim();
                    if (s.StartsWith('[') && s.EndsWith(']') && s.Length >= 2)
                    {
                        try { return JsonNode.Parse(s) as JsonArray; }
                        catch (JsonException) { }
                    }
                    // A single value where a list is expected (chat_post about="wi-12": 87 failures in 48 h to
                    // 2026-09-27), or a comma-separated one: the list of those values.
                    if (s.Length > 0)
                        return new JsonArray([.. s.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(p => (JsonNode)JsonValue.Create(p)!)]);
                }
                if (kind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False) return new JsonArray(value!.DeepClone());
                // One object where a list of objects is expected (edits={oldText,newText}): the list of it.
                // Only when the items are objects or unconstrained; an object is never a string or a number.
                if (kind == JsonValueKind.Object && (schema["items"] is not JsonObject items || GetTypes(items) is var itemTypes && (itemTypes.Count == 0 || itemTypes.Contains("object"))))
                    return new JsonArray(value!.DeepClone());
                return value;
            default:
                return value;
        }
    }

    /// <summary>A double-serialized JSON object, or null when the text is not one.</summary>
    private static JsonObject? ParseObject(string text)
    {
        var s = text.Trim();
        if (s.Length < 2 || !s.StartsWith('{') || !s.EndsWith('}')) return null;
        try { return JsonNode.Parse(s) as JsonObject; }
        catch (JsonException) { return null; }
    }

    private static bool TryGetDouble(JsonNode node, out double result)
    {
        if (node is JsonValue v && v.TryGetValue(out result)) return true;
        if (node is JsonValue v2 && v2.TryGetValue(out long l)) { result = l; return true; }
        if (node is JsonValue v3 && v3.TryGetValue(out int i)) { result = i; return true; }
        result = 0;
        return false;
    }

    private static bool Matches(JsonNode? value, string type)
    {
        var kind = value?.GetValueKind() ?? JsonValueKind.Null;
        return type switch
        {
            "number" => kind == JsonValueKind.Number,
            "integer" => kind == JsonValueKind.Number && TryGetDouble(value!, out var d) && Math.Floor(d) == d,
            "boolean" => kind is JsonValueKind.True or JsonValueKind.False,
            "string" => kind == JsonValueKind.String,
            "null" => kind == JsonValueKind.Null,
            "array" => kind == JsonValueKind.Array,
            "object" => kind == JsonValueKind.Object,
            _ => true,
        };
    }

    private static string Describe(JsonNode? value) => value is null ? "null" : value.GetValueKind() switch
    {
        JsonValueKind.Object => "object",
        JsonValueKind.Array => "array",
        JsonValueKind.String => "string",
        JsonValueKind.Number => "number",
        JsonValueKind.True or JsonValueKind.False => "boolean",
        _ => "null",
    };
}
