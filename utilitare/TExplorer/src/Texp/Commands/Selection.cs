using System;
using Texp.Core;
using Texp.Fs;
using Terminal.Gui.Input;

namespace Texp.Commands;

public sealed class MarkCommand : Command
{
  public override string Name
  {
    get { return "mark"; }
  }

  public override string[] Aliases
  {
    get { return ["select"]; }
  }

  public override string Help
  {
    get { return "Mark the entry under the cursor (or every name containing text) for multi-select"; }
  }

  public override Key[] Keys
  {
    get { return [Key.Space]; }
  }

  public override string Usage
  {
    get { return "mark [text]"; }
  }

  public override int Order
  {
    get { return 70; }
  }

  public override string Hint(Explorer x, ReadOnlySpan<char> args)
  {
    return args.IsEmpty ? "mark / unmark " + (x.Tab.HasItem ? x.Tab.Current.Name : "nothing") : "mark every name containing \"" + args.ToString() + "\"";
  }

  public override void Run(Explorer x, ReadOnlySpan<char> args)
  {
    Tab t = x.Tab;
    if (args.IsEmpty)
    {
      if (t.HasItem)
      {
        t.ToggleMark(t.Current.Name);
        t.Cursor = Math.Min(t.Cursor + 1, t.Rows.Count - 1);
      }
      return;
    }
    int added = 0;
    for (int i = 0; i < t.Rows.Count; i++)
    {
      string name = t.Rows[i].Name;
      if (!t.Rows[i].IsParent && name.AsSpan().Contains(args, StringComparison.OrdinalIgnoreCase) && !t.IsMarked(name))
      {
        t.Marked.Add(name);
        added++;
      }
    }
    x.Flash("marked " + added + " more, " + t.Marked.Count + " in all");
  }
}

public sealed class SelectAllCommand : Command
{
  public override string Name
  {
    get { return "all"; }
  }

  public override string Help
  {
    get { return "Mark everything shown (again: unmark all)"; }
  }

  public override Key[] Keys
  {
    get { return [Key.A.WithCtrl]; }
  }

  public override int Order
  {
    get { return 71; }
  }

  public override void Run(Explorer x, ReadOnlySpan<char> args)
  {
    Tab t = x.Tab;
    if (t.Marked.Count >= t.ItemCount && t.ItemCount > 0)
    {
      t.Marked.Clear();
      return;
    }
    t.Marked.Clear();
    for (int i = 0; i < t.Rows.Count; i++)
    {
      if (!t.Rows[i].IsParent)
      {
        t.Marked.Add(t.Rows[i].Name);
      }
    }
  }
}

public sealed class HiddenCommand : Command
{
  public override string Name
  {
    get { return "hidden"; }
  }

  public override string Help
  {
    get { return "Show / hide hidden files (names starting with a dot)"; }
  }

  public override Key[] Keys
  {
    // Terminals send Ctrl+H as the old backspace code, which arrives as Ctrl+Backspace.
    get { return [Key.H.WithCtrl, Key.Backspace.WithCtrl, Key.H.WithAlt]; }
  }

  public override int Order
  {
    get { return 72; }
  }

  public override void Run(Explorer x, ReadOnlySpan<char> args)
  {
    x.ShowHidden = !x.ShowHidden;
    x.Flash(x.ShowHidden ? "showing hidden files" : "hiding hidden files");
  }
}

public sealed class SortCommand : Command
{
  public override string Name
  {
    get { return "sort"; }
  }

  public override string Help
  {
    get { return "Sort by name, size or date (same again reverses); or click a column header"; }
  }

  public override string Usage
  {
    get { return "sort name|size|date"; }
  }

  public override int Order
  {
    get { return 73; }
  }

  public override string? Prefill(Explorer x)
  {
    return "sort ";
  }

  public override void Run(Explorer x, ReadOnlySpan<char> args)
  {
    SortBy by;
    if (args.IsEmpty || "name".AsSpan().StartsWith(args, StringComparison.OrdinalIgnoreCase))
    {
      by = SortBy.Name;
    }
    else if ("size".AsSpan().StartsWith(args, StringComparison.OrdinalIgnoreCase))
    {
      by = SortBy.Size;
    }
    else if ("date".AsSpan().StartsWith(args, StringComparison.OrdinalIgnoreCase) || "modified".AsSpan().StartsWith(args, StringComparison.OrdinalIgnoreCase))
    {
      by = SortBy.Modified;
    }
    else
    {
      x.Fail("sort by name, size or date");
      return;
    }
    Apply(x.Tab, by);
  }

  /// <summary>Sorts by <paramref name="by"/>; choosing the current column again flips the direction.</summary>
  public static void Apply(Tab t, SortBy by)
  {
    if (t.Order.By == by)
    {
      t.Order.Descending = !t.Order.Descending;
    }
    else
    {
      t.Order.By = by;
      // Newest / biggest first is what you usually want for those columns.
      t.Order.Descending = by != SortBy.Name;
    }
  }
}

public sealed class RefreshCommand : Command
{
  public override string Name
  {
    get { return "refresh"; }
  }

  public override string[] Aliases
  {
    get { return ["reload"]; }
  }

  public override string Help
  {
    get { return "Re-read the folder and the sidebar"; }
  }

  public override Key[] Keys
  {
    get { return [Key.F5]; }
  }

  public override int Order
  {
    get { return 74; }
  }

  public override void Run(Explorer x, ReadOnlySpan<char> args)
  {
    x.Reload();
  }
}
