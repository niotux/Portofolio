namespace Holodeck.Web.Settings;

// Current is what is stored now, so an editor can show it beside its own unsaved values.
public sealed record SettingsConflict(string Error, SettingsView Current);
