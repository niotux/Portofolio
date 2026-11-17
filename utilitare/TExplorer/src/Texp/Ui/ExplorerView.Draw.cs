using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Texp.Commands;
using Texp.Core;
using Texp.Fs;
using Terminal.Gui.Drawing;
using Terminal.Gui.Text;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Texp.Ui;

/// <summary>A clickable range on one line; <see cref="Index"/> says what it stands for.</summary>
public readonly record struct Hit
(
  int Start,
  int End,
  int Index
);

public readonly record struct Box
(
  int X,
  int Y,
  int Width,
  int Height
);

/// <summary>X positions of the list columns for the current width.</summary>
public struct Columns
{
  public const int SizeWidth = 8;
  public const int ModifiedWidth = 12;

  public int Mark;
  public int Name;
  public int NameWidth;
  public int Size;
  public int Modified;
}

public sealed partial class ExplorerView
{
  /// <summary>Rows: 0 tabs, 1 path, 2 column header, 3.. files; then status, command line, shortcut bar.</summary>
  const int FirstRowY = 3;
  const int SideWidth = 18;
  const int MinWidthForSide = 64;

  readonly List<Hit> _barHits = new();
  readonly List<Hit> _tabHits = new();
  readonly List<Hit> _pathHits = new();
  readonly List<string> _pathTargets = new();
  readonly List<string> _crumbLabels = new();
  readonly List<Hit> _placeHits = new();
  int _plusX = -1;
  int _closeX = -1;
  int _yesX = -1;
  int _noX = -1;
  int _sideWidth;
  Box _overlayBox;
  int _overlayBodyHeight = 10;

  int ListHeight
  {
    get { return Math.Max(0, Viewport.Height - FirstRowY - 3); }
  }

  protected override bool OnDrawingContent(DrawContext? context)
  {
    int w = Viewport.Width;
    int h = Viewport.Height;
    _sideWidth = w >= MinWidthForSide ? SideWidth : 0;
    ClampScroll();
    DrawTabs(w);
    DrawPath(w);
    DrawSidebar(h);
    DrawList(w);
    DrawStatus(w, h - 3);
    DrawCommandLine(w, h - 2);
    DrawBar(w, h - 1);
    if (_x.Overlay != null)
    {
      DrawOverlay(_x.Overlay, w, h);
    }
    return true;
  }

  Columns ColumnLayout()
  {
    int w = Viewport.Width;
    Columns c = new();
    c.Mark = _sideWidth + (_sideWidth > 0 ? 1 : 0);
    c.Name = c.Mark + 2;
    c.Modified = w - 1 - Columns.ModifiedWidth;
    c.Size = c.Modified - 1 - Columns.SizeWidth;
    c.NameWidth = Math.Max(0, c.Size - 1 - c.Name);
    return c;
  }

  // ---- Top: tabs and path ---------------------------------------------------------------------

  void DrawTabs(int w)
  {
    Put(0, 0, "", w, Theme.Normal);
    _tabHits.Clear();
    _closeX = -1;
    int x = 0;
    Span<char> num = stackalloc char[8];
    for (int i = 0; i < _x.Tabs.Count && x < w; i++)
    {
      bool on = i == _x.Active;
      Attribute attr = on ? Theme.TabOn : Theme.TabOff;
      int start = x;
      (i + 1).TryFormat(num, out int n, default, CultureInfo.InvariantCulture);
      x = Seg(x, 0, " ", w, attr);
      x = Seg(x, 0, num.Slice(0, n), w, on ? new Attribute(Theme.Yellow, Theme.TabBg) : attr);
      x = Seg(x, 0, " ", w, attr);
      x = Seg(x, 0, Clip(_x.Tabs[i].Title(), 20), w, attr);
      x = Seg(x, 0, " ", w, attr);
      if (on && _x.Tabs.Count > 1)
      {
        _closeX = x;
        x = Seg(x, 0, "× ", w, new Attribute(Theme.Dim, Theme.TabBg));
      }
      _tabHits.Add(new Hit(start, x, i));
      x = Seg(x, 0, " ", w, Theme.Normal);
    }
    _plusX = x;
    x = Seg(x, 0, "[+]", w, Theme.Faint);

    // Right side: what's on the clipboard, so paste is never a surprise.
    if (_x.Clipboard.Count > 0)
    {
      StringBuilder sb = new(48);
      sb.Append(_x.ClipboardIsCut ? "cut: " : "copied: ");
      if (_x.Clipboard.Count == 1)
      {
        sb.Append(Path.GetFileName(_x.Clipboard[0]));
      }
      else
      {
        sb.Append(_x.Clipboard.Count).Append(" items");
      }
      sb.Append("  ^V paste ");
      string text = sb.ToString();
      int at = Math.Max(x + 2, w - text.Length);
      Seg(at, 0, text, w, Theme.Warn);
    }
  }

