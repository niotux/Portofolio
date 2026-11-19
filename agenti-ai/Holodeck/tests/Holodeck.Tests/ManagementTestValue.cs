using System.Text.Json.Serialization;

namespace Holodeck.Tests;

internal sealed record ManagementTestValue([property: JsonPropertyName("text")] string Text);
