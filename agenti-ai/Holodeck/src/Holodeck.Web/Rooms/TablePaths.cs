namespace Holodeck.Web.Rooms;

// Turns a path an agent names into a file on its own table, refusing anything that leads elsewhere.
public static class TablePaths
{
  public static OutgoingFile Resolve(string table, string relativePath)
  {
    if (string.IsNullOrEmpty(relativePath) || Path.IsPathRooted(relativePath) || HasParentSegment(relativePath))
    {
      throw new ArgumentException($"'{relativePath}' is not a file on your table.");
    }

    string root = Path.GetFullPath(table);
    string full = Path.GetFullPath(Path.Combine(root, relativePath));
    bool isInside = full.AsSpan().StartsWith(root.AsSpan(), StringComparison.Ordinal) && full.Length > root.Length && full[root.Length] == Path.DirectorySeparatorChar;
    if (!isInside || PassesThroughLink(root, full) || !File.Exists(full))
    {
      throw new ArgumentException($"'{relativePath}' is not a file on your table.");
    }

    FileInfo file = new(full);
    if (file.Length > RoomLimits.MaxFileBytes)
    {
      throw new ArgumentException($"'{relativePath}' is larger than {RoomLimits.MaxFileBytes / (1024 * 1024)} MB.");
    }

    return new OutgoingFile(file.Name, full, root);
  }

  private static bool HasParentSegment(string path)
  {
    ReadOnlySpan<char> rest = path.AsSpan();
    while (true)
    {
      int separator = rest.IndexOfAny('/', '\\');
      ReadOnlySpan<char> segment = separator < 0 ? rest : rest[..separator];
      if (segment.SequenceEqual(".."))
      {
        return true;
      }

      if (separator < 0)
      {
        return false;
      }

      rest = rest[(separator + 1)..];
    }
  }

  private static bool PassesThroughLink(string root, string full)
  {
    string current = full;
    while (current.Length > root.Length)
    {
      FileSystemInfo entry = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
      if (entry.LinkTarget is not null)
      {
        return true;
      }

      current = Path.GetDirectoryName(current)!;
    }

    return false;
  }
}