  void DrawPath(int w)
  {
    Put(0, 1, "", w, Theme.Normal);
    _pathHits.Clear();
    _pathTargets.Clear();

    // Collect the crumbs first: "~" or "/", then one per folder below it.
    string folder = _x.Tab.Folder;
    string home = Places.Home();
    List<string> labels = _crumbLabels;
    labels.Clear();
    ReadOnlySpan<char> rest;
    int consumed;
    if (FileOps.IsInside(folder, home) && home != "/")
    {
      labels.Add("~");
      _pathTargets.Add(home);
      rest = folder.AsSpan(home.Length);
      consumed = home.Length;
    }
    else
    {
      labels.Add("/");
      _pathTargets.Add("/");
      rest = folder.AsSpan(1);
      consumed = 0;
    }
    rest = rest.TrimStart('/');
    while (!rest.IsEmpty)
    {
      int slash = rest.IndexOf('/');
      ReadOnlySpan<char> part = slash < 0 ? rest : rest.Slice(0, slash);
      consumed += 1 + part.Length;
      labels.Add(part.ToString());
      _pathTargets.Add(folder.AsSpan(0, consumed).ToString());
      rest = slash < 0 ? ReadOnlySpan<char>.Empty : rest.Slice(slash + 1);
    }

    // Too long: keep the root and the deepest folders, hide the middle behind "…".
    int width = 1;
    for (int i = 0; i < labels.Count; i++)
    {
      width += Cells(labels[i]) + (i > 0 ? 3 : 0);
    }
    int skipFrom = labels.Count;
    int first = 1;
    while (width > w - 2 && first < labels.Count - 1)
    {
      width -= Cells(labels[first]) + 3;
      if (skipFrom == labels.Count)
      {
        width += 4;
        skipFrom = first;
      }
      first++;
    }

    int x = 1;
    for (int i = 0; i < labels.Count; i++)
    {
      if (i >= skipFrom && i < first)
      {
        if (i == skipFrom)
        {
          x = Seg(x, 1, " › …", w, Theme.Faint);
        }
        continue;
      }
      if (i > 0)
      {
        x = Seg(x, 1, " › ", w, Theme.Faint);
      }
      int start = x;
      x = Seg(x, 1, labels[i], w, i == labels.Count - 1 ? Theme.PathHere : Theme.Path);
      _pathHits.Add(new Hit(start, x, i));
    }

    Tab t = _x.Tab;
    StringBuilder sb = new(40);
    sb.Append(t.ItemCount).Append(t.ItemCount == 1 ? " item" : " items");
    if (!_x.ShowHidden)
    {
      int hidden = 0;
      for (int i = 0; i < t.All.Count; i++)
      {
        if (t.All[i].IsHidden)
        {
          hidden++;
        }
      }
      if (hidden > 0)
      {
        sb.Append(" (+").Append(hidden).Append(" hidden)");
      }
    }
    sb.Append(' ');
    string count = sb.ToString();
    if (x + 2 + count.Length <= w)
    {
      Put(w - count.Length, 1, count, count.Length, Theme.Faint);
    }
  }

  // ---- Places ---------------------------------------------------------------------------------

