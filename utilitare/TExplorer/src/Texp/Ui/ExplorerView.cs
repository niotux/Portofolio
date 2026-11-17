using System;
using System.Collections.Generic;
using Texp.Commands;
using Texp.Core;
using Texp.Fs;
using Terminal.Gui.Input;
using Terminal.Gui.Views;
using Command = Texp.Commands.Command;

namespace Texp.Ui;

/// <summary>The whole app: one self-drawn screen. Input handling lives here, drawing in ExplorerView.Draw.cs.</summary>
public sealed partial class ExplorerView : Runnable
{
  readonly Explorer _x;

  public ExplorerView(Explorer explorer)
  {
    _x = explorer;
    CanFocus = true;
  }

  /// <summary>Periodic UI-thread refresh: picks up changes on disk and animates progress.</summary>
  public bool Tick()
  {
    _x.Tick();
    SetNeedsDraw();
    return true;
  }

  // ---- Keyboard -------------------------------------------------------------------------------

  /// <summary>Hooked to the application's KeyDown so it runs before default bindings (Esc would quit otherwise).</summary>
  public void OnAppKeyDown(object? sender, Key key)
  {
    if (HandleKey(key))
    {
      key.Handled = true;
    }
    AfterInput();
  }

  void AfterInput()
  {
    if (_x.QuitRequested)
    {
      App?.RequestStop();
      return;
    }
    EnsureCursorVisible();
    SetNeedsDraw();
  }

  bool HandleKey(Key key)
  {
    if (_x.Overlay != null)
    {
      return HandleOverlayKey(_x.Overlay, key);
    }
    int rune = key.AsRune.Value;
    if (_x.Question != null)
    {
      if (key == Key.Enter || rune == 'y' || rune == 'Y')
      {
        _x.Answer(true);
      }
      else if (key == Key.Esc || rune == 'n' || rune == 'N')
      {
        _x.Answer(false);
      }
      return true;
    }

    if (key == Key.Esc)
    {
      if (!_x.Line.IsEmpty)
      {
        _x.ClearLine();
      }
      else
      {
        _x.Tab.Marked.Clear();
      }
      return true;
    }

    Command? cmd = CommandFor(key);
    if (cmd != null)
    {
      Trigger(cmd);
      return true;
    }

    Tab t = _x.Tab;
    int page = Math.Max(1, ListHeight - 1);
    if (key == Key.CursorUp)
    {
      MoveCursor(t.Cursor - 1);
    }
    else if (key == Key.CursorDown)
    {
      MoveCursor(t.Cursor + 1);
    }
    else if (key == Key.PageUp)
    {
      MoveCursor(t.Cursor - page);
    }
    else if (key == Key.PageDown)
    {
      MoveCursor(t.Cursor + page);
    }
    else if (key == Key.Home)
    {
      MoveCursor(0);
    }
    else if (key == Key.End)
    {
      MoveCursor(t.Rows.Count - 1);
    }
    else if (key == Key.Enter)
    {
      _x.Submit();
    }
    else if (key == Key.Tab)
    {
      _x.Complete();
    }
    else if (key == Key.Backspace)
    {
      _x.Line.Backspace();
      _x.LineChanged();
    }
    else if (key == Key.Backspace.WithAlt || key == Key.W.WithAlt)
    {
      _x.Line.BackspaceWord();
      _x.LineChanged();
    }
    else if (key == Key.Delete)
    {
      _x.Line.DeleteForward();
      _x.LineChanged();
    }
    else if (key == Key.CursorLeft)
    {
      _x.Line.Move(-1);
    }
    else if (key == Key.CursorRight)
    {
      _x.Line.Move(1);
    }
    else if (rune >= 32 && rune < 0xFFFF && !char.IsSurrogate((char)rune) && !key.IsCtrl && !key.IsAlt)
    {
      _x.Line.Insert((char)rune);
      _x.LineChanged();
    }
    else
    {
      return false;
    }
    return true;
  }

  /// <summary>
  /// The command bound to <paramref name="key"/>. Keys without Ctrl/Alt (other than F-keys) only count while the command
  /// line is empty, so typing is never hijacked.
  /// </summary>
  Command? CommandFor(Key key)
  {
    bool plain = !key.IsCtrl && !key.IsAlt && !IsFunctionKey(key);
    if (plain && !_x.Line.IsEmpty)
    {
      return null;
    }
    Command[] all = _x.Commands;
    for (int i = 0; i < all.Length; i++)
    {
      Key[] keys = all[i].Keys;
      for (int k = 0; k < keys.Length; k++)
      {
        if (key == keys[k])
        {
          return all[i];
        }
      }
    }
    return null;
  }

