namespace Holodeck.Web.Settings;

// Path is a JSON Pointer to the value, "" for the whole document.
public sealed record SettingsFieldError(string Path, string Message);