  void DrawSidebar(int h)
  {
    _placeHits.Clear();
    if (_sideWidth == 0)
    {
      return;
    }
    int bottom = h - 3;
    Put(0, FirstRowY - 1, " Places", _sideWidth, Theme.Side);
    int y = FirstRowY;
    PlaceKind last = PlaceKind.Home;
    string folder = _x.Tab.Folder;
    Span<char> label = stackalloc char[SideWidth];
    for (int i = 0; i < _x.Places.Count && y < bottom; i++)
    {
      Place p = _x.Places[i];
      // A gap between groups: home dirs, bookmarks, then the system and drives.
      if (i > 0 && GroupOf(p.Kind) != GroupOf(last) && y < bottom - 1)
      {
        Put(0, y++, "", _sideWidth, Theme.SideText);
      }
      last = p.Kind;
      bool here = folder == p.Path;
      label[0] = ' ';
      label[1] = p.Icon;
      label[2] = ' ';
      Put(0, y, label.Slice(0, 3), 3, here ? Theme.SideHere : Theme.Side);
      Put(3, y, p.Label, _sideWidth - 3, here ? Theme.SideHere : Theme.SideText);
      _placeHits.Add(new Hit(y, y + 1, i));
      y++;
    }
    while (y < bottom)
    {
      Put(0, y++, "", _sideWidth, Theme.SideText);
    }
    for (int r = FirstRowY - 1; r < bottom; r++)
    {
      Put(_sideWidth, r, "│", 1, Theme.Faint);
    }
  }

  static int GroupOf(PlaceKind kind)
  {
    return kind == PlaceKind.Home || kind == PlaceKind.UserDir ? 0 : kind == PlaceKind.Bookmark ? 1 : 2;
  }

  // ---- The file list --------------------------------------------------------------------------

  void DrawList(int w)
  {
    Columns c = ColumnLayout();
    Tab t = _x.Tab;
    int left = c.Mark;
    int listWidth = w - left;

    // Header, with an arrow on the sort column.
    Put(left, FirstRowY - 1, "", listWidth, Theme.Header);
    ReadOnlySpan<char> arrow = t.Order.Descending ? " ▼" : " ▲";
    int x = Seg(c.Name, FirstRowY - 1, "Name", w, Theme.Header);
    if (t.Order.By == SortBy.Name)
    {
      Seg(x, FirstRowY - 1, arrow, w, Theme.Header);
    }
    PutRight(c.Size, FirstRowY - 1, t.Order.By == SortBy.Size ? (t.Order.Descending ? "▼ Size" : "▲ Size") : "Size", Columns.SizeWidth, Theme.Header);
    x = Seg(c.Modified + 1, FirstRowY - 1, "Modified", w, Theme.Header);
    if (t.Order.By == SortBy.Modified)
    {
      Seg(x, FirstRowY - 1, arrow, w, Theme.Header);
    }

    int h = ListHeight;
    DateTime now = DateTime.Now;
    Span<char> buf = stackalloc char[24];
    for (int r = 0; r < h; r++)
    {
      int y = FirstRowY + r;
      int i = t.Scroll + r;
      if (i >= t.Rows.Count)
      {
        Put(left, y, "", listWidth, Theme.Normal);
        if (i == t.Rows.Count && t.ItemCount == 0)
        {
          DrawEmptyHint(c.Name, y, w);
        }
        continue;
      }
      Entry e = t.Rows[i];
      bool cursor = i == t.Cursor;
      if (e.IsParent)
      {
        Put(left, y, "", listWidth, cursor ? Theme.Cursor : Theme.Normal);
        PutIcon(c.Mark, y, Icons.Parent, cursor);
        x = Seg(c.Name, y, "..", w, cursor ? Theme.Cursor : Theme.Folder);
        Seg(x + 2, y, "parent folder", c.Name + c.NameWidth, cursor ? Theme.Cursor : Theme.Faint);
        continue;
      }
      bool marked = t.IsMarked(e.Name);
      Attribute nameAttr = marked ? Theme.Marked : e.IsDir ? Theme.Folder : e.IsLink ? Theme.Link : e.IsExecutable ? Theme.Exec : e.IsHidden ? Theme.Faint : Theme.Normal;
      Attribute plain = Theme.Normal;
      if (cursor)
      {
        nameAttr = marked ? Theme.CursorMarked : Theme.Cursor;
        plain = Theme.Cursor;
      }
      Put(left, y, "", listWidth, plain);
      PutIcon(c.Mark, y, marked ? Icons.Marked : Icons.For(e), cursor);
      int end = Seg(c.Name, y, e.Name, c.Name + c.NameWidth, nameAttr);
      if (e.IsDir && end < c.Name + c.NameWidth)
      {
        Seg(end, y, "/", c.Name + c.NameWidth, nameAttr);
      }
      if (!e.IsDir)
      {
        int n = Format.Bytes(e.Size, buf);
        PutRight(c.Size, y, buf.Slice(0, n), Columns.SizeWidth, cursor ? plain : Theme.Normal);
      }
      int d = Format.Date(e.Modified, now, buf);
      Put(c.Modified + 1, y, buf.Slice(0, d), Columns.ModifiedWidth, cursor ? plain : Theme.Faint);
    }
  }