  static bool IsFunctionKey(Key key)
  {
    Key k = key.NoShift.NoCtrl.NoAlt;
    return k == Key.F1 || k == Key.F2 || k == Key.F3 || k == Key.F4 || k == Key.F5 || k == Key.F6 || k == Key.F7 || k == Key.F8 || k == Key.F9 || k == Key.F10 || k == Key.F11 || k == Key.F12;
  }

  /// <summary>A shortcut or bar click: commands that need input pre-fill the command line, the rest run now.</summary>
  void Trigger(Command cmd)
  {
    string? text = cmd.Prefill(_x);
    if (text != null)
    {
      _x.Prefill(text);
    }
    else
    {
      _x.Run(cmd, "");
    }
  }

  bool HandleOverlayKey(Overlay o, Key key)
  {
    int page = Math.Max(1, _overlayBodyHeight - 1);
    if (key == Key.Esc || key == Key.Q.WithCtrl || (!o.IsPicker && key == Key.Enter))
    {
      _x.Overlay = null;
    }
    else if (key == Key.Enter)
    {
      _x.PickOverlay(o.Cursor);
    }
    else if (key == Key.CursorUp)
    {
      MoveOverlay(o, -1);
    }
    else if (key == Key.CursorDown)
    {
      MoveOverlay(o, 1);
    }
    else if (key == Key.PageUp)
    {
      MoveOverlay(o, -page);
    }
    else if (key == Key.PageDown || key == Key.Space)
    {
      MoveOverlay(o, page);
    }
    else if (key == Key.Home)
    {
      MoveOverlay(o, -o.Lines.Count);
    }
    else if (key == Key.End)
    {
      MoveOverlay(o, o.Lines.Count);
    }
    return true;
  }

  void MoveOverlay(Overlay o, int delta)
  {
    int h = Math.Max(1, _overlayBodyHeight);
    if (o.IsPicker)
    {
      o.Cursor = Math.Clamp(o.Cursor + delta, 0, Math.Max(0, o.Lines.Count - 1));
      if (o.Cursor < o.Scroll)
      {
        o.Scroll = o.Cursor;
      }
      if (o.Cursor >= o.Scroll + h)
      {
        o.Scroll = o.Cursor - h + 1;
      }
    }
    else
    {
      o.Scroll = Math.Clamp(o.Scroll + delta, 0, Math.Max(0, o.Lines.Count - h));
    }
  }

  void MoveCursor(int index)
  {
    Tab t = _x.Tab;
    if (t.Rows.Count == 0)
    {
      return;
    }
    t.Cursor = Math.Clamp(index, 0, t.Rows.Count - 1);
  }

  void EnsureCursorVisible()
  {
    Tab t = _x.Tab;
    int h = ListHeight;
    if (t.Cursor < t.Scroll)
    {
      t.Scroll = t.Cursor;
    }
    if (h > 0 && t.Cursor >= t.Scroll + h)
    {
      t.Scroll = t.Cursor - h + 1;
    }
    ClampScroll();
  }

  void ClampScroll()
  {
    Tab t = _x.Tab;
    t.Scroll = Math.Clamp(t.Scroll, 0, Math.Max(0, t.Rows.Count - ListHeight));
  }

  // ---- Mouse ----------------------------------------------------------------------------------

  protected override bool OnMouseEvent(Mouse mouse)
  {
    if (mouse.Position is not System.Drawing.Point pos)
    {
      return false;
    }
    MouseFlags flags = mouse.Flags;
    bool handled = HandleMouse(pos.X, pos.Y, flags);
    if (handled)
    {
      if (_x.QuitRequested)
      {
        App?.RequestStop();
      }
      SetNeedsDraw();
    }
    return handled;
  }

