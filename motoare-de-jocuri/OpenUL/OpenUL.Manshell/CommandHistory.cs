namespace OpenUL.Manshell;

internal static class CommandHistory
{
  private static readonly string FilePath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    ".manshell",
    "history.txt"
  );

  private static readonly List<string> Entries = Load();

  public static IReadOnlyList<string> All => Entries;

  public static void Add(string line)
  {
    if (string.IsNullOrWhiteSpace(line))
    {
      return;
    }
    if (Entries.Count > 0 && Entries[^1] == line)
    {
      return;
    }

    Entries.Add(line);
    try
    {
      var dir = Path.GetDirectoryName(FilePath);
      if (dir != null)
      {
        Directory.CreateDirectory(dir);
      }
      File.AppendAllLines(FilePath, [line]);
    }
    catch
    {
      // Best-effort persistence: history still works for the rest of this session.
    }
  }

  private static List<string> Load()
  {
    try
    {
      return File.Exists(FilePath) ? [.. File.ReadAllLines(FilePath)] : [];
    }
    catch
    {
      return [];
    }
  }
}
