using System.Text.Json.Serialization;

namespace CreativeStudio;

public sealed record ChatStreamOptions([property: JsonPropertyName("include_usage")] bool IncludeUsage);
