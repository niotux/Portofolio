// Argument records for the built-in tools plus their source-generated JSON metadata.
// Schemas mirror pi: packages/coding-agent/src/core/tools/*.ts @ 71dca871b
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace PiSharp.Tools;

public sealed record ReadArgs(string Path, [property: JsonConverter(typeof(LenientIntConverter))] int? Offset = null, [property: JsonConverter(typeof(LenientIntConverter))] int? Limit = null, bool Refresh = false);

public sealed record WriteArgs(string Path, string Content);

/// <summary>One targeted replacement inside an <see cref="EditArgs"/> call.</summary>
public sealed record EditSpec(string OldText, string NewText);

public sealed record EditArgs(string Path, List<EditSpec> Edits, bool? Fuzzy = null);

public sealed record PatchArgs(string Input);

public sealed record BashArgs(string Command, double? Timeout = null, double? Yield = null, bool? Tty = null,
    [property: JsonPropertyName("max_output_tokens")] double? MaxOutputTokens = null);

public sealed record LsArgs(string? Path = null, [property: JsonConverter(typeof(LenientIntConverter))] int? Limit = null);

public sealed record GrepArgs(
    string Pattern,
    string? Path = null,
    string? Glob = null,
    bool? IgnoreCase = null,
    bool? Literal = null,
    [property: JsonConverter(typeof(LenientIntConverter))] int? Context = null,
    [property: JsonConverter(typeof(LenientIntConverter))] int? Limit = null,
    string? OutputMode = null,
    [property: JsonConverter(typeof(LenientIntConverter))] int? Offset = null,
    string? Type = null,
    bool? Multiline = null);

public sealed record FindArgs(string Pattern, string? Path = null, [property: JsonConverter(typeof(LenientIntConverter))] int? Limit = null);

public sealed record RewindArgs(string To, bool? DryRun = null);

public sealed record CheckpointListArgs([property: JsonConverter(typeof(LenientIntConverter))] int? Limit = null);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    NumberHandling = JsonNumberHandling.AllowReadingFromString)]
[JsonSerializable(typeof(ReadArgs))]
[JsonSerializable(typeof(WriteArgs))]
[JsonSerializable(typeof(EditArgs))]
[JsonSerializable(typeof(EditSpec))]
[JsonSerializable(typeof(List<EditSpec>))]
[JsonSerializable(typeof(PatchArgs))]
[JsonSerializable(typeof(BashArgs))]
[JsonSerializable(typeof(LsArgs))]
[JsonSerializable(typeof(GrepArgs))]
[JsonSerializable(typeof(FindArgs))]
[JsonSerializable(typeof(RewindArgs))]
[JsonSerializable(typeof(CheckpointListArgs))]
public sealed partial class ToolsJsonContext : JsonSerializerContext;

/// <summary>
/// The schemas declare counts as "number" (as pi does), so a model may send 50.0 or 2.5 where the
/// record wants an int. Accept any finite number or numeric string and truncate it toward zero.
/// </summary>
public sealed class LenientIntConverter : JsonConverter<int?>
{
    public override bool HandleNull => true;

    public override int? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        double value;
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.Number:
                if (reader.TryGetInt32(out var i)) return i;
                value = reader.GetDouble();
                break;
            case JsonTokenType.String:
                var s = reader.GetString()?.Trim();
                if (string.IsNullOrEmpty(s)) return null;
                if (!double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value))
                    throw new JsonException($"Expected a number, received \"{s}\"");
                break;
            default:
                throw new JsonException($"Expected a number, received {reader.TokenType}");
        }
        if (!double.IsFinite(value)) throw new JsonException("Expected a finite number");
        return (int)Math.Clamp(Math.Truncate(value), int.MinValue, int.MaxValue);
    }

    public override void Write(Utf8JsonWriter writer, int? value, JsonSerializerOptions options)
    {
        if (value is { } v) writer.WriteNumberValue(v);
        else writer.WriteNullValue();
    }
}

internal static class Schema
{
    /// <summary>Parse a JSON Schema literal written in source.</summary>
    internal static JsonObject Parse(string json) =>
        JsonNode.Parse(json) as JsonObject ?? throw new InvalidOperationException("Schema must be a JSON object");
}
