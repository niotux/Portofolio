namespace Holodeck.Web.Settings;

public sealed record SettingsRejection(string Error, List<SettingsFieldError> FieldErrors);
