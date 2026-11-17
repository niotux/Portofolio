using System;
using System.Collections.Generic;
using System.IO;

namespace Texp.Fs;

/// <summary>Reads folders and orders their entries: folders first, then by the chosen column.</summary>
public static class Listing
{
  static readonly EnumerationOptions Options = new() { IgnoreInaccessible = true, AttributesToSkip = 0, RecurseSubdirectories = false };

  /// <summary>Fills <paramref name="into"/> with the entries of <paramref name="folder"/>; returns an error line, or null.</summary>
  public static string? Load(string folder, List<Entry> into)
  {
    into.Clear();
    try
    {
      DirectoryInfo dir = new(folder);
      foreach (FileSystemInfo info in dir.EnumerateFileSystemInfos("*", Options))
      {
        into.Add(ToEntry(info));
      }
      return null;
    }
    catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
    {
      return e.Message;
    }
  }

  public static Entry ToEntry(FileSystemInfo info)
  {
    bool link = (info.Attributes & FileAttributes.ReparsePoint) != 0;
    bool dir = info is DirectoryInfo;
    long size = 0;
    UnixFileMode mode = 0;
    DateTime modified = default;
    try
    {
      if (info is FileInfo file)
      {
        size = file.Length;
      }
      mode = info.UnixFileMode;
      modified = info.LastWriteTime;
    }
    catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
    {
      // Dangling link or a file that vanished mid-listing: show it with what we have.
    }
    return new Entry(info.Name, dir, link, size, modified, mode);
  }

  /// <summary>Folders first, then by <see cref="By"/>; names compare naturally ("file2" before "file10").</summary>
  public sealed class Comparer : IComparer<Entry>
  {
    public SortBy By;
    public bool Descending;

    public int Compare(Entry a, Entry b)
    {
      if (a.IsDir != b.IsDir)
      {
        return a.IsDir ? -1 : 1;
      }
      int c = 0;
      if (By == SortBy.Size)
      {
        c = a.Size.CompareTo(b.Size);
      }
      else if (By == SortBy.Modified)
      {
        c = a.Modified.CompareTo(b.Modified);
      }
      if (c == 0)
      {
        c = Natural(a.Name, b.Name);
      }
      return Descending ? -c : c;
    }
  }

  /// <summary>Case-insensitive compare where runs of digits compare by value. Ties break on the raw text.</summary>
  public static int Natural(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
  {
    int i = 0;
    int j = 0;
    while (i < a.Length && j < b.Length)
    {
      if (char.IsAsciiDigit(a[i]) && char.IsAsciiDigit(b[j]))
      {
        int si = i;
        int sj = j;
        while (i < a.Length && char.IsAsciiDigit(a[i]))
        {
          i++;
        }
        while (j < b.Length && char.IsAsciiDigit(b[j]))
        {
          j++;
        }
        ReadOnlySpan<char> na = a.Slice(si, i - si).TrimStart('0');
        ReadOnlySpan<char> nb = b.Slice(sj, j - sj).TrimStart('0');
        if (na.Length != nb.Length)
        {
          return na.Length < nb.Length ? -1 : 1;
        }
        int d = na.SequenceCompareTo(nb);
        if (d != 0)
        {
          return d;
        }
        continue;
      }
      char ca = char.ToLowerInvariant(a[i]);
      char cb = char.ToLowerInvariant(b[j]);
      if (ca != cb)
      {
        return ca < cb ? -1 : 1;
      }
      i++;
      j++;
    }
    if (i < a.Length || j < b.Length)
    {
      return i < a.Length ? 1 : -1;
    }
    return a.SequenceCompareTo(b);
  }
}
