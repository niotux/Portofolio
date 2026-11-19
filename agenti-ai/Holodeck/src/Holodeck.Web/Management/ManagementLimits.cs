namespace Holodeck.Web.Management;

public static class ManagementLimits
{
  public const int FormatVersion = 1;
  public const int MaxActions = 16;
  public const int MaxDeclarationBytes = 65536;
  public const int MaxIdentifierLength = 128;
  public const int MaxTitleLength = 120;
  public const int MaxDescriptionLength = 4000;
  public const int MaxPayloadBytes = 512 * 1024;
  public const int MaxMessageLength = 2000;
  public const int MaxErrorLength = 4000;
  public const int LeaseSeconds = 60;
  public const int RecentJobs = 50;
  public const int MaxProgress = 100;
}
