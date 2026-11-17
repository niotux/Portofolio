using System;
using System.IO;
using Texp.Core;
using Texp.Fs;
using Terminal.Gui.Input;

namespace Texp.Commands;

public sealed class OpenCommand : Command
{
  public override string Name
  {
    get { return "open"; }
  }

  public override string Help
  {
    get { return "Open the folder, or open the file in its usual app"; }
  }

  public override Key[] Keys
  {
    get { return [Key.Enter]; }
  }

  public override string Usage
  {
    get { return "open [name]"; }
  }

  public override int Order
  {
    get { return 10; }
  }

  public override string Hint(Explorer x, ReadOnlySpan<char> args)
  {
    if (args.IsEmpty && x.Tab.HasCursor && x.Tab.Current.IsParent)
    {
      return "go up to the parent folder";
    }
    return "open " + (args.IsEmpty ? x.SelectionLabel() : args.ToString());
  }

  public override void Run(Explorer x, ReadOnlySpan<char> args)
  {
    string path;
    if (!args.IsEmpty)
    {
      path = x.ResolvePath(args);
    }
    else if (x.Tab.HasCursor)
    {
      path = x.Tab.Current.IsParent ? Path.GetDirectoryName(x.Tab.Folder) ?? "/" : x.Tab.PathOf(x.Tab.Current.Name);
    }
    else
    {
      return;
    }
    if (Directory.Exists(path))
    {
      x.Go(path);
      return;
    }
    if (IsExecutable(path))
    {
      AskHowToRun(x, path);
      return;
    }
    string? error = Launcher.Open(path);
    if (error != null)
    {
      x.Fail(error);
    }
    else
    {
      x.Flash("opening " + Path.GetFileName(path) + "…");
    }
  }

  /// <summary>A regular file with an execute bit (following links).</summary>
  internal static bool IsExecutable(string path)
  {
    try
    {
      FileInfo f = new(path);
      FileSystemInfo target = f.LinkTarget != null ? f.ResolveLinkTarget(true) ?? f : f;
      return target is FileInfo file && file.Exists && (file.UnixFileMode & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
    }
    catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
    {
      return false;
    }
  }

  /// <summary>Like GNOME Files: a program could be meant to run or to be looked at, so ask.</summary>
  static void AskHowToRun(Explorer x, string path)
  {
    string name = Path.GetFileName(path);
    string folder = Path.GetDirectoryName(path) ?? "/";
    Overlay picker = new(name + " is a program", index =>
    {
      switch (index)
      {
        case 0:
          RunCommand.Report(x, Launcher.RunInTerminal(path, folder), "running " + name + " in a terminal");
          break;
        case 1:
          RunCommand.Report(x, Launcher.Detached(path, [], folder), "started " + name);
          break;
        case 2:
          RunCommand.Report(x, Launcher.Open(path), "opening " + name + "…");
          break;
      }
    });
    picker.Lines.Add("Run in a terminal          see its output; the window stays open");
    picker.Lines.Add("Run                        no window; for apps with their own GUI");
    picker.Lines.Add("Open with the default app  e.g. view the script in an editor");
    x.ShowOverlay(picker);
  }
}

public sealed class RunCommand : Command
{
  public override string Name
  {
    get { return "run"; }
  }

  public override string[] Aliases
  {
    get { return ["exec"]; }
  }

  public override string Help
  {
    get { return "Run the program under the cursor in a new terminal window"; }
  }

  public override string Usage
  {
    get { return "run [name]"; }
  }

  public override int Order
  {
    get { return 15; }
  }

  public override string Hint(Explorer x, ReadOnlySpan<char> args)
  {
    return "run " + (args.IsEmpty ? x.SelectionLabel() : args.ToString()) + " in a terminal";
  }

