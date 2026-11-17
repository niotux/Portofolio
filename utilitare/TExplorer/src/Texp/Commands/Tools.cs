using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Texp.Core;
using Texp.Fs;
using Terminal.Gui.Input;

namespace Texp.Commands;

public sealed class TerminalCommand : Command
{
  public override string Name
  {
    get { return "terminal"; }
  }

  public override string[] Aliases
  {
    get { return ["term", "shell"]; }
  }

  public override string Help
  {
    get { return "Open a terminal window in this folder (separate, keeps running)"; }
  }

  public override Key[] Keys
  {
    get { return [Key.E.WithCtrl]; }
  }

  public override string? Bar
  {
    get { return "terminal"; }
  }

  public override int Order
  {
    get { return 50; }
  }

  public override string Hint(Explorer x, ReadOnlySpan<char> args)
  {
    return "open " + (Launcher.Terminal() ?? "a terminal") + " in " + Explorer.Tilde(x.Tab.Folder);
  }

  public override void Run(Explorer x, ReadOnlySpan<char> args)
  {
    string? error = Launcher.OpenTerminal(x.Tab.Folder);
    if (error != null)
    {
      x.Fail(error);
    }
    else
    {
      x.Flash("terminal opened in " + Explorer.Tilde(x.Tab.Folder));
    }
  }
}

public sealed class InfoCommand : Command
{
  public override string Name
  {
    get { return "info"; }
  }

  public override string[] Aliases
  {
    get { return ["props", "properties"]; }
  }

  public override string Help
  {
    get { return "Properties: size, dates, permissions, owner"; }
  }

  public override Key[] Keys
  {
    get { return [Key.P.WithCtrl]; }
  }

  public override int Order
  {
    get { return 51; }
  }

  public override string Hint(Explorer x, ReadOnlySpan<char> args)
  {
    return "properties of " + x.SelectionLabel();
  }

  public override void Run(Explorer x, ReadOnlySpan<char> args)
  {
    List<string> selection = x.Selection();
    if (selection.Count == 0)
    {
      selection.Add(x.Tab.Folder);
    }
    Overlay box = new(selection.Count == 1 ? Path.GetFileName(selection[0]) : selection.Count + " items", null);
    int sizeLine;
    if (selection.Count == 1)
    {
      sizeLine = Describe(selection[0], box.Lines);
    }
    else
    {
      box.Lines.Add("Items:       " + selection.Count);
      box.Lines.Add("Where:       " + Explorer.Tilde(x.Tab.Folder));
      sizeLine = box.Lines.Count;
      box.Lines.Add("Size:        counting…");
    }
    x.ShowOverlay(box);

    // Folder totals can take a while: count in the background and fill the line in when done.
    _ = Task.Run(() =>
    {
      long total = 0;
      for (int i = 0; i < selection.Count; i++)
      {
        total += FileOps.Measure(selection[i], null);
      }
      string text = "Size:        " + Format.Bytes(total) + " (" + total.ToString("N0", CultureInfo.InvariantCulture) + " bytes)";
      x.Post(() =>
      {
        if (sizeLine < box.Lines.Count)
        {
          box.Lines[sizeLine] = text;
        }
      });
    });
  }

