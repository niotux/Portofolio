using System;
using System.Collections.Generic;
using Texp.Core;
using Texp.Fs;
using Terminal.Gui.Input;

namespace Texp.Commands;

public sealed class CopyCommand : Command
{
  public override string Name
  {
    get { return "copy"; }
  }

  public override string[] Aliases
  {
    get { return ["cp"]; }
  }

  public override string Help
  {
    get { return "Copy the selection; paste it in another folder or tab"; }
  }

  public override Key[] Keys
  {
    get { return [Key.C.WithCtrl]; }
  }

  public override string? Bar
  {
    get { return "copy"; }
  }

  public override int Order
  {
    get { return 20; }
  }

  public override string Hint(Explorer x, ReadOnlySpan<char> args)
  {
    return "copy " + x.SelectionLabel() + " to the clipboard";
  }

  public override void Run(Explorer x, ReadOnlySpan<char> args)
  {
    Take(x, false);
  }

  internal static void Take(Explorer x, bool cut)
  {
    List<string> selection = x.Selection();
    if (selection.Count == 0)
    {
      x.Fail("nothing selected");
      return;
    }
    string label = x.SelectionLabel();
    x.Clipboard.Clear();
    x.Clipboard.AddRange(selection);
    x.ClipboardIsCut = cut;
    x.Tab.Marked.Clear();
    x.Flash((cut ? "cut " : "copied ") + label + ": go where you want it and press ^V");
  }
}

public sealed class CutCommand : Command
{
  public override string Name
  {
    get { return "cut"; }
  }

  public override string[] Aliases
  {
    get { return ["mv", "move"]; }
  }

  public override string Help
  {
    get { return "Cut the selection; paste moves it"; }
  }

  public override Key[] Keys
  {
    get { return [Key.X.WithCtrl]; }
  }

  public override string? Bar
  {
    get { return "cut"; }
  }

  public override int Order
  {
    get { return 21; }
  }

  public override string Hint(Explorer x, ReadOnlySpan<char> args)
  {
    return "cut " + x.SelectionLabel() + ", to move it with paste";
  }

  public override void Run(Explorer x, ReadOnlySpan<char> args)
  {
    CopyCommand.Take(x, true);
  }
}

public sealed class PasteCommand : Command
{
  public override string Name
  {
    get { return "paste"; }
  }

  public override string Help
  {
    get { return "Paste what you copied or cut into this folder (never overwrites)"; }
  }

  public override Key[] Keys
  {
    get { return [Key.V.WithCtrl]; }
  }

  public override string? Bar
  {
    get { return "paste"; }
  }

  public override int Order
  {
    get { return 22; }
  }

  public override string Hint(Explorer x, ReadOnlySpan<char> args)
  {
    if (x.Clipboard.Count == 0)
    {
      return "the clipboard is empty";
    }
    return (x.ClipboardIsCut ? "move " : "copy ") + x.Clipboard.Count + " item(s) into " + Explorer.Tilde(x.Tab.Folder);
  }

  public override void Run(Explorer x, ReadOnlySpan<char> args)
  {
    if (x.Clipboard.Count == 0)
    {
      x.Fail("nothing to paste: copy (^C) or cut (^X) something first");
      return;
    }
    List<string> sources = new(x.Clipboard);
    bool move = x.ClipboardIsCut;
    string folder = x.Tab.Folder;
    Tab tab = x.Tab;
    Job job = new((move ? "moving " : "copying ") + sources.Count + " item(s)");
    x.Start(job, j =>
    {
      List<string> created = FileOps.Transfer(sources, folder, move, j);
      return () =>
      {
        // Only a move that worked empties the clipboard; after a failure you can fix things and paste again.
        if (move)
        {
          x.Clipboard.Clear();
        }
        if (tab.Folder != folder)
        {
          return;
        }
        tab.Marked.Clear();
        if (created.Count > 1)
        {
          tab.Marked.AddRange(created);
        }
        if (created.Count > 0)
        {
          tab.Rebuild(x.Filter, x.ShowHidden);
          tab.Select(created[0]);
        }
      };
    });
  }
}
