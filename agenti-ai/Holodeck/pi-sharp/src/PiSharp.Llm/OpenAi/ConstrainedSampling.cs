// Ported from pi: packages/ai/src/api/constrained-sampling.ts @ 71dca871b
// Grammar-tool variants (openai_lark / openai_regex) are dropped: docs/02 folds
// supportsOpenAIGrammarTools into json_schema / strict mode.
using System.Text.Json.Nodes;
using PiSharp.Core.Agent;

namespace PiSharp.Llm.OpenAi;

/// <summary>
/// Builds the strict JSON-schema subset that OpenAI-compatible servers (and llama.cpp
/// <c>response_format.json_schema</c>) accept for constrained tool-argument sampling.
/// </summary>
public static class ConstrainedSampling
{
    private static readonly string[] UnsupportedKeys =
    [
        "$ref", "$defs", "definitions", "allOf", "oneOf", "patternProperties",
        "dependentSchemas", "dependencies", "unevaluatedProperties", "propertyNames",
        "contains", "prefixItems", "not", "if", "then", "else",
    ];

    /// <summary>
    /// Convert a tool parameter schema to the strict subset (every property required,
    /// <c>additionalProperties: false</c>, optional fields widened with <c>null</c>).
    /// </summary>
    /// <exception cref="UnsupportedStrictJsonSchemaException">The schema uses a construct strict mode rejects.</exception>
    public static JsonObject MakeStrictJsonSchema(JsonNode? schema)
    {
        var cloned = schema?.DeepClone();
        if (cloned is not JsonObject obj)
        {
            throw new UnsupportedStrictJsonSchemaException("root schema must have type object");
        }
        MakeNodeStrict(obj);
        if (!HasType(obj, "object"))
        {
            throw new UnsupportedStrictJsonSchemaException("root schema must have type object");
        }
        return obj;
    }

    /// <summary>Like <see cref="MakeStrictJsonSchema"/> but returns null instead of throwing on unsupported schemas.</summary>
    public static JsonObject? TryMakeStrictJsonSchema(JsonNode? schema)
    {
        try
        {
            return MakeStrictJsonSchema(schema);
        }
        catch (UnsupportedStrictJsonSchemaException)
        {
            return null;
        }
    }

    /// <summary>
    /// Parameters to send on the wire: the strict rewrite when <paramref name="strict"/> is true,
    /// otherwise a clone of the original schema.
    /// </summary>
    public static JsonObject GetJsonSchemaToolParameters(ITool tool, bool strict)
    {
        if (strict) return MakeStrictJsonSchema(tool.Parameters);
        return (JsonObject)tool.Parameters.DeepClone();
    }

    /// <summary>
    /// Whether this tool should be advertised with <c>strict: true</c>.
    /// Returns false when the schema cannot be rewritten and strict is not required.
    /// </summary>
    public static bool ResolveStrictSampling(ITool tool, bool supportsStrictMode)
    {
        if (!supportsStrictMode) return false;
        return TryMakeStrictJsonSchema(tool.Parameters) is not null;
    }

    /// <summary>
    /// llama.cpp / OpenAI <c>response_format</c> that constrains the whole completion to
    /// <paramref name="schema"/>. Used when a single tool is forced.
    /// </summary>
    public static JsonObject JsonSchemaResponseFormat(string name, JsonObject schema, bool strict = true) => new()
    {
        ["type"] = "json_schema",
        ["json_schema"] = new JsonObject
        {
            ["name"] = SanitizeSchemaName(name),
            ["strict"] = strict,
            ["schema"] = schema.DeepClone(),
        },
    };

