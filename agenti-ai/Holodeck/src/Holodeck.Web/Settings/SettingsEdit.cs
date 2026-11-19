namespace Holodeck.Web.Settings;

// The text a write would store, or why it cannot be built.
internal sealed record SettingsEdit(string? Text, List<SettingsFieldError> Errors);
