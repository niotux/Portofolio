using System.Text.Json;

namespace Holodeck.Web.Settings;

// Sets the form field at Path to Value, or removes the stored value when Remove is true.
public sealed record SettingsChange(string? Path, JsonElement? Value, bool Remove);
