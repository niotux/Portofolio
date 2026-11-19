namespace Holodeck.Web.Computers;

// Bytes used under a folder, counting files only and not following links. Stops counting after MaxEntries.
public static class DiskUsage
{
  private const int MaxEntries = 200_000;

  public static long Measure(string folder)
  {
    if (!Directory.Exists(folder))
    {
      return 0;
    }

    long total = 0;
    int seen = 0;
    EnumerationOptions options = new() { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
    foreach (FileInfo file in new DirectoryInfo(folder).EnumerateFiles("*", options))
    {
      total += file.Length;
      seen++;
      if (seen >= MaxEntries)
      {
        break;
      }
    }

    return total;
  }
}