  /// <summary>Fills in the lines for one entry; returns the index of the size line.</summary>
  static int Describe(string path, List<string> lines)
  {
    FileInfo f = new(path);
    string? link = f.LinkTarget;
    bool dir = Directory.Exists(path);
    lines.Add("Where:       " + Explorer.Tilde(Path.GetDirectoryName(path) ?? "/"));
    lines.Add("Type:        " + (dir ? "folder" : "file") + (link != null ? ", link to " + link : ""));
    int sizeLine = lines.Count;
    lines.Add("Size:        counting…");
    FileSystemInfo info = dir ? new DirectoryInfo(path) : f;
    Span<char> buf = stackalloc char[16];
    try
    {
      lines.Add("Modified:    " + info.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
      lines.Add("Accessed:    " + info.LastAccessTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
      UnixFileMode mode = info.UnixFileMode;
      int n = Format.Mode(mode, buf);
      lines.Add("Permissions: " + new string(buf.Slice(0, n)) + " (" + Convert.ToString((int)mode & 0xFFF, 8) + ")");
    }
    catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
    {
      lines.Add("(" + e.Message + ")");
    }
    Launcher.Result owner = Launcher.Run("stat", ["-c", "%U:%G", "--", path]);
    if (owner.Ok)
    {
      lines.Add("Owner:       " + owner.Stdout.AsSpan().Trim().ToString());
    }
    return sizeLine;
  }
}

public sealed class BookmarkCommand : Command
{
  public override string Name
  {
    get { return "bookmark"; }
  }

  public override string Help
  {
    get { return "Add this folder to Places (again: remove it); shared with GNOME Files"; }
  }

  public override Key[] Keys
  {
    get { return [Key.B.WithCtrl]; }
  }

  public override int Order
  {
    get { return 52; }
  }

  public override string Hint(Explorer x, ReadOnlySpan<char> args)
  {
    string here = Explorer.Tilde(x.Tab.Folder);
    return Places.IsBookmarked(x.Tab.Folder) ? "remove " + here + " from Places" : "add " + here + " to Places";
  }

  public override void Run(Explorer x, ReadOnlySpan<char> args)
  {
    bool added = Places.ToggleBookmark(x.Tab.Folder);
    Places.Load(x.Places);
    x.Flash((added ? "bookmarked " : "removed bookmark ") + Explorer.Tilde(x.Tab.Folder));
  }
}

public sealed class CancelCommand : Command
{
  public override string Name
  {
    get { return "cancel"; }
  }

  public override string Help
  {
    get { return "Stop running copies, moves and deletes"; }
  }

  public override int Order
  {
    get { return 53; }
  }

  public override string Hint(Explorer x, ReadOnlySpan<char> args)
  {
    return x.Jobs.Count == 0 ? "nothing is running" : "stop " + x.Jobs.Count + " running job(s)";
  }

  public override void Run(Explorer x, ReadOnlySpan<char> args)
  {
    for (int i = 0; i < x.Jobs.Count; i++)
    {
      x.Jobs[i].Cancel.Cancel();
    }
  }
}

public sealed class HelpCommand : Command
{
  public override string Name
  {
    get { return "help"; }
  }

  public override string[] Aliases
  {
    get { return ["?"]; }
  }

  public override string Help
  {
    get { return "Show every command and key"; }
  }

  public override Key[] Keys
  {
    get { return [Key.F1]; }
  }

  public override string? Bar
  {
    get { return "help"; }
  }

  public override int Order
  {
    get { return 98; }
  }

  public override void Run(Explorer x, ReadOnlySpan<char> args)
  {
    Overlay box = new("Help", null);
    List<string> l = box.Lines;
    l.Add("Type a command and press Enter, or use its key. Anything else you type filters the list;");
    l.Add("a path (/… or ~/…) jumps there. Tab completes. Commands act on the marked entries, or the");
    l.Add("one under the cursor. Mouse: click tabs, path parts, Places, headers; double-click opens.");
    l.Add("");
    l.Add("  ↑ ↓ PgUp PgDn Home End  move        Space  mark        Esc  clear line / marks");
    l.Add("");
    for (int i = 0; i < x.Commands.Length; i++)
    {
      Command c = x.Commands[i];
      string keys = "";
      Key[] k = c.Keys;
      for (int j = 0; j < k.Length; j++)
      {
        keys = j == 0 ? KeyLabel(k[j]) : keys + " " + KeyLabel(k[j]);
      }
      string usage = c.Usage;
      string[] aliases = c.Aliases;
      if (aliases.Length > 0)
      {
        usage += " (" + string.Join(", ", aliases) + ")";
      }
      l.Add("  " + Pad(keys, 16) + Pad(usage, 30) + c.Help);
    }
    l.Add("");
    l.Add("New commands: add a class to src/Texp/Commands and run ./install.sh.");
    x.ShowOverlay(box);
  }

  static string Pad(string s, int width)
  {
    return s.Length >= width ? s + " " : s + new string(' ', width - s.Length);
  }
}

public sealed class QuitCommand : Command
{
  public override string Name
  {
    get { return "quit"; }
  }

  public override string[] Aliases
  {
    get { return ["exit", "q"]; }
  }

  public override string Help
  {
    get { return "Quit texp"; }
  }

  public override Key[] Keys
  {
    get { return [Key.Q.WithCtrl]; }
  }

  public override string? Bar
  {
    get { return "quit"; }
  }

  public override int Order
  {
    get { return 99; }
  }

  public override void Run(Explorer x, ReadOnlySpan<char> args)
  {
    if (x.Jobs.Count == 0)
    {
      x.QuitRequested = true;
      return;
    }
    x.Ask(x.Jobs[0].Label + " is still running. Quit and stop it?", () => x.QuitRequested = true);
  }
}