  bool HandleMouse(int mx, int my, MouseFlags flags)
  {
    Overlay? o = _x.Overlay;
    if ((flags & (MouseFlags.WheeledUp | MouseFlags.WheeledDown)) != 0)
    {
      int delta = (flags & MouseFlags.WheeledUp) != 0 ? -3 : 3;
      if (o != null)
      {
        o.Scroll = Math.Clamp(o.Scroll + delta, 0, Math.Max(0, o.Lines.Count - _overlayBodyHeight));
      }
      else
      {
        _x.Tab.Scroll += delta;
        ClampScroll();
      }
      return true;
    }

    bool clicked = (flags & MouseFlags.LeftButtonClicked) != 0;
    bool doubleClicked = (flags & MouseFlags.LeftButtonDoubleClicked) != 0;
    bool middle = (flags & MouseFlags.MiddleButtonClicked) != 0;
    bool right = (flags & MouseFlags.RightButtonClicked) != 0;
    bool ctrl = (flags & MouseFlags.Ctrl) != 0;
    if (!clicked && !doubleClicked && !middle && !right)
    {
      return false;
    }

    if (o != null)
    {
      ClickOverlay(o, mx, my);
      return true;
    }
    int h = Viewport.Height;
    if (_x.Question != null)
    {
      if (my == h - 3 && mx >= _yesX && mx < _yesX + 5)
      {
        _x.Answer(true);
      }
      else if (my == h - 3 && mx >= _noX && mx < _noX + 4)
      {
        _x.Answer(false);
      }
      return true;
    }

    if (my == h - 1)
    {
      int at = HitTest(_barHits, mx);
      if (at >= 0)
      {
        Trigger(_x.Commands[at]);
      }
      return true;
    }
    if (my == 0)
    {
      ClickTabs(mx, middle);
      return true;
    }
    if (my == 1)
    {
      int at = HitTest(_pathHits, mx);
      if (at >= 0 && at < _pathTargets.Count)
      {
        if (middle)
        {
          _x.OpenTab(_pathTargets[at]);
        }
        else
        {
          _x.Go(_pathTargets[at]);
        }
      }
      return true;
    }
    if (mx < _sideWidth && my >= FirstRowY - 1 && my < h - 3)
    {
      int at = HitTest(_placeHits, my);
      if (at >= 0 && at < _x.Places.Count)
      {
        if (middle)
        {
          _x.OpenTab(_x.Places[at].Path);
        }
        else
        {
          _x.Go(_x.Places[at].Path);
        }
      }
      return true;
    }
    if (my == FirstRowY - 1)
    {
      Columns c = ColumnLayout();
      SortBy by = mx >= c.Modified ? SortBy.Modified : mx >= c.Size ? SortBy.Size : SortBy.Name;
      SortCommand.Apply(_x.Tab, by);
      _x.Tab.Rebuild(_x.Filter, _x.ShowHidden);
      return true;
    }

    Tab t = _x.Tab;
    int index = t.Scroll + my - FirstRowY;
    if (my < FirstRowY || my >= FirstRowY + ListHeight || index < 0 || index >= t.Rows.Count)
    {
      return true;
    }
    t.Cursor = index;
    Entry e = t.Rows[index];
    if ((ctrl || right) && !e.IsParent)
    {
      t.ToggleMark(e.Name);
    }
    else if (middle && e.IsDir)
    {
      _x.OpenTab(t.PathOf(e.Name));
    }
    else if (doubleClicked)
    {
      Command? open = _x.Find("open");
      if (open != null)
      {
        _x.Run(open, "");
      }
    }
    return true;
  }

  void ClickTabs(int mx, bool middle)
  {
    if (mx >= _plusX && mx < _plusX + 3)
    {
      _x.OpenTab(_x.Tab.Folder);
      return;
    }
    if (mx >= _closeX && mx < _closeX + 2)
    {
      _x.CloseTab();
      return;
    }
    int at = HitTest(_tabHits, mx);
    if (at < 0)
    {
      return;
    }
    if (middle)
    {
      _x.SwitchTab(at);
      _x.CloseTab();
    }
    else
    {
      _x.SwitchTab(at);
    }
  }

  void ClickOverlay(Overlay o, int mx, int my)
  {
    Box b = _overlayBox;
    if (mx < b.X || mx >= b.X + b.Width || my < b.Y || my >= b.Y + b.Height)
    {
      _x.Overlay = null;
      return;
    }
    int line = o.Scroll + my - b.Y - 1;
    if (o.IsPicker && my > b.Y && my < b.Y + b.Height - 1 && line >= 0 && line < o.Lines.Count)
    {
      _x.PickOverlay(line);
    }
  }

  /// <summary>Index of the [start, end) range containing <paramref name="v"/>, or -1.</summary>
  static int HitTest(List<Hit> hits, int v)
  {
    for (int i = 0; i < hits.Count; i++)
    {
      if (v >= hits[i].Start && v < hits[i].End)
      {
        return hits[i].Index;
      }
    }
    return -1;
  }
}
