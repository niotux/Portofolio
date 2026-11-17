using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Texp.Fs;

public enum PlaceKind
{
  Home,
  UserDir,
  Bookmark,
  Root,
  Drive
}

public readonly record struct Place
(
  string Label,
  string Path,
  PlaceKind Kind,
  char Icon
);

/// <summary>
/// The sidebar: home, the XDG user dirs, GTK bookmarks (shared with GNOME Files), / and mounted drives.
/// </summary>
public static class Places
{
  /// <summary>XDG user-dirs keys shown in the sidebar, in order, with their icons.</summary>
  static readonly string[] UserDirKeys = ["XDG_DESKTOP_DIR", "XDG_DOCUMENTS_DIR", "XDG_DOWNLOAD_DIR", "XDG_MUSIC_DIR", "XDG_PICTURES_DIR", "XDG_VIDEOS_DIR"];
  static readonly char[] UserDirIcons = ['◧', '▤', '↓', '♪', '▣', '►'];
  static readonly string[] MountRoots = ["/media/", "/run/media/", "/mnt/"];

  public static string Home()
  {
    return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
  }

  public static string ConfigHome()
  {
    string? xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
    return string.IsNullOrEmpty(xdg) ? Path.Combine(Home(), ".config") : xdg;
  }

  public static string DataHome()
  {
    string? xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
    return string.IsNullOrEmpty(xdg) ? Path.Combine(Home(), ".local", "share") : xdg;
  }

  public static string BookmarksFile()
  {
    return Path.Combine(ConfigHome(), "gtk-3.0", "bookmarks");
  }

  /// <summary>The templates folder (XDG_TEMPLATES_DIR, default ~/Templates).</summary>
  public static string Templates()
  {
    string home = Home();
    string? dir = UserDir(ReadOrEmpty(Path.Combine(ConfigHome(), "user-dirs.dirs")), "XDG_TEMPLATES_DIR", home);
    return dir ?? Path.Combine(home, "Templates");
  }

  public static void Load(List<Place> into)
  {
    into.Clear();
    string home = Home();
    into.Add(new Place("Home", home, PlaceKind.Home, '⌂'));

    string dirs = ReadOrEmpty(Path.Combine(ConfigHome(), "user-dirs.dirs"));
    for (int i = 0; i < UserDirKeys.Length; i++)
    {
      string? dir = UserDir(dirs, UserDirKeys[i], home);
      if (dir != null && dir != home && Directory.Exists(dir) && !Contains(into, dir))
      {
        into.Add(new Place(Path.GetFileName(dir), dir, PlaceKind.UserDir, UserDirIcons[i]));
      }
    }

    List<Place> marks = new();
    ParseBookmarks(ReadOrEmpty(BookmarksFile()), marks);
    for (int i = 0; i < marks.Count; i++)
    {
      if (!Contains(into, marks[i].Path))
      {
        into.Add(marks[i]);
      }
    }

    into.Add(new Place("/", "/", PlaceKind.Root, '/'));
    ParseMounts(ReadOrEmpty("/proc/mounts"), into);
  }

  static bool Contains(List<Place> places, string path)
  {
    for (int i = 0; i < places.Count; i++)
    {
      if (places[i].Path == path)
      {
        return true;
      }
    }
    return false;
  }

  static string ReadOrEmpty(string path)
  {
    try
    {
      return File.ReadAllText(path);
    }
    catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
    {
      return "";
    }
  }

  /// <summary>Looks up <paramref name="key"/> in a user-dirs.dirs file: XDG_xxx_DIR="$HOME/yyy" or "/yyy".</summary>
  public static string? UserDir(ReadOnlySpan<char> file, ReadOnlySpan<char> key, string home)
  {
    foreach (Range r in file.Split('\n'))
    {
      ReadOnlySpan<char> line = file[r].Trim();
      if (!line.StartsWith(key, StringComparison.Ordinal) || line.Length <= key.Length || line[key.Length] != '=')
      {
        continue;
      }
      ReadOnlySpan<char> value = line.Slice(key.Length + 1).Trim('"');
      if (value.StartsWith("$HOME", StringComparison.Ordinal))
      {
        ReadOnlySpan<char> rest = value.Slice(5).TrimStart('/');
        return rest.IsEmpty ? home : Path.Join(home, rest);
      }
      if (value.StartsWith("/"))
      {
        return Path.TrimEndingDirectorySeparator(value).ToString();
      }
    }
    return null;
  }

