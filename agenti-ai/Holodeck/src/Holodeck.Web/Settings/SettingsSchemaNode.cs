using System.Text.Json;

namespace Holodeck.Web.Settings;

// One field of a library's settings schema, in the supported profile. Properties are in display order. Default is
// a suggestion to show; it is never written into an instance's settings.
public sealed class SettingsSchemaNode
{
  public required string Name { get; init; }
  public required string Type { get; init; }
  public string? Title { get; init; }
  public string? Description { get; init; }
  public JsonElement? Default { get; init; }
  public string? Control { get; init; }
  public string? Group { get; init; }
  public List<SettingsSchemaNode>? Properties { get; init; }
  public List<string>? Required { get; init; }
  public bool? AdditionalProperties { get; init; }
  public List<string>? Enum { get; init; }
  public int? MinLength { get; init; }
  public int? MaxLength { get; init; }
  public double? Minimum { get; init; }
  public double? Maximum { get; init; }
}