  public override void Run(Explorer x, ReadOnlySpan<char> args)
  {
    string path;
    if (!args.IsEmpty)
    {
      path = x.ResolvePath(args);
    }
    else if (x.Tab.HasItem)
    {
      path = x.Tab.PathOf(x.Tab.Current.Name);
    }
    else
    {
      return;
    }
    if (!OpenCommand.IsExecutable(path))
    {
      x.Fail(Path.GetFileName(path) + " isn't executable");
      return;
    }
    Report(x, Launcher.RunInTerminal(path, x.Tab.Folder), "running " + Path.GetFileName(path) + " in a terminal");
  }

  internal static void Report(Explorer x, string? error, string ok)
  {
    if (error != null)
    {
      x.Fail(error);
    }
    else
    {
      x.Flash(ok);
    }
  }
}

public sealed class UpCommand : Command
{
  public override string Name
  {
    get { return "up"; }
  }

  public override string[] Aliases
  {
    get { return [".."]; }
  }

  public override string Help
  {
    get { return "Go to the parent folder"; }
  }

  public override Key[] Keys
  {
    get { return [Key.CursorLeft, Key.CursorUp.WithAlt]; }
  }

  public override int Order
  {
    get { return 11; }
  }

  public override void Run(Explorer x, ReadOnlySpan<char> args)
  {
    string? parent = Path.GetDirectoryName(x.Tab.Folder);
    if (parent != null)
    {
      x.Go(parent);
    }
  }
}

public sealed class BackCommand : Command
{
  public override string Name
  {
    get { return "back"; }
  }

  public override string Help
  {
    get { return "Go back to the previous folder"; }
  }

  public override Key[] Keys
  {
    get { return [Key.CursorLeft.WithAlt]; }
  }

  public override int Order
  {
    get { return 12; }
  }

  public override void Run(Explorer x, ReadOnlySpan<char> args)
  {
    Step(x, x.Tab.Back, x.Tab.Forward);
  }

  /// <summary>Moves one step through the tab's history, from <paramref name="from"/> onto <paramref name="to"/>.</summary>
  internal static void Step(Explorer x, System.Collections.Generic.List<string> from, System.Collections.Generic.List<string> to)
  {
    if (from.Count == 0)
    {
      x.Flash("no more history");
      return;
    }
    string here = x.Tab.Folder;
    string target = from[from.Count - 1];
    from.RemoveAt(from.Count - 1);
    if (x.Tab.Navigate(target, false))
    {
      to.Add(here);
    }
    else
    {
      x.Fail(x.Tab.Error ?? "can't open " + target);
    }
  }
}

public sealed class ForwardCommand : Command
{
  public override string Name
  {
    get { return "forward"; }
  }

  public override string Help
  {
    get { return "Go forward again after going back"; }
  }

  public override Key[] Keys
  {
    get { return [Key.CursorRight.WithAlt]; }
  }

  public override int Order
  {
    get { return 13; }
  }

  public override void Run(Explorer x, ReadOnlySpan<char> args)
  {
    BackCommand.Step(x, x.Tab.Forward, x.Tab.Back);
  }
}

public sealed class GoCommand : Command
{
  public override string Name
  {
    get { return "cd"; }
  }

  public override string[] Aliases
  {
    get { return ["go"]; }
  }

  public override string Help
  {
    get { return "Go to a folder (or just type a path starting with / or ~)"; }
  }

  public override Key[] Keys
  {
    get { return [Key.L.WithCtrl]; }
  }

  public override string Usage
  {
    get { return "cd <path>"; }
  }

  public override int Order
  {
    get { return 14; }
  }

  public override string? Prefill(Explorer x)
  {
    string here = Explorer.Tilde(x.Tab.Folder);
    return here == "/" ? "cd /" : "cd " + here + "/";
  }

  public override string Hint(Explorer x, ReadOnlySpan<char> args)
  {
    return "go to " + Explorer.Tilde(args.IsEmpty ? Places.Home() : x.ResolvePath(args));
  }

  public override void Run(Explorer x, ReadOnlySpan<char> args)
  {
    x.Go(args.IsEmpty ? Places.Home() : x.ResolvePath(args));
  }
}
