using System;
using Texp.Core;
using Terminal.Gui.Input;

namespace Texp.Commands;

public sealed class NewTabCommand : Command
{
  public override string Name
  {
    get { return "tab"; }
  }

  public override string Help
  {
    get { return "Open a new tab (here, or at a path)"; }
  }

  public override Key[] Keys
  {
    get { return [Key.T.WithCtrl]; }
  }

  public override string Usage
  {
    get { return "tab [path]"; }
  }

  public override string? Bar
  {
    get { return "tab"; }
  }

  public override int Order
  {
    get { return 60; }
  }

  public override string Hint(Explorer x, ReadOnlySpan<char> args)
  {
    return "new tab at " + Explorer.Tilde(args.IsEmpty ? x.Tab.Folder : x.ResolvePath(args));
  }

  public override void Run(Explorer x, ReadOnlySpan<char> args)
  {
    x.OpenTab(args.IsEmpty ? x.Tab.Folder : x.ResolvePath(args));
  }
}

public sealed class CloseTabCommand : Command
{
  public override string Name
  {
    get { return "close"; }
  }

  public override string Help
  {
    get { return "Close this tab (the last one quits)"; }
  }

  public override Key[] Keys
  {
    get { return [Key.W.WithCtrl]; }
  }

  public override int Order
  {
    get { return 61; }
  }

  public override void Run(Explorer x, ReadOnlySpan<char> args)
  {
    x.CloseTab();
  }
}

public sealed class NextTabCommand : Command
{
  public override string Name
  {
    get { return "next"; }
  }

  public override string Help
  {
    get { return "Switch to the next tab"; }
  }

  public override Key[] Keys
  {
    get { return [Key.PageDown.WithCtrl, Key.CursorRight.WithCtrl]; }
  }

  public override int Order
  {
    get { return 62; }
  }

  public override void Run(Explorer x, ReadOnlySpan<char> args)
  {
    x.SwitchTab(x.Active + 1);
  }
}

public sealed class PrevTabCommand : Command
{
  public override string Name
  {
    get { return "prev"; }
  }

  public override string Help
  {
    get { return "Switch to the previous tab"; }
  }

  public override Key[] Keys
  {
    get { return [Key.PageUp.WithCtrl, Key.CursorLeft.WithCtrl]; }
  }

  public override int Order
  {
    get { return 63; }
  }

  public override void Run(Explorer x, ReadOnlySpan<char> args)
  {
    x.SwitchTab(x.Active - 1);
  }
}
