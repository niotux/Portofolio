namespace CreativeStudio;

public sealed class WorkspaceFiles(string workspace)
{
  private readonly string root = Path.GetFullPath(workspace);

  public string Resolve(string relative)
  {
    if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative))
    {
      throw new ArgumentException("Select a file already available in this workspace.");
    }

    string path = Path.GetFullPath(Path.Combine(root, relative));
    if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) || !File.Exists(path))
    {
      throw new ArgumentException("That reference is no longer available in this workspace.");
    }

    string current = path;
    while (current.Length > root.Length)
    {
      FileSystemInfo item = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
      if (item.LinkTarget is not null)
      {
        throw new ArgumentException("Reference files cannot traverse symbolic links.");
      }

      current = Path.GetDirectoryName(current)!;
    }

    return path;
  }

  public static bool IsImage(string path)
  {
    string extension = Path.GetExtension(path).ToLowerInvariant();
    return extension == ".png" || extension == ".jpg" || extension == ".jpeg" || extension == ".webp" || extension == ".gif";
  }

  public static bool IsText(string path)
  {
    string extension = Path.GetExtension(path).ToLowerInvariant();
    return extension == ".txt" || extension == ".md" || extension == ".json" || extension == ".csv" || extension == ".yaml" || extension == ".yml";
  }
}
