// Ported from pi: packages/ai/src/types.ts (content blocks) @ 71dca871b
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace PiSharp.Core.Messages;

/// <summary>A block inside a message. Discriminated by <c>type</c> when serialized.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(TextContent), "text")]
[JsonDerivedType(typeof(ThinkingContent), "thinking")]
[JsonDerivedType(typeof(ImageContent), "image")]
[JsonDerivedType(typeof(ToolCallContent), "toolCall")]
public abstract record ContentPart;

/// <summary>Plain text produced by the user or the model.</summary>
public sealed record TextContent(string Text) : ContentPart
{
    /// <summary>Responses message item ID and phase, for provider-specific history replay.</summary>
    public string? Signature { get; init; }
}

/// <summary>Model reasoning. <see cref="Signature"/> carries provider replay data when present.</summary>
public sealed record ThinkingContent(string Thinking, string? Signature = null, bool Redacted = false) : ContentPart;

/// <summary>An image as base64 data plus MIME type (image/png, image/jpeg, image/gif, image/webp).</summary>
public sealed record ImageContent(string Data, string MimeType) : ContentPart;

/// <summary>A tool invocation requested by the model. Arguments are the parsed JSON object.</summary>
public sealed record ToolCallContent(string Id, string Name, JsonObject Arguments) : ContentPart;