  void DrawEmptyHint(int x, int y, int w)
  {
    Tab t = _x.Tab;
    if (t.Error != null)
    {
      Seg(x, y, t.Error, w, Theme.Error);
    }
    else if (!_x.Filter.IsEmpty)
    {
      Seg(x, y, "nothing here matches the filter (Esc clears it)", w, Theme.Faint);
    }
    else if (t.All.Count > 0)
    {
      Seg(x, y, "only hidden files here (^H shows them)", w, Theme.Faint);
    }
    else
    {
      Seg(x, y, "empty folder", w, Theme.Faint);
    }
  }

  // ---- Bottom: status, command line, shortcut bar ---------------------------------------------

  void DrawStatus(int w, int y)
  {
    Put(0, y, "", w, Theme.Normal);
    _yesX = -1;
    _noX = -1;
    if (_x.Question != null)
    {
      int x = Seg(1, y, _x.Question, w - 12, Theme.Warn);
      _yesX = x + 1;
      Put(_yesX, y, " Yes ", 5, new Attribute(Theme.Black, Theme.Green));
      _noX = _yesX + 6;
      Put(_noX, y, " No ", 4, new Attribute(Theme.Black, Theme.Red));
      return;
    }
    if (_x.Jobs.Count > 0)
    {
      DrawProgress(_x.Jobs[0], w, y);
      return;
    }
    if (_x.HasMessage)
    {
      Put(1, y, _x.Message ?? "", w - 2, _x.MessageIsError ? Theme.Error : Theme.Ok);
      return;
    }
    Tab t = _x.Tab;
    if (t.Marked.Count > 0)
    {
      long total = 0;
      for (int i = 0; i < t.All.Count; i++)
      {
        if (!t.All[i].IsDir && t.IsMarked(t.All[i].Name))
        {
          total += t.All[i].Size;
        }
      }
      Put(1, y, t.Marked.Count + " marked (" + Format.Bytes(total) + " in files) · Esc unmarks", w - 2, Theme.Marked);
      return;
    }
    if (t.HasItem)
    {
      Entry e = t.Current;
      Span<char> mode = stackalloc char[9];
      Format.Mode(e.Mode, mode);
      int x = Seg(1, y, mode, w, Theme.Faint);
      x = Seg(x + 2, y, e.Name, w, Theme.Faint);
      if (!e.IsDir)
      {
        Seg(x + 2, y, Format.Bytes(e.Size), w, Theme.Faint);
      }
    }
  }

  void DrawProgress(Job job, int w, int y)
  {
    long total = job.Total;
    long done = job.Done;
    double frac = total > 0 ? Math.Clamp((double)done / total, 0, 1) : 0;
    int x = Seg(1, y, job.Label, w, Theme.Warn);
    x = Seg(x, y, "  ", w, Theme.Normal);
    const int barWidth = 24;
    int filled = (int)(frac * barWidth);
    Span<char> bar = stackalloc char[barWidth];
    for (int i = 0; i < barWidth; i++)
    {
      bar[i] = i < filled ? '█' : '░';
    }
    x = Seg(x, y, bar, w, Theme.Ok);
    Span<char> pct = stackalloc char[8];
    ((int)(frac * 100)).TryFormat(pct, out int n, default, CultureInfo.InvariantCulture);
    pct[n++] = '%';
    x = Seg(x + 1, y, pct.Slice(0, n), w, Theme.Normal);
    string more = _x.Jobs.Count > 1 ? "  +" + (_x.Jobs.Count - 1) + " more" : "";
    Seg(x, y, more + "   type cancel to stop", w, Theme.Faint);
  }

