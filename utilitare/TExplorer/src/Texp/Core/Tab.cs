using System;
using System.Collections.Generic;
using System.IO;
using Texp.Fs;

namespace Texp.Core;

/// <summary>One open folder: its listing, what's visible after filtering, the cursor, marks and history.</summary>
public sealed class Tab
{
  public string Folder;
  public string? Error;
  public readonly List<Entry> All = new();
  public readonly List<Entry> Rows = new();
  /// <summary>Names of the marked entries (multi-selection).</summary>
  public readonly List<string> Marked = new();
  public readonly List<string> Back = new();
  public readonly List<string> Forward = new();
  public readonly Listing.Comparer Order = new();
  public int Cursor;
  public int Scroll;
  DateTime _loadedStamp;

  public Tab(string folder)
  {
    Folder = folder;
  }

  public bool HasCursor
  {
    get { return Cursor >= 0 && Cursor < Rows.Count; }
  }

  /// <summary>The cursor is on a real entry (not on ".."), so there's something to act on.</summary>
  public bool HasItem
  {
    get { return HasCursor && !Rows[Cursor].IsParent; }
  }

  public Entry Current
  {
    get { return Rows[Cursor]; }
  }

  public string PathOf(string name)
  {
    return Path.Combine(Folder, name);
  }

  public bool IsMarked(string name)
  {
    return Marked.Contains(name);
  }

  public void ToggleMark(string name)
  {
    if (!Marked.Remove(name))
    {
      Marked.Add(name);
    }
  }

  /// <summary>Re-reads the folder from disk.</summary>
  public void Load()
  {
    Error = Listing.Load(Folder, All);
    _loadedStamp = Stamp();
    for (int i = Marked.Count - 1; i >= 0; i--)
    {
      if (IndexIn(All, Marked[i]) < 0)
      {
        Marked.RemoveAt(i);
      }
    }
  }

  /// <summary>True when the folder changed on disk since the last Load (entries added, removed or renamed).</summary>
  public bool IsStale()
  {
    return Stamp() != _loadedStamp;
  }

  DateTime Stamp()
  {
    try
    {
      return Directory.GetLastWriteTimeUtc(Folder);
    }
    catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
    {
      return default;
    }
  }

  /// <summary>Rebuilds <see cref="Rows"/> from <see cref="All"/>, keeping the cursor on the same name when it's still there.</summary>
  public void Rebuild(ReadOnlySpan<char> filter, bool showHidden)
  {
    string? keep = HasCursor ? Current.Name : null;
    Rows.Clear();
    for (int i = 0; i < All.Count; i++)
    {
      Entry e = All[i];
      if (e.IsHidden && !showHidden && !filter.StartsWith("."))
      {
        continue;
      }
      if (!filter.IsEmpty && !e.Name.AsSpan().Contains(filter, StringComparison.OrdinalIgnoreCase))
      {
        continue;
      }
      Rows.Add(e);
    }
    Rows.Sort(Order);
    // ".." to go up, always first; hidden while filtering so Enter opens the match.
    bool parent = Folder != "/" && filter.IsEmpty;
    if (parent)
    {
      Rows.Insert(0, new Entry("..", true, false, 0, default, 0));
    }
    int at = keep == null ? -1 : IndexIn(Rows, keep);
    Cursor = at >= 0 ? at : Math.Clamp(Cursor, 0, Math.Max(0, Rows.Count - 1));
    // A freshly opened folder starts on its first entry, not on "..".
    if (keep == null && parent && Cursor == 0 && Rows.Count > 1)
    {
      Cursor = 1;
    }
  }

  /// <summary>Number of real entries shown (without "..").</summary>
  public int ItemCount
  {
    get { return Rows.Count > 0 && Rows[0].IsParent ? Rows.Count - 1 : Rows.Count; }
  }

  /// <summary>Puts the cursor on the best match for a filter: the first name starting with it, else the first row.</summary>
  public void JumpToMatch(ReadOnlySpan<char> filter)
  {
    for (int i = 0; i < Rows.Count; i++)
    {
      if (Rows[i].Name.AsSpan().StartsWith(filter, StringComparison.OrdinalIgnoreCase))
      {
        Cursor = i;
        return;
      }
    }
    Cursor = 0;
  }

  public void Select(string name)
  {
    int at = IndexIn(Rows, name);
    if (at >= 0)
    {
      Cursor = at;
    }
  }

  public static int IndexIn(List<Entry> entries, string name)
  {
    for (int i = 0; i < entries.Count; i++)
    {
      if (entries[i].Name == name)
      {
        return i;
      }
    }
    return -1;
  }

  /// <summary>Switches to <paramref name="folder"/>. Returns false (and changes nothing) when it can't be listed.</summary>
  public bool Navigate(string folder, bool remember)
  {
    string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
    if (full.Length == 0)
    {
      full = "/";
    }
    if (!Directory.Exists(full))
    {
      Error = full + " is not a folder";
      return false;
    }
    List<Entry> probe = new();
    string? error = Listing.Load(full, probe);
    if (error != null)
    {
      Error = error;
      return false;
    }
    string previous = Folder;
    if (remember && previous != full)
    {
      Back.Add(previous);
      Forward.Clear();
    }
    Folder = full;
    Marked.Clear();
    Rows.Clear();
    Cursor = 0;
    Scroll = 0;
    Load();
    // Coming back up out of a folder lands the cursor on it, like GNOME Files.
    if (FileOps.IsInside(previous, full) && previous != full)
    {
      ReadOnlySpan<char> rest = previous.AsSpan(full.Length).TrimStart('/');
      int slash = rest.IndexOf('/');
      string child = (slash < 0 ? rest : rest.Slice(0, slash)).ToString();
      Rows.Clear();
      Rows.AddRange(All);
      Rows.Sort(Order);
      Select(child);
    }
    return true;
  }

  /// <summary>Short name for the tab strip.</summary>
  public string Title()
  {
    if (Folder == "/")
    {
      return "/";
    }
    if (Folder == Places.Home())
    {
      return "~";
    }
    return Path.GetFileName(Folder);
  }
}
