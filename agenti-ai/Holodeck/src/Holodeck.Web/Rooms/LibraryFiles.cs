using System.Text.Json;

namespace Holodeck.Web.Rooms;

internal static class LibraryFiles
{
  public static T Read<T>(string path)
  {
    return JsonSerializer.Deserialize<T>(File.ReadAllText(path)) ?? throw new ArgumentException($"{path} must contain a JSON object.");
  }

  public static string RequireId(string? value, string label)
  {
    if (string.IsNullOrWhiteSpace(value) || value.StartsWith('.'))
    {
      throw new ArgumentException($"{label} must be a nonempty identifier.");
    }

    foreach (char character in value)
    {
      if (!char.IsAsciiLetterOrDigit(character) && character != '.' && character != '-' && character != '_')
      {
        throw new ArgumentException($"{label} may contain only ASCII letters, digits, dots, hyphens, and underscores.");
      }
    }

    return value;
  }

  public static string Inside(string root, string relative)
  {
    if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains('\\'))
    {
      throw new ArgumentException($"'{relative}' must be a relative library path.");
    }

    string full = Path.GetFullPath(Path.Combine(root, relative));
    string prefix = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
    if (!full.StartsWith(prefix, StringComparison.Ordinal))
    {
      throw new ArgumentException($"'{relative}' escapes its library.");
    }

    string current = full;
    while (current.Length >= prefix.Length)
    {
      FileSystemInfo entry = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
      if (entry.LinkTarget is not null)
      {
        throw new ArgumentException($"'{relative}' passes through a symbolic link.");
      }

      current = Path.GetDirectoryName(current)!;
    }

    return full;
  }

  public static bool IsManifestError(Exception exception)
  {
    return exception is ArgumentException || exception is JsonException || exception is IOException || exception is InvalidDataException || exception is OverflowException || exception is UnauthorizedAccessException;
  }
}
