using Holodeck.Web.Settings;

namespace Holodeck.Web.Management;

public sealed record ManagementValidationError(string Error, List<SettingsFieldError> FieldErrors);
