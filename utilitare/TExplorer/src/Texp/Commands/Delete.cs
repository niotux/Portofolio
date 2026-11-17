using System;
using System.Collections.Generic;
using Texp.Core;
using Texp.Fs;
using Terminal.Gui.Input;

namespace Texp.Commands;

public sealed class TrashCommand : Command
{
  public override string Name
  {
    get { return "delete"; }
  }

  public override string[] Aliases
  {
    get { return ["trash", "rm", "del"]; }
  }

  public override string Help
  {
    get { return "Move the selection to the trash (restore it from GNOME's Trash)"; }
  }

  public override Key[] Keys
  {
    get { return [Key.Delete]; }
  }

  public override string? Bar
  {
    get { return "trash"; }
  }

  public override int Order
  {
    get { return 40; }
  }

  public override string Hint(Explorer x, ReadOnlySpan<char> args)
  {
    return "move " + x.SelectionLabel() + " to the trash";
  }

  public override void Run(Explorer x, ReadOnlySpan<char> args)
  {
    List<string> selection = x.Selection();
    if (selection.Count == 0)
    {
      x.Fail("nothing selected");
      return;
    }
    Job job = new("trashing " + x.SelectionLabel());
    x.Tab.Marked.Clear();
    x.Start(job, j =>
    {
      j.Total = selection.Count;
      FileOps.Trash(selection);
      j.Advance(selection.Count);
      return null;
    });
  }
}

public sealed class DeleteForeverCommand : Command
{
  public override string Name
  {
    get { return "delete!"; }
  }

  public override string[] Aliases
  {
    get { return ["destroy"]; }
  }

  public override string Help
  {
    get { return "Delete the selection permanently (asks first)"; }
  }

  public override Key[] Keys
  {
    get { return [Key.Delete.WithShift]; }
  }

  public override int Order
  {
    get { return 41; }
  }

  public override string Hint(Explorer x, ReadOnlySpan<char> args)
  {
    return "PERMANENTLY delete " + x.SelectionLabel();
  }

  public override void Run(Explorer x, ReadOnlySpan<char> args)
  {
    List<string> selection = x.Selection();
    if (selection.Count == 0)
    {
      x.Fail("nothing selected");
      return;
    }
    string label = x.SelectionLabel();
    x.Ask("Permanently delete " + label + "? This can't be undone.", () =>
    {
      x.Tab.Marked.Clear();
      Job job = new("deleting " + label);
      x.Start(job, j =>
      {
        j.Total = selection.Count;
        for (int i = 0; i < selection.Count; i++)
        {
          j.Token.ThrowIfCancellationRequested();
          FileOps.DeletePermanently(selection[i]);
          j.Advance(1);
        }
        return null;
      });
    });
  }
}
