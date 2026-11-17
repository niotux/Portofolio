using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Texp.Core;

namespace Texp.Fs;

/// <summary>Copy, move, rename, create and delete. Nothing here ever overwrites an existing entry.</summary>
public static class FileOps
{
  const int BufferSize = 1 << 20;

  /// <summary>True for anything at <paramref name="path"/>, including a dangling symlink.</summary>
  public static bool Exists(string path)
  {
    return File.Exists(path) || Directory.Exists(path) || new FileInfo(path).LinkTarget != null;
  }

  /// <summary>Returns an error for a name that can't be a single entry, or null when it's fine.</summary>
  public static string? CheckName(ReadOnlySpan<char> name)
  {
    if (name.IsEmpty)
    {
      return "a name is needed";
    }
    if (name.Contains('/') || name.Contains('\0'))
    {
      return "a name can't contain '/'";
    }
    if (name.SequenceEqual(".") || name.SequenceEqual(".."))
    {
      return "'.' and '..' are taken";
    }
    return null;
  }

  /// <summary>
  /// <paramref name="name"/> if it's free in <paramref name="folder"/>, else "name (copy).ext", "name (copy 2).ext", ...
  /// (or "name (2).ext", ... when <paramref name="copyWord"/> is false).
  /// </summary>
  public static string FreeName(string folder, string name, bool copyWord)
  {
    if (!Exists(Path.Combine(folder, name)))
    {
      return name;
    }
    ReadOnlySpan<char> n = name;
    int dot = n.LastIndexOf('.');
    if (dot <= 0)
    {
      dot = n.Length;
    }
    ReadOnlySpan<char> stem = n.Slice(0, dot);
    ReadOnlySpan<char> ext = n.Slice(dot);
    Span<char> number = stackalloc char[12];
    for (int i = copyWord ? 1 : 2; ; i++)
    {
      StringBuilder sb = new(name.Length + 16);
      sb.Append(stem).Append(" (");
      if (copyWord)
      {
        sb.Append("copy");
      }
      if (i > 1)
      {
        i.TryFormat(number, out int len, default, CultureInfo.InvariantCulture);
        if (copyWord)
        {
          sb.Append(' ');
        }
        sb.Append(number.Slice(0, len));
      }
      sb.Append(')').Append(ext);
      string candidate = sb.ToString();
      if (!Exists(Path.Combine(folder, candidate)))
      {
        return candidate;
      }
    }
  }

  public static void Rename(string path, string newName)
  {
    string? error = CheckName(newName);
    if (error != null)
    {
      throw new IOException(error);
    }
    string folder = Path.GetDirectoryName(path) ?? "/";
    string target = Path.Combine(folder, newName);
    if (Exists(target))
    {
      throw new IOException(newName + " already exists");
    }
    MoveEntry(path, target);
  }

  public static string MakeFolder(string folder, string name)
  {
    string? error = CheckName(name);
    if (error != null)
    {
      throw new IOException(error);
    }
    string path = Path.Combine(folder, name);
    if (Exists(path))
    {
      throw new IOException(name + " already exists");
    }
    Directory.CreateDirectory(path);
    return path;
  }

  /// <summary>Creates a new entry in <paramref name="folder"/> from <paramref name="template"/> (null = empty file); returns its name.</summary>
  public static string CreateFrom(string folder, string? template, string name)
  {
    string free = FreeName(folder, name, false);
    string path = Path.Combine(folder, free);
    if (template == null)
    {
      using (new FileStream(path, FileMode.CreateNew))
      {
      }
    }
    else
    {
      Job job = new("new");
      CopyEntry(template, path, job, new byte[BufferSize]);
    }
    return free;
  }

