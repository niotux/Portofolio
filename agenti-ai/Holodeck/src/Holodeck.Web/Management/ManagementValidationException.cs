using Holodeck.Web.Settings;

namespace Holodeck.Web.Management;

public sealed class ManagementValidationException(List<SettingsFieldError> errors) : ArgumentException("Some action fields are invalid.")
{
  public List<SettingsFieldError> Errors { get; } = errors;
}