  void DrawCommandLine(int w, int y)
  {
    Put(0, y, "", w, Theme.Normal);
    int x = Seg(1, y, "› ", w, Theme.Prompt);
    int textX = x;
    ReadOnlySpan<char> text = _x.Line.Text;
    if (text.IsEmpty)
    {
      Put(x, y, " ", 1, Theme.Caret);
      Seg(x + 2, y, "type a command (try help) or part of a name to filter", w, Theme.Faint);
      return;
    }

    Command? cmd = _x.LineCommand(out ReadOnlySpan<char> args);
    Attribute rest = cmd != null ? Theme.Normal : _x.LineIsPath ? Theme.PathText : Theme.FilterText;
    int wordEnd = 0;
    if (cmd != null)
    {
      int lead = text.Length - text.TrimStart(' ').Length;
      int space = text.Slice(lead).IndexOf(' ');
      wordEnd = space < 0 ? text.Length : lead + space;
    }
    x = Seg(x, y, text.Slice(0, wordEnd), w, Theme.Command);
    x = Seg(x, y, text.Slice(wordEnd), w, rest);

    // Ghost completion: the rest of the command name being typed; Tab accepts it.
    Command? suggestion = cmd == null ? _x.Suggestion() : null;
    char ghost = ' ';
    if (suggestion != null && suggestion.Name.Length > text.Length)
    {
      ghost = suggestion.Name[text.Length];
      x = Seg(x, y, suggestion.Name.AsSpan(text.Length), w, Theme.Faint);
    }

    int caretX = textX + _x.Line.Cursor;
    if (caretX < w)
    {
      char under = _x.Line.Cursor < text.Length ? text[_x.Line.Cursor] : ghost;
      Put(caretX, y, new ReadOnlySpan<char>(ref under), 1, Theme.Caret);
      x = Math.Max(x, caretX + 1);
    }

    string hint;
    if (cmd != null)
    {
      hint = cmd.Hint(_x, args);
    }
    else if (_x.LineIsPath)
    {
      hint = Directory.Exists(_x.ResolvePath(text)) ? "Enter: go there" : "no such folder";
    }
    else if (suggestion != null)
    {
      hint = "Tab: " + suggestion.Name + " · " + suggestion.Help;
    }
    else
    {
      hint = "filter · Enter opens the highlighted entry";
    }
    x = Seg(x + 2, y, "— ", w, Theme.Faint);
    Seg(x, y, hint, w, Theme.Faint);
  }

  void DrawBar(int w, int y)
  {
    Put(0, y, "", w, Theme.KeyText);
    _barHits.Clear();
    int x = 0;
    Command[] all = _x.Commands;
    for (int i = 0; i < all.Length && x < w; i++)
    {
      Command c = all[i];
      string? label = c.Bar;
      if (label == null || c.Keys.Length == 0)
      {
        continue;
      }
      int start = x;
      x = Seg(x, y, Command.KeyLabel(c.Keys[0]), w, Theme.KeyName);
      x = Seg(x, y, " ", w, Theme.KeyText);
      x = Seg(x, y, label, w, Theme.KeyText);
      _barHits.Add(new Hit(start, x, i));
      x = Seg(x, y, "  ", w, Theme.KeyText);
    }
  }

  // ---- Overlay box ----------------------------------------------------------------------------

