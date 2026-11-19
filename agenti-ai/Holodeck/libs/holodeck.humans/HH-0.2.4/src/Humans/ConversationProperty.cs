namespace Humans;

public sealed record ConversationProperty([property: System.Text.Json.Serialization.JsonPropertyName("type")] string Type, [property: System.Text.Json.Serialization.JsonPropertyName("enum")] List<string> Enum);
