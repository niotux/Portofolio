using System.IO.Compression;
using System.Security.Cryptography;

namespace Holodeck.Web.Rooms;

internal static class LibraryArchive
{
  private const int MaxEntries = 10000;
  private const long MaxExpandedBytes = 512L * 1024 * 1024;
  private const int UnixTypeMask = 0xF000;
  private const int UnixRegularFile = 0x8000;
  private const int UnixDirectory = 0x4000;
  private const int UnixPermissionsMask = 0x1FF;

  public static string Extract(string archivePath, string cache)
  {
    using FileStream source = File.OpenRead(archivePath);
    string hash = Convert.ToHexString(SHA256.HashData(source));
    string destination = Path.Combine(cache, hash);
    if (Directory.Exists(destination))
    {
      return destination;
    }

    Directory.CreateDirectory(cache);
    string temporary = Path.Combine(cache, "." + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(temporary);
    try
    {
      source.Position = 0;
      using ZipArchive archive = new(source, ZipArchiveMode.Read, true);
      ExtractEntries(archive, temporary);
      if (!File.Exists(Path.Combine(temporary, "Library.json")))
      {
        throw new ArgumentException("A library ZIP must contain Library.json at its root.");
      }

      Directory.Move(temporary, destination);
      return destination;
    }
    finally
    {
      if (Directory.Exists(temporary))
      {
        Directory.Delete(temporary, true);
      }
    }
  }

  private static void ExtractEntries(ZipArchive archive, string destination)
  {
    if (archive.Entries.Count > MaxEntries)
    {
      throw new ArgumentException($"A library ZIP may contain at most {MaxEntries} entries.");
    }

    long expanded = 0;
    HashSet<string> names = new(StringComparer.Ordinal);
    foreach (ZipArchiveEntry entry in archive.Entries)
    {
      expanded = checked(expanded + entry.Length);
      if (expanded > MaxExpandedBytes)
      {
        throw new ArgumentException("The expanded library ZIP exceeds 512 MB.");
      }

      int fileType = (entry.ExternalAttributes >> 16) & UnixTypeMask;
      if (fileType != 0 && fileType != UnixRegularFile && fileType != UnixDirectory)
      {
        throw new ArgumentException("Library ZIP entries must be ordinary files or directories.");
      }

      string target = LibraryFiles.Inside(destination, entry.FullName);
      if (!names.Add(target))
      {
        throw new ArgumentException($"Duplicate ZIP entry '{entry.FullName}'.");
      }

      if (entry.FullName.EndsWith('/'))
      {
        Directory.CreateDirectory(target);
        continue;
      }

      Directory.CreateDirectory(Path.GetDirectoryName(target)!);
      entry.ExtractToFile(target);
      int permissions = (entry.ExternalAttributes >> 16) & UnixPermissionsMask;
      if (!OperatingSystem.IsWindows() && permissions != 0)
      {
        File.SetUnixFileMode(target, (UnixFileMode)permissions);
      }
    }
  }
}