  /// <summary>Total bytes under <paramref name="path"/>, not following symlinks.</summary>
  public static long Measure(string path, Job? job)
  {
    FileInfo f = new(path);
    if (f.LinkTarget != null)
    {
      return 0;
    }
    if (f.Exists)
    {
      return f.Length;
    }
    if (!Directory.Exists(path))
    {
      return 0;
    }
    long total = 0;
    EnumerationOptions options = new() { IgnoreInaccessible = true, AttributesToSkip = 0, RecurseSubdirectories = false };
    foreach (FileSystemInfo info in new DirectoryInfo(path).EnumerateFileSystemInfos("*", options))
    {
      job?.Token.ThrowIfCancellationRequested();
      if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
      {
        continue;
      }
      if (info is FileInfo file)
      {
        total += file.Length;
      }
      else
      {
        total += Measure(info.FullName, job);
      }
    }
    return total;
  }

  /// <summary>
  /// Copies or moves <paramref name="sources"/> into <paramref name="folder"/>. Clashing names get " (copy)";
  /// moving an entry onto its own folder does nothing. Returns the names created in <paramref name="folder"/>.
  /// </summary>
  public static List<string> Transfer(List<string> sources, string folder, bool move, Job job)
  {
    string dest = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
    long total = 0;
    for (int i = 0; i < sources.Count; i++)
    {
      string src = sources[i];
      if (IsInside(dest, src))
      {
        throw new IOException("can't put " + Path.GetFileName(src) + " inside itself");
      }
      if (!move)
      {
        total += Measure(src, job);
      }
    }
    job.Total = move ? sources.Count : total;

    List<string> created = new();
    byte[] buffer = new byte[BufferSize];
    for (int i = 0; i < sources.Count; i++)
    {
      job.Token.ThrowIfCancellationRequested();
      string src = sources[i];
      string name = Path.GetFileName(src);
      if (move && Path.GetDirectoryName(src) == dest)
      {
        job.Advance(1);
        continue;
      }
      string free = FreeName(dest, name, true);
      string target = Path.Combine(dest, free);
      if (move)
      {
        MoveEntry(src, target);
        job.Advance(1);
      }
      else
      {
        CopyEntry(src, target, job, buffer);
      }
      created.Add(free);
    }
    return created;
  }

  /// <summary>True when <paramref name="path"/> is <paramref name="root"/> or somewhere below it.</summary>
  public static bool IsInside(string path, string root)
  {
    ReadOnlySpan<char> p = path;
    ReadOnlySpan<char> r = Path.TrimEndingDirectorySeparator(root.AsSpan());
    if (!p.StartsWith(r, StringComparison.Ordinal))
    {
      return false;
    }
    return p.Length == r.Length || p[r.Length] == '/' || r.Length == 1;
  }

  /// <summary>rename(2) when possible; across drives, copy and then delete the original.</summary>
  static void MoveEntry(string src, string target)
  {
    FileInfo f = new(src);
    if (f.LinkTarget != null || f.Exists)
    {
      File.Move(src, target);
      return;
    }
    try
    {
      Directory.Move(src, target);
    }
    catch (IOException) when (!Exists(target))
    {
      Job job = new("move");
      CopyEntry(src, target, job, new byte[BufferSize]);
      DeletePermanently(src);
    }
  }

  static void CopyEntry(string src, string target, Job job, byte[] buffer)
  {
    job.Token.ThrowIfCancellationRequested();
    FileInfo f = new(src);
    string? link = f.LinkTarget;
    if (link != null)
    {
      File.CreateSymbolicLink(target, link);
      return;
    }
    if (f.Exists)
    {
      CopyFile(f, target, job, buffer);
      return;
    }
    DirectoryInfo dir = new(src);
    Directory.CreateDirectory(target);
    EnumerationOptions options = new() { IgnoreInaccessible = false, AttributesToSkip = 0, RecurseSubdirectories = false };
    foreach (FileSystemInfo info in dir.EnumerateFileSystemInfos("*", options))
    {
      CopyEntry(info.FullName, Path.Combine(target, info.Name), job, buffer);
    }
    File.SetUnixFileMode(target, dir.UnixFileMode);
    Directory.SetLastWriteTimeUtc(target, dir.LastWriteTimeUtc);
  }

