namespace Holodeck.Web.Settings;

// View is the saved settings, or the current ones when the write was refused.
public sealed record SettingsWriteResult(SettingsWriteStatus Status, SettingsView View, string Message, List<SettingsFieldError> Errors);
