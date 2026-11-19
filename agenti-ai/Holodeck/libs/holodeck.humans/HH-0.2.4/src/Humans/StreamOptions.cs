using System.Text.Json.Serialization;

namespace Humans;

public sealed record StreamOptions([property: JsonPropertyName("include_usage")] bool IncludeUsage);
