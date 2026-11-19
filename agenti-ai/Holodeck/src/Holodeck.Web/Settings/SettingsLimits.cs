namespace Holodeck.Web.Settings;

// Bounds on what a library's settings schema may declare and what one validation reports.
public static class SettingsLimits
{
  public const int MaxSchemaBytes = 64 * 1024;
  public const int MaxDepth = 8;
  public const int MaxFields = 256;
  public const int MaxEnumValues = 100;
  public const int MaxNameLength = 128;
  public const int MaxTextLength = 2000;
  public const int MaxFieldErrors = 100;
}