  void DrawOverlay(Overlay o, int w, int h)
  {
    int longest = o.Title.Length + 6;
    for (int i = 0; i < o.Lines.Count; i++)
    {
      longest = Math.Max(longest, o.Lines[i].Length + 4);
    }
    int bw = Math.Clamp(longest, Math.Min(40, w), Math.Max(4, w - 4));
    int bh = Math.Clamp(o.Lines.Count + 2, 3, Math.Max(3, h - 4));
    int bx = Math.Max(0, (w - bw) / 2);
    int by = Math.Max(0, (h - bh) / 2);
    _overlayBox = new Box(bx, by, bw, bh);
    _overlayBodyHeight = bh - 2;

    // Border with the title on top and what keys do at the bottom.
    Span<char> line = stackalloc char[Math.Max(1, bw)];
    line.Fill('─');
    line[0] = '┌';
    line[bw - 1] = '┐';
    Put(bx, by, line, bw, Theme.BoxBorder);
    Seg(bx + 2, by, " " + o.Title + " ", bx + bw - 1, Theme.BoxBorder);
    line[0] = '└';
    line[bw - 1] = '┘';
    Put(bx, by + bh - 1, line, bw, Theme.BoxBorder);
    Seg(bx + 2, by + bh - 1, o.IsPicker ? " ↑↓ Enter: pick · Esc: cancel " : " Esc: close ", bx + bw - 1, Theme.BoxBorder);

    o.Scroll = Math.Clamp(o.Scroll, 0, Math.Max(0, o.Lines.Count - _overlayBodyHeight));
    for (int r = 0; r < _overlayBodyHeight; r++)
    {
      int y = by + 1 + r;
      int i = o.Scroll + r;
      Put(bx, y, "│", 1, Theme.BoxBorder);
      Put(bx + bw - 1, y, "│", 1, Theme.BoxBorder);
      Attribute attr = o.IsPicker && i == o.Cursor ? Theme.BoxPick : Theme.Box;
      Put(bx + 1, y, " ", 1, attr);
      Put(bx + 2, y, i < o.Lines.Count ? o.Lines[i] : "", bw - 3, attr);
    }
  }

  void PutIcon(int x, int y, Icon icon, bool cursor)
  {
    char glyph = icon.Glyph;
    Put(x, y, new ReadOnlySpan<char>(ref glyph), 1, new Attribute(icon.Color, cursor ? Theme.SelectBg : Theme.Bg));
    Put(x + 1, y, " ", 1, cursor ? Theme.Cursor : Theme.Normal);
  }

  // ---- Primitives -----------------------------------------------------------------------------

  static string Clip(string s, int max)
  {
    return s.Length <= max ? s : string.Concat(s.AsSpan(0, max - 1), "…");
  }

  /// <summary>Number of terminal cells <paramref name="text"/> takes (wide characters count 2).</summary>
  static int Cells(ReadOnlySpan<char> text)
  {
    int cells = 0;
    int i = 0;
    while (i < text.Length)
    {
      if (Rune.DecodeFromUtf16(text.Slice(i), out Rune r, out int used) != System.Buffers.OperationStatus.Done)
      {
        r = new Rune('?');
        used = 1;
      }
      cells += Math.Max(0, r.GetColumns());
      i += used;
    }
    return cells;
  }

  /// <summary>Writes <paramref name="text"/> left-aligned into exactly <paramref name="width"/> cells (padded, or cut with '…').</summary>
  void Put(int x, int y, ReadOnlySpan<char> text, int width, Attribute attr)
  {
    if (width <= 0)
    {
      return;
    }
    SetAttribute(attr);
    Move(x, y);
    bool cut = Cells(text) > width;
    int room = cut ? width - 1 : width;
    int used = 0;
    int i = 0;
    while (i < text.Length)
    {
      if (Rune.DecodeFromUtf16(text.Slice(i), out Rune r, out int units) != System.Buffers.OperationStatus.Done)
      {
        r = new Rune('?');
        units = 1;
      }
      i += units;
      int cols = r.GetColumns();
      if (cols <= 0 || r.Value < 32)
      {
        // Control and zero-width characters would break the layout; show them as '?'.
        r = new Rune('?');
        cols = 1;
      }
      if (used + cols > room)
      {
        break;
      }
      AddRune(r);
      used += cols;
    }
    if (cut)
    {
      AddRune('…');
      used++;
    }
    for (; used < width; used++)
    {
      AddRune(' ');
    }
  }

  void PutRight(int x, int y, ReadOnlySpan<char> text, int width, Attribute attr)
  {
    int cells = Cells(text);
    if (cells >= width)
    {
      Put(x, y, text, width, attr);
      return;
    }
    Put(x, y, "", width - cells, attr);
    Put(x + width - cells, y, text, cells, attr);
  }

  /// <summary>Writes text at x without padding, stopping at <paramref name="maxX"/>; returns the x after it.</summary>
  int Seg(int x, int y, ReadOnlySpan<char> text, int maxX, Attribute attr)
  {
    int room = maxX - x;
    if (room <= 0 || text.IsEmpty)
    {
      return x;
    }
    int len = Math.Min(Cells(text), room);
    Put(x, y, text, len, attr);
    return x + len;
  }
}