  /// <summary>GTK bookmarks: one "file:///percent/encoded/path [label]" per line; other schemes are skipped.</summary>
  public static void ParseBookmarks(ReadOnlySpan<char> file, List<Place> into)
  {
    foreach (Range r in file.Split('\n'))
    {
      ReadOnlySpan<char> line = file[r].Trim();
      if (!line.StartsWith("file://", StringComparison.Ordinal))
      {
        continue;
      }
      line = line.Slice(7);
      int space = line.IndexOf(' ');
      ReadOnlySpan<char> uri = space < 0 ? line : line.Slice(0, space);
      ReadOnlySpan<char> label = space < 0 ? ReadOnlySpan<char>.Empty : line.Slice(space + 1).Trim();
      string path = Uri.UnescapeDataString(uri);
      if (path.Length > 1)
      {
        path = Path.TrimEndingDirectorySeparator(path);
      }
      string name = label.IsEmpty ? Path.GetFileName(path) : label.ToString();
      into.Add(new Place(name.Length == 0 ? path : name, path, PlaceKind.Bookmark, '★'));
    }
  }

  /// <summary>Adds the mount points under /media, /run/media and /mnt from a /proc/mounts text.</summary>
  public static void ParseMounts(ReadOnlySpan<char> mounts, List<Place> into)
  {
    foreach (Range r in mounts.Split('\n'))
    {
      ReadOnlySpan<char> line = mounts[r];
      int a = line.IndexOf(' ');
      if (a < 0)
      {
        continue;
      }
      ReadOnlySpan<char> rest = line.Slice(a + 1);
      int b = rest.IndexOf(' ');
      ReadOnlySpan<char> point = b < 0 ? rest : rest.Slice(0, b);
      bool wanted = false;
      for (int i = 0; i < MountRoots.Length; i++)
      {
        wanted |= point.StartsWith(MountRoots[i], StringComparison.Ordinal);
      }
      if (!wanted)
      {
        continue;
      }
      string path = UnescapeMount(point);
      if (!Contains(into, path))
      {
        into.Add(new Place(Path.GetFileName(path), path, PlaceKind.Drive, '◆'));
      }
    }
  }

  /// <summary>/proc/mounts writes space, tab, newline and backslash as \ooo octal.</summary>
  static string UnescapeMount(ReadOnlySpan<char> s)
  {
    StringBuilder sb = new(s.Length);
    for (int i = 0; i < s.Length; i++)
    {
      if (s[i] == '\\' && i + 3 < s.Length && IsOctal(s[i + 1]) && IsOctal(s[i + 2]) && IsOctal(s[i + 3]))
      {
        sb.Append((char)((s[i + 1] - '0') * 64 + (s[i + 2] - '0') * 8 + (s[i + 3] - '0')));
        i += 3;
      }
      else
      {
        sb.Append(s[i]);
      }
    }
    return sb.ToString();
  }

  static bool IsOctal(char c)
  {
    return c >= '0' && c <= '7';
  }

  public static bool IsBookmarked(string path)
  {
    List<Place> marks = new();
    ParseBookmarks(ReadOrEmpty(BookmarksFile()), marks);
    return Contains(marks, path);
  }

  /// <summary>Adds <paramref name="path"/> to the GTK bookmarks, or removes it if it's there. Returns true when added.</summary>
  public static bool ToggleBookmark(string path)
  {
    string file = BookmarksFile();
    string text = ReadOrEmpty(file);
    StringBuilder kept = new(text.Length + 64);
    bool removed = false;
    ReadOnlySpan<char> all = text;
    List<Place> one = new();
    foreach (Range r in all.Split('\n'))
    {
      ReadOnlySpan<char> line = all[r];
      if (line.Trim().IsEmpty)
      {
        continue;
      }
      one.Clear();
      ParseBookmarks(line, one);
      if (one.Count == 1 && one[0].Path == path)
      {
        removed = true;
        continue;
      }
      kept.Append(line).Append('\n');
    }
    if (!removed)
    {
      kept.Append("file://");
      FileOps.AppendEscaped(kept, path);
      kept.Append('\n');
    }
    Directory.CreateDirectory(Path.GetDirectoryName(file)!);
    File.WriteAllText(file, kept.ToString());
    return !removed;
  }
}