    /// <summary>
    /// Schema for a tool-call JSON object <c>{name, arguments}</c> so a model that cannot emit
    /// native <c>tool_calls</c> is still constrained. The fallback parser then recovers the call.
    /// </summary>
    public static JsonObject ToolCallEnvelopeSchema(ITool tool, bool strictArguments)
    {
        var arguments = strictArguments
            ? TryMakeStrictJsonSchema(tool.Parameters) ?? (JsonObject)tool.Parameters.DeepClone()
            : (JsonObject)tool.Parameters.DeepClone();
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["name"] = new JsonObject
                {
                    ["type"] = "string",
                    ["enum"] = new JsonArray { (JsonNode)JsonValue.Create(tool.Name)! },
                },
                ["arguments"] = arguments,
            },
            ["required"] = new JsonArray
            {
                (JsonNode)JsonValue.Create("name")!,
                (JsonNode)JsonValue.Create("arguments")!,
            },
            ["additionalProperties"] = false,
        };
    }

    private static string SanitizeSchemaName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "schema";
        var chars = name.Select(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' ? c : '_').ToArray();
        if (!char.IsAsciiLetter(chars[0]) && chars[0] != '_') chars[0] = 's';
        return new string(chars);
    }

    private static void MakeNodeStrict(JsonNode? schema)
    {
        if (schema is not JsonObject obj)
        {
            throw new UnsupportedStrictJsonSchemaException("boolean schemas are unsupported");
        }

        foreach (var key in UnsupportedKeys)
        {
            if (obj[key] is not null)
            {
                throw new UnsupportedStrictJsonSchemaException($"{key} schemas are unsupported");
            }
        }

        if (obj["anyOf"] is { } anyOfNode)
        {
            if (anyOfNode is not JsonArray { Count: > 0 } anyOf)
            {
                throw new UnsupportedStrictJsonSchemaException("anyOf must contain at least one schema");
            }
            foreach (var variant in anyOf)
            {
                if (IsStructuredSchema(variant))
                {
                    throw new UnsupportedStrictJsonSchemaException("object and array unions are unsupported");
                }
                MakeNodeStrict(variant);
            }
        }

        if (obj["items"] is { } items)
        {
            if (items is JsonArray)
            {
                throw new UnsupportedStrictJsonSchemaException("tuple schemas are unsupported");
            }
            MakeNodeStrict(items);
        }

        // pi: `schema.type === "object"` — a string type, not a union.
        var isObjectSchema = obj["type"] is JsonValue v && v.TryGetValue(out string? typeName) && typeName == "object";

        if (obj["properties"] is not null && !isObjectSchema)
        {
            throw new UnsupportedStrictJsonSchemaException("properties require type object");
        }
        if (!isObjectSchema) return;

        if (obj["additionalProperties"] is not null
            && !(obj["additionalProperties"] is JsonValue fv && fv.TryGetValue(out bool f) && f == false))
        {
            throw new UnsupportedStrictJsonSchemaException("schema-valued or true additionalProperties is unsupported");
        }

        if (obj["properties"] is not null and not JsonObject)
        {
            throw new UnsupportedStrictJsonSchemaException("object properties must be a schema map");
        }

        if (obj["required"] is { } requiredNode)
        {
            if (requiredNode is not JsonArray requiredArr || requiredArr.Any(k => k is not JsonValue rv || !rv.TryGetValue(out string? _)))
            {
                throw new UnsupportedStrictJsonSchemaException("object required must be a string array");
            }
        }

        var properties = obj["properties"] as JsonObject ?? new JsonObject();
        if (obj["properties"] is null) obj["properties"] = properties;
        var propertyNames = properties.Select(p => p.Key).ToList();
        var required = new HashSet<string>(StringComparer.Ordinal);
        if (obj["required"] is JsonArray existing)
        {
            foreach (var item in existing)
            {
                if (item is JsonValue rv && rv.TryGetValue(out string? name)) required.Add(name);
            }
        }
        if (required.Any(key => !propertyNames.Contains(key)))
        {
            throw new UnsupportedStrictJsonSchemaException("required contains an unknown property");
        }

        foreach (var (key, property) in properties.ToList())
        {
            MakeNodeStrict(property);
            if (!required.Contains(key) && !SchemaAllowsNull(property))
            {
                var anyOf = new JsonArray();
                anyOf.Add(property?.DeepClone());
                anyOf.Add((JsonNode)new JsonObject { ["type"] = "null" });
                properties[key] = new JsonObject
                {
                    ["anyOf"] = anyOf,
                };
            }
        }

        var requiredArray = new JsonArray();
        foreach (var name in propertyNames) requiredArray.Add((JsonNode)JsonValue.Create(name)!);
        obj["required"] = requiredArray;
        obj["additionalProperties"] = false;
    }

    private static bool SchemaAllowsNull(JsonNode? schema)
    {
        if (schema is not JsonObject obj) return false;
        if (HasType(obj, "null")) return true;
        if (obj["const"] is JsonValue c && c.GetValueKind() == System.Text.Json.JsonValueKind.Null) return true;
        if (obj["enum"] is JsonArray en && en.Any(e => e is null || (e is JsonValue ev && ev.GetValueKind() == System.Text.Json.JsonValueKind.Null)))
        {
            return true;
        }
        if (obj["anyOf"] is JsonArray anyOf) return anyOf.Any(SchemaAllowsNull);
        return false;
    }

    private static bool IsStructuredSchema(JsonNode? schema)
    {
        if (schema is not JsonObject obj) return false;
        return HasType(obj, "object") || HasType(obj, "array")
            || obj["properties"] is not null || obj["items"] is not null;
    }

    private static bool HasType(JsonObject schema, string type)
    {
        return schema["type"] switch
        {
            JsonValue v when v.TryGetValue(out string? s) => s == type,
            JsonArray arr => arr.Any(t => t is JsonValue v && v.TryGetValue(out string? s) && s == type),
            _ => false,
        };
    }
}

public sealed class UnsupportedStrictJsonSchemaException(string message) : Exception(message);
