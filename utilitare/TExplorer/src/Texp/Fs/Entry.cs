using System;
using System.IO;

namespace Texp.Fs;

/// <summary>One row of a folder listing.</summary>
public readonly record struct Entry
(
  string Name,
  bool IsDir,
  bool IsLink,
  long Size,
  DateTime Modified,
  UnixFileMode Mode
)
{
  public bool IsHidden
  {
    get { return Name.Length > 0 && Name[0] == '.'; }
  }

  /// <summary>The ".." row at the top of the list, which goes up a folder. Never a real entry.</summary>
  public bool IsParent
  {
    get { return Name == ".."; }
  }

  public bool IsExecutable
  {
    get { return !IsDir && (Mode & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0; }
  }
}

public enum SortBy
{
  Name,
  Size,
  Modified
}
