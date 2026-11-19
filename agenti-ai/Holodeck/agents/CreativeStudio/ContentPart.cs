using System.Text.Json.Serialization;

namespace CreativeStudio;

public sealed record ContentPart(string Type, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Text, [property: JsonPropertyName("image_url"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ContentImage? ImageUrl);