  static void CopyFile(FileInfo src, string target, Job job, byte[] buffer)
  {
    using (FileStream input = new(src.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 0))
    using (FileStream output = new(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 0))
    {
      while (true)
      {
        job.Token.ThrowIfCancellationRequested();
        int n = input.Read(buffer, 0, buffer.Length);
        if (n == 0)
        {
          break;
        }
        output.Write(buffer, 0, n);
        job.Advance(n);
      }
    }
    File.SetUnixFileMode(target, src.UnixFileMode);
    File.SetLastWriteTimeUtc(target, src.LastWriteTimeUtc);
  }

  /// <summary>Removes for good. A symlink is removed, never what it points to.</summary>
  public static void DeletePermanently(string path)
  {
    FileInfo f = new(path);
    if (f.LinkTarget != null || f.Exists)
    {
      File.Delete(path);
    }
    else if (Directory.Exists(path))
    {
      Directory.Delete(path, true);
    }
  }

  /// <summary>Moves <paramref name="paths"/> to the desktop trash: `gio trash` when available, else the XDG home trash.</summary>
  public static void Trash(List<string> paths)
  {
    if (Launcher.FindOnPath("gio") != null)
    {
      string[] args = new string[paths.Count + 2];
      args[0] = "trash";
      args[1] = "--";
      for (int i = 0; i < paths.Count; i++)
      {
        args[i + 2] = paths[i];
      }
      Launcher.Result r = Launcher.Run("gio", args);
      if (!r.Ok)
      {
        throw new IOException(r.ErrorLine());
      }
      return;
    }
    for (int i = 0; i < paths.Count; i++)
    {
      HomeTrash(paths[i]);
    }
  }

  /// <summary>The freedesktop.org trash in the user's data dir; only works on the same drive as it.</summary>
  public static void HomeTrash(string path)
  {
    string trash = Path.Combine(Places.DataHome(), "Trash");
    string files = Path.Combine(trash, "files");
    string info = Path.Combine(trash, "info");
    Directory.CreateDirectory(files);
    Directory.CreateDirectory(info);
    string name = FreeName(files, Path.GetFileName(path), false);
    StringBuilder sb = new();
    sb.Append("[Trash Info]\nPath=");
    AppendEscaped(sb, Path.GetFullPath(path));
    sb.Append("\nDeletionDate=").Append(DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)).Append('\n');
    string infoPath = Path.Combine(info, name + ".trashinfo");
    File.WriteAllText(infoPath, sb.ToString());
    try
    {
      FileInfo f = new(path);
      if (f.LinkTarget != null || f.Exists)
      {
        File.Move(path, Path.Combine(files, name));
      }
      else
      {
        Directory.Move(path, Path.Combine(files, name));
      }
    }
    catch
    {
      File.Delete(infoPath);
      throw;
    }
  }

  /// <summary>Percent-encodes a path for a .trashinfo / bookmarks URI, keeping '/' and unreserved characters.</summary>
  public static void AppendEscaped(StringBuilder sb, ReadOnlySpan<char> path)
  {
    Span<byte> bytes = stackalloc byte[4];
    Span<char> one = stackalloc char[2];
    for (int i = 0; i < path.Length; i++)
    {
      char c = path[i];
      if (char.IsAsciiLetterOrDigit(c) || c == '/' || c == '-' || c == '_' || c == '.' || c == '~')
      {
        sb.Append(c);
        continue;
      }
      int units = char.IsHighSurrogate(c) && i + 1 < path.Length ? 2 : 1;
      one[0] = c;
      if (units == 2)
      {
        one[1] = path[++i];
      }
      int n = Encoding.UTF8.GetBytes(one.Slice(0, units), bytes);
      for (int b = 0; b < n; b++)
      {
        sb.Append('%').Append(bytes[b].ToString("X2", CultureInfo.InvariantCulture));
      }
    }
  }
}
