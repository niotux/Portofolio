namespace Holodeck.Web.Activity;

public static class ActivityLimits
{
  public const int MaxId = 128;
  public const int MaxLabel = 160;
  public const int MaxText = 128 * 1024;
  public const int MaxSteps = 16;
  public const int RecentRuns = 50;
  public const int LeaseSeconds = 45;
}
