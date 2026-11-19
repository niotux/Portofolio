namespace Holodeck.Web.Rooms;

public static class RoomLimits
{
  public const int MaxNoteCharacters = 32000;
  public const long MaxFileBytes = 50 * 1024 * 1024;
  public const int MaxFilesPerPacket = 16;
  public const int MaxWaitSeconds = 60;
  public const int HistoryPageSize = 100;
  public const int MaxLogBytes = 1024 * 1024;
  public const int MaxNameLength = 80;
  public const int MaxTableListing = 500;
  public const int MaxSettingsBytes = 64 * 1024;
  public const int MaxIdempotencyKeyLength = 128;
  public const long MaxRequestBytes = MaxFilesPerPacket * MaxFileBytes + 1024 * 1024;
}
