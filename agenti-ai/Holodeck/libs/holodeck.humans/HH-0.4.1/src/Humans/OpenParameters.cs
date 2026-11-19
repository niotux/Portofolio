namespace Humans;

// The open control's argument schema: one conversation, chosen from those the body has.
public sealed record OpenParameters([property: System.Text.Json.Serialization.JsonPropertyName("type")] string Type, [property: System.Text.Json.Serialization.JsonPropertyName("properties")] OpenProperties Properties, [property: System.Text.Json.Serialization.JsonPropertyName("required")] List<string> Required);
