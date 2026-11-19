namespace Holodeck.Web.Settings;

// The declarative presentation hints in a schema's "x-ui" object.
internal sealed record SettingsUiHints(string? Control, string? Group, List<string>? Order);
