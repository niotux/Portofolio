using System;
using System.Globalization;
using Dtm.Docker;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Dtm.Ui;

public enum Lever
{
  Off,
  Busy,
  Partial,
  On
}

/// <summary>X positions of the list columns for the current width.</summary>
public struct Columns
{
  public const int ContextWidth = 14;
  public const int StatusWidth = 20;
  public const int CpuWidth = 6;
  public const int MemWidth = 7;
  public const int GpuWidth = 5;
  public const int IdWidth = 12;
  public const int SizeWidth = 9;
  public const int CreatedWidth = 15;

  public int Lever;
  public int Delete;
  public int Name;
  public int NameWidth;
  public int Context;
  // Containers: Status, Cpu, Mem, Gpu. Images: Status = ID, Cpu = SIZE, Mem = CREATED.
  public int Status;
  public int Cpu;
  public int Mem;
  public int Gpu;
}

public sealed partial class DashboardView
{
  static readonly string[] HelpLines =
  [
    "Docker Task Manager",
    "",
    "Everything in every Docker context, in one list. Contexts come from `docker context ls`.",
    "",
    "Mouse",
    "  [──┃] lever        click to start / stop (a compose row switches the whole project)",
    "  ✕                  delete (asks first; a compose project gets `docker compose down`)",
    "  ▼ / ▶              expand / collapse a compose project",
    "  column header      sort by that column (click again to reverse)",
    "  double-click       expand a project, or open a container's logs",
    "  Filter / Show      click to edit the filter / toggle running-only",
    "  wheel              scroll",
    "",
    "Keys",
    "  ↑ ↓ PgUp PgDn      move            ← →     collapse / expand",
    "  Space              flip lever      Enter   expand / logs",
    "  / or F4            filter          Esc     clear filter",
    "  F2 images   F3 logs   F5 start   F6 stop   F7 restart   F8/Del delete",
    "  F9 running-only / all   F10 or q quit",
    "",
    "Lever:  [┃──] off   [─┃─] working   [──┃] on (yellow: project partly running)",
    "",
    "Esc to close",
  ];

  static readonly Key[] FunctionKeys = [Key.F1, Key.F2, Key.F3, Key.F4, Key.F5, Key.F6, Key.F7, Key.F8, Key.F9, Key.F10];

  const int CodeClose = 100;
  const int CodeReload = 101;
  const int MaxBar = 10;
  readonly int[] _barStart = new int[MaxBar];
  readonly int[] _barEnd = new int[MaxBar];
  readonly int[] _barCode = new int[MaxBar];
  int _barCount;
  int _confirmYesX = -1;
  int _confirmNoX = -1;
  int _showToggleX = -1;
  int _filterX = -1;

  protected override bool OnDrawingContent(DrawContext? context)
  {
    int h = Viewport.Height;
    switch (_mode)
    {
      case Mode.Logs:
        DrawLogs(h);
        break;
      case Mode.Help:
        DrawHelp(h);
        break;
      default:
        DrawMeters();
        DrawColumnHeader();
        DrawRows();
        DrawStatusLine(h - 2);
        DrawFunctionBar(h - 1);
        break;
    }
    return true;
  }

  Columns ColumnLayout()
  {
    int w = Viewport.Width;
    Columns c = new();
    c.Lever = 1;
    c.Delete = 7;
    c.Name = 9;
    if (_mode == Mode.Images || (_mode != Mode.Containers && _returnMode == Mode.Images))
    {
      c.Mem = w - 1 - Columns.CreatedWidth;
      c.Cpu = c.Mem - 1 - Columns.SizeWidth;
      c.Status = c.Cpu - 1 - Columns.IdWidth;
      c.Gpu = w;
    }
    else
    {
      c.Gpu = w - 1 - Columns.GpuWidth;
      c.Mem = c.Gpu - 1 - Columns.MemWidth;
      c.Cpu = c.Mem - 1 - Columns.CpuWidth;
      c.Status = c.Cpu - 1 - Columns.StatusWidth;
    }
    c.Context = c.Status - 1 - Columns.ContextWidth;
    c.NameWidth = c.Context - 1 - c.Name;
    return c;
  }

  SortColumn ColumnAt(int x)
  {
    Columns c = ColumnLayout();
    bool images = _mode == Mode.Images;
    if (x >= c.Gpu && !images)
    {
      return SortColumn.Gpu;
    }
    if (x >= c.Mem && !images)
    {
      return SortColumn.Mem;
    }
    if (x >= c.Cpu && !images)
    {
      return SortColumn.Cpu;
    }
    if (x >= c.Status && !images)
    {
      return SortColumn.Status;
    }
    if (x >= c.Context)
    {
      return SortColumn.Context;
    }
    return SortColumn.Name;
  }

  int FunctionKeyAt(int x)
  {
    for (int i = 0; i < _barCount; i++)
    {
      if (x >= _barStart[i] && x < _barEnd[i])
      {
        return _barCode[i];
      }
    }
    return 0;
  }

  // ---- Header: meters and summary -------------------------------------------------------------

  void DrawMeters()
  {
    int w = Viewport.Width;
    ContextPoller[] pollers = _fleet.Pollers;
    double cpuSum = 0;
    int cpus = 0;
    long mem = 0;
    long memTotal = 0;
    double gpu = -1;
    int running = 0;
    int stopped = 0;
    int projects = 0;
    int images = 0;
    for (int p = 0; p < pollers.Length; p++)
    {
      ContextSnapshot s = pollers[p].Snapshot;
      cpus += s.Cpus;
      memTotal += s.MemTotal;
      images += s.Images.Count;
      string? lastProject = null;
      for (int i = 0; i < s.Containers.Count; i++)
      {
        Container c = s.Containers[i];
        if (c.Project != null && c.Project != lastProject)
        {
          projects++;
          lastProject = c.Project;
        }
        if (!c.IsRunning)
        {
          stopped++;
          continue;
        }
        running++;
        cpuSum += c.Cpu;
        mem += c.MemBytes;
        if (c.Gpu > gpu)
        {
          gpu = c.Gpu;
        }
      }
    }

    int meterWidth = Math.Max(10, w / 2 - 6);
    Span<char> buf = stackalloc char[32];
    int n;

    double cpuFraction = cpus > 0 ? cpuSum / (cpus * 100.0) : -1;
    n = cpus > 0 ? FormatPercent(cpuSum / cpus, buf) : Copy("n/a", buf);
    if (cpus > 0)
    {
      buf[n++] = '%';
    }
    Meter(0, "CPU", cpuFraction, buf.Slice(0, n), meterWidth);

    double memFraction = memTotal > 0 ? (double)mem / memTotal : -1;
    n = FormatBytes(mem, buf);
    buf[n++] = '/';
    n += memTotal > 0 ? FormatBytes(memTotal, buf.Slice(n)) : Copy("?", buf.Slice(n));
    Meter(1, "Mem", memFraction, buf.Slice(0, n), meterWidth);

    n = gpu >= 0 ? FormatPercent(gpu, buf) : Copy("n/a", buf);
    if (gpu >= 0)
    {
      buf[n++] = '%';
    }
    Meter(2, "GPU", gpu >= 0 ? gpu / 100.0 : -1, buf.Slice(0, n), meterWidth);

    int rx = meterWidth + 8;
    int end = w;

    // Line 0: contexts, colored by health.
    int x = Seg(rx, 0, "Contexts: ", end, Theme.Label);
    if (_fleet.Error != null)
    {
      x = Seg(x, 0, "docker: ", end, Theme.Error);
      x = Seg(x, 0, _fleet.Error, end, Theme.Error);
    }
    else if (pollers.Length == 0)
    {
      x = Seg(x, 0, "loading…", end, Theme.Faint);
    }
    for (int p = 0; p < pollers.Length; p++)
    {
      ContextSnapshot s = pollers[p].Snapshot;
      Attribute a = !s.Loaded ? Theme.Faint : s.Error != null ? Theme.Error : Theme.Ok;
      x = Seg(x, 0, s.Name, end, a);
      x = Seg(x, 0, " ", end, Theme.Normal);
    }
    Seg(x, 0, "", end, Theme.Normal);

    // Line 1: counts.
    x = Seg(rx, 1, "Running: ", end, Theme.Label);
    n = FormatInt(running, buf);
    x = Seg(x, 1, buf.Slice(0, n), end, Theme.Ok);
    x = Seg(x, 1, "  Stopped: ", end, Theme.Label);
    n = FormatInt(stopped, buf);
    x = Seg(x, 1, buf.Slice(0, n), end, stopped > 0 ? Theme.Warn : Theme.Faint);
    x = Seg(x, 1, "  Projects: ", end, Theme.Label);
    n = FormatInt(projects, buf);
    x = Seg(x, 1, buf.Slice(0, n), end, Theme.Normal);
    x = Seg(x, 1, "  Images: ", end, Theme.Label);
    n = FormatInt(images, buf);
    x = Seg(x, 1, buf.Slice(0, n), end, Theme.Normal);
    Seg(x, 1, "", end, Theme.Normal);

    // Line 2: filter and running-only toggle (both clickable).
    x = Seg(rx, 2, "Filter: ", end, Theme.Label);
    _filterX = x;
    const int filterWidth = 18;
    if (_filterLength == 0)
    {
      Put(x, 2, "(none)", filterWidth, Theme.Faint);
    }
    else
    {
      Put(x, 2, Filter, filterWidth, Theme.Warn);
    }
    x += filterWidth + 2;
    x = Seg(x, 2, "Show: ", end, Theme.Label);
    _showToggleX = x;
    x = Seg(x, 2, _runningOnly ? "[Running]" : "[All]    ", end, Theme.Normal);
    Seg(x, 2, "", end, Theme.Normal);
  }

  void Meter(int y, ReadOnlySpan<char> label, double fraction, ReadOnlySpan<char> value, int width)
  {
    Put(0, y, " ", 1, Theme.Normal);
    Put(1, y, label, 3, Theme.Label);
    Put(4, y, "[", 1, Theme.Normal);
    int inner = Math.Min(width, 256);
    int bars = fraction < 0 ? 0 : (int)Math.Round(Math.Clamp(fraction, 0, 1) * inner);
    Attribute barAttr = fraction > 0.85 ? Theme.Error : fraction > 0.6 ? Theme.Warn : Theme.Ok;
    int valueStart = inner - value.Length;
    SetAttribute(barAttr);
    Move(5, y);
    for (int i = 0; i < inner; i++)
    {
      if (i >= valueStart)
      {
        SetAttribute(Theme.Faint);
        AddRune(value[i - valueStart]);
      }
      else
      {
        AddRune(i < bars ? '|' : ' ');
      }
    }
    Put(5 + inner, y, "]", 1, Theme.Normal);
    Put(6 + inner, y, "", 2, Theme.Normal);
  }

  void DrawColumnHeader()
  {
    int w = Viewport.Width;
    int y = FirstRowY - 1;
    Columns c = ColumnLayout();
    Put(0, y, "", w, Theme.Header);
    bool images = _mode == Mode.Images;
    if (!images)
    {
      Put(c.Lever, y, "POWER", 5, Theme.Header);
    }
    HeaderCell(c.Name, y, images ? "IMAGE" : "NAME", c.NameWidth, SortColumn.Name, false);
    HeaderCell(c.Context, y, "CONTEXT", Columns.ContextWidth, SortColumn.Context, false);
    if (images)
    {
      Put(c.Status, y, "ID", Columns.IdWidth, Theme.Header);
      PutRight(c.Cpu, y, "SIZE", Columns.SizeWidth, Theme.Header);
      Put(c.Mem + 1, y, "CREATED", Columns.CreatedWidth, Theme.Header);
    }
    else
    {
      HeaderCell(c.Status, y, "STATUS", Columns.StatusWidth, SortColumn.Status, false);
      HeaderCell(c.Cpu, y, "CPU%", Columns.CpuWidth, SortColumn.Cpu, true);
      HeaderCell(c.Mem, y, "MEM", Columns.MemWidth, SortColumn.Mem, true);
      HeaderCell(c.Gpu, y, "GPU%", Columns.GpuWidth, SortColumn.Gpu, true);
    }
  }

  void HeaderCell(int x, int y, ReadOnlySpan<char> title, int width, SortColumn column, bool right)
  {
    Span<char> buf = stackalloc char[16];
    title.CopyTo(buf);
    int n = title.Length;
    if (_comparer.Column == column)
    {
      buf[n++] = _comparer.Descending ? '▲' : '▼';
    }
    if (right)
    {
      PutRight(x, y, buf.Slice(0, n), width, Theme.Header);
    }
    else
    {
      Put(x, y, buf.Slice(0, n), width, Theme.Header);
    }
  }

  // ---- Rows -----------------------------------------------------------------------------------

  void DrawRows()
  {
    int w = Viewport.Width;
    int h = ListHeight;
    Columns c = ColumnLayout();
    for (int r = 0; r < h; r++)
    {
      int y = FirstRowY + r;
      int index = _scroll + r;
      if (index >= _rows.Count)
      {
        if (index == 0 && r == 0)
        {
          Put(0, y, _fleet.Pollers.Length == 0 ? "  waiting for docker…" : _filterLength > 0 ? "  nothing matches the filter" : "  nothing here", w, Theme.Faint);
        }
        else
        {
          Put(0, y, "", w, Theme.Normal);
        }
        continue;
      }
      DrawRow(_rows[index], y, index == _selected, c, w);
    }
  }

  void DrawRow(Row row, int y, bool selected, Columns c, int w)
  {
    Color bg = selected ? Theme.SelectBg : Theme.Bg;
    bool busy = row.Kind != RowKind.Image && row.Kind != RowKind.ContextError && IsBusy(row);
    bool alive = row.Running > 0 || row.Kind == RowKind.Image;
    Color fg = selected ? Theme.Black : alive ? Theme.Text : Theme.Dim;
    Attribute text = new(fg, bg);
    Put(0, y, "", w, text);
    Span<char> buf = stackalloc char[32];
    int n;

    if (row.Kind == RowKind.ContextError)
    {
      Attribute err = new(selected ? Theme.Black : Theme.Red, bg);
      int x = Seg(c.Name, y, "⚠ ", w - 1, err);
      x = Seg(x, y, row.Snapshot.Name, w - 1, err);
      x = Seg(x, y, " unreachable: ", w - 1, err);
      Seg(x, y, row.Snapshot.Error, w - 1, text);
      return;
    }

    Put(c.Delete, y, "✕", 1, new Attribute(selected ? Theme.Black : Theme.Red, bg));
    Put(c.Context, y, row.Snapshot.Name, Columns.ContextWidth, new Attribute(selected ? Theme.Black : Theme.Cyan, bg));

    if (row.Kind == RowKind.Image)
    {
      Image img = row.Image;
      int nameEnd = c.Name + c.NameWidth;
      int x = Seg(c.Name, y, img.Repository, nameEnd, text);
      if (img.Repository != "<none>")
      {
        x = Seg(x, y, ":", nameEnd, new Attribute(selected ? Theme.Black : Theme.Dim, bg));
        Seg(x, y, img.Tag, nameEnd, text);
      }
      Put(c.Status, y, img.Id, Columns.IdWidth, text);
      PutRight(c.Cpu, y, img.Size, Columns.SizeWidth, text);
      Put(c.Mem + 1, y, img.CreatedSince, Columns.CreatedWidth, text);
      return;
    }

    // Lever: [┃──] off, [─┃─] busy, [──┃] on.
    Lever lever = busy ? Lever.Busy : row.Running == 0 ? Lever.Off : row.Running < row.Count ? Lever.Partial : Lever.On;
    DrawLever(c.Lever, y, lever, bg);

    // Name, with tree marker and status dot.
    int nameEnd2 = c.Name + c.NameWidth;
    int nx = c.Name;
    Color dotColor = row.Running == 0 ? Theme.Dim : row.Running < row.Count ? Theme.Yellow : Theme.Green;
    Attribute dot = new(selected ? Theme.Black : dotColor, bg);
    if (row.Kind == RowKind.Project)
    {
      nx = Seg(nx, y, _collapsed.Contains(row.Key) ? "▶ " : "▼ ", nameEnd2, text);
    }
    else if (row.Child)
    {
      nx = Seg(nx, y, "    ", nameEnd2, text);
    }
    else
    {
      nx = Seg(nx, y, "  ", nameEnd2, text);
    }
    nx = Seg(nx, y, row.Running > 0 ? "● " : "○ ", nameEnd2, dot);
    nx = Seg(nx, y, row.Name, nameEnd2, text);
    if (row.Kind == RowKind.Project)
    {
      n = Copy(" (", buf);
      n += FormatInt(row.Count, buf.Slice(n));
      buf[n++] = ')';
      Seg(nx, y, buf.Slice(0, n), nameEnd2, new Attribute(selected ? Theme.Black : Theme.Dim, bg));
    }

    // Status.
    Attribute statusAttr = new(selected ? Theme.Black : busy ? Theme.Yellow : dotColor == Theme.Dim ? Theme.Dim : dotColor, bg);
    if (busy)
    {
      Put(c.Status, y, "working…", Columns.StatusWidth, statusAttr);
    }
    else if (row.Kind == RowKind.Project)
    {
      if (row.Running == 0)
      {
        Put(c.Status, y, "stopped", Columns.StatusWidth, statusAttr);
      }
      else
      {
        n = FormatInt(row.Running, buf);
        buf[n++] = '/';
        n += FormatInt(row.Count, buf.Slice(n));
        n += Copy(" up", buf.Slice(n));
        Put(c.Status, y, buf.Slice(0, n), Columns.StatusWidth, statusAttr);
      }
    }
    else
    {
      Put(c.Status, y, row.Container.Status, Columns.StatusWidth, row.Running > 0 ? text : statusAttr);
    }

    // Usage.
    if (row.Running > 0)
    {
      n = FormatPercent(row.Cpu, buf);
      PutRight(c.Cpu, y, buf.Slice(0, n), Columns.CpuWidth, text);
      n = FormatBytes(row.Mem, buf);
      PutRight(c.Mem, y, buf.Slice(0, n), Columns.MemWidth, text);
    }
    else
    {
      PutRight(c.Cpu, y, "—", Columns.CpuWidth, text);
      PutRight(c.Mem, y, "—", Columns.MemWidth, text);
    }
    if (row.Gpu >= 0 && row.Running > 0)
    {
      n = FormatPercent(row.Gpu, buf);
      PutRight(c.Gpu, y, buf.Slice(0, n), Columns.GpuWidth, text);
    }
    else
    {
      PutRight(c.Gpu, y, "—", Columns.GpuWidth, text);
    }
  }

  void DrawLever(int x, int y, Lever lever, Color bg)
  {
    int knob;
    Color color;
    switch (lever)
    {
      case Lever.On:
        knob = 2;
        color = Theme.Green;
        break;
      case Lever.Partial:
        knob = 2;
        color = Theme.Yellow;
        break;
      case Lever.Busy:
        knob = 1;
        color = Theme.Yellow;
        break;
      default:
        knob = 0;
        color = Theme.Dim;
        break;
    }
    Attribute frame = new(Theme.Dim, bg);
    Attribute track = new(new Color(0x44, 0x44, 0x44), bg);
    Attribute on = new(color, bg);
    Put(x, y, "[", 1, frame);
    for (int i = 0; i < 3; i++)
    {
      Put(x + 1 + i, y, i == knob ? "┃" : "─", 1, i == knob ? on : track);
    }
    Put(x + 4, y, "]", 1, frame);
  }

  // ---- Bottom lines ---------------------------------------------------------------------------

  void DrawStatusLine(int y)
  {
    int w = Viewport.Width;
    Put(0, y, "", w, Theme.Normal);
    if (_confirming)
    {
      int x = Seg(1, y, _confirmText, w - 12, Theme.Warn);
      _confirmYesX = x + 1;
      Put(_confirmYesX, y, " Yes ", 5, new Attribute(Theme.Black, Theme.Green));
      _confirmNoX = _confirmYesX + 6;
      Put(_confirmNoX, y, " No ", 4, new Attribute(Theme.Black, Theme.Red));
      return;
    }
    if (_editingFilter)
    {
      int x = Seg(1, y, "Filter: ", w, Theme.Label);
      x = Seg(x, y, Filter, w, Theme.Warn);
      x = Seg(x, y, "▏", w, Theme.Warn);
      Seg(x + 2, y, "Enter: done   Esc: clear", w, Theme.Faint);
      return;
    }
    if (_message != null && Environment.TickCount64 < _messageUntil)
    {
      Put(1, y, _message, w - 2, _messageIsError ? Theme.Error : Theme.Ok);
      return;
    }
    ReadOnlySpan<char> hint = _mode == Mode.Images ? "✕ delete image · click header to sort · F2 back to containers" : "click lever: on/off · ✕ delete · ▼ expand · click header: sort · double-click: logs";
    Put(1, y, hint, w - 2, Theme.Faint);
  }

  void DrawFunctionBar(int y)
  {
    int w = Viewport.Width;
    Put(0, y, "", w, Theme.KeyText);
    _barCount = 0;
    int x = 0;
    x = BarItem(x, y, "F1", "Help", 1);
    x = BarItem(x, y, "F2", _mode == Mode.Images ? "Contnr" : "Images", 2);
    if (_mode != Mode.Images)
    {
      x = BarItem(x, y, "F3", "Logs", 3);
    }
    x = BarItem(x, y, "F4", "Filter", 4);
    if (_mode != Mode.Images)
    {
      x = BarItem(x, y, "F5", "Start", 5);
      x = BarItem(x, y, "F6", "Stop", 6);
      x = BarItem(x, y, "F7", "Restrt", 7);
    }
    x = BarItem(x, y, "F8", "Delete", 8);
    if (_mode != Mode.Images)
    {
      x = BarItem(x, y, "F9", _runningOnly ? "All" : "Runnng", 9);
    }
    BarItem(x, y, "F10", "Quit", 10);
  }

  int BarItem(int x, int y, ReadOnlySpan<char> key, ReadOnlySpan<char> label, int code)
  {
    int w = Viewport.Width;
    if (x >= w || _barCount >= MaxBar)
    {
      return x;
    }
    int start = x;
    x = Seg(x, y, key, w, Theme.KeyName);
    x = Seg(x, y, label, w, Theme.KeyText);
    x = Seg(x, y, " ", w, Theme.KeyText);
    _barStart[_barCount] = start;
    _barEnd[_barCount] = x;
    _barCode[_barCount] = code;
    _barCount++;
    return x;
  }

  // ---- Logs and help --------------------------------------------------------------------------

  void DrawLogs(int h)
  {
    int w = Viewport.Width;
    int bodyHeight = Math.Max(0, h - 2);
    Put(0, 0, "", w, Theme.Header);
    int x = Seg(1, 0, "Logs: ", w, Theme.Header);
    x = Seg(x, 0, _logTitle, w, Theme.Header);
    Span<char> buf = stackalloc char[48];
    int n = Copy("   lines ", buf);
    n += FormatInt(_logLineStarts.Count == 0 ? 0 : _logScroll + 1, buf.Slice(n));
    buf[n++] = '-';
    n += FormatInt(Math.Min(_logLineStarts.Count, _logScroll + bodyHeight), buf.Slice(n));
    n += Copy(" of ", buf.Slice(n));
    n += FormatInt(_logLineStarts.Count, buf.Slice(n));
    Seg(x, 0, buf.Slice(0, n), w, Theme.Header);

    ReadOnlySpan<char> text = _logText;
    Span<char> line = stackalloc char[Math.Clamp(w, 1, 1024)];
    for (int r = 0; r < bodyHeight; r++)
    {
      int i = _logScroll + r;
      if (i >= _logLineStarts.Count)
      {
        Put(0, 1 + r, "", w, Theme.Normal);
        continue;
      }
      int start = _logLineStarts[i];
      int end = i + 1 < _logLineStarts.Count ? _logLineStarts[i + 1] - 1 : text.Length;
      int len = CleanLogLine(text.Slice(start, end - start), line);
      ReadOnlySpan<char> l = line.Slice(0, len);
      Put(0, 1 + r, l, w, l.SequenceEqual("──── stderr ────") ? Theme.Warn : Theme.Normal);
    }

    Put(0, h - 1, "", w, Theme.KeyText);
    _barCount = 0;
    x = BarItem(0, h - 1, "Esc", "Close", CodeClose);
    x = BarItem(x, h - 1, "R", "Reload", CodeReload);
    Seg(x + 1, h - 1, "↑↓ PgUp PgDn Home End / wheel: scroll", w, Theme.KeyText);
  }

  /// <summary>Copies a log line into <paramref name="into"/>, dropping ANSI escape sequences and control characters.</summary>
  static int CleanLogLine(ReadOnlySpan<char> src, Span<char> into)
  {
    int n = 0;
    for (int i = 0; i < src.Length && n < into.Length; i++)
    {
      char ch = src[i];
      if (ch == '\x1b')
      {
        // CSI: ESC [ params final-byte(@..~)
        if (i + 1 < src.Length && src[i + 1] == '[')
        {
          i += 2;
          while (i < src.Length && (src[i] < '@' || src[i] > '~'))
          {
            i++;
          }
        }
        continue;
      }
      if (ch == '\t')
      {
        ch = ' ';
      }
      if (ch < ' ' || char.IsSurrogate(ch))
      {
        continue;
      }
      into[n++] = ch;
    }
    return n;
  }

  void DrawHelp(int h)
  {
    int w = Viewport.Width;
    Put(0, 0, "", w, Theme.Header);
    Put(1, 0, "Help", w - 1, Theme.Header);
    for (int y = 1; y < h - 1; y++)
    {
      int i = y - 2;
      if (i >= 0 && i < HelpLines.Length)
      {
        Put(0, y, "  ", 2, Theme.Normal);
        Put(2, y, HelpLines[i], w - 2, i == 0 || HelpLines[i] == "Mouse" || HelpLines[i] == "Keys" ? Theme.Label : Theme.Normal);
      }
      else
      {
        Put(0, y, "", w, Theme.Normal);
      }
    }
    Put(0, h - 1, "", w, Theme.KeyText);
    _barCount = 0;
    BarItem(0, h - 1, "Esc", "Close", CodeClose);
  }

  // ---- Primitives -----------------------------------------------------------------------------

  /// <summary>Writes <paramref name="text"/> left-aligned into exactly <paramref name="width"/> cells (padded, or cut with '…').</summary>
  void Put(int x, int y, ReadOnlySpan<char> text, int width, Attribute attr)
  {
    if (width <= 0)
    {
      return;
    }
    SetAttribute(attr);
    Move(x, y);
    bool cut = text.Length > width;
    for (int i = 0; i < width; i++)
    {
      char ch = ' ';
      if (i < text.Length)
      {
        ch = cut && i == width - 1 ? '…' : text[i];
      }
      AddRune(char.IsSurrogate(ch) ? '?' : ch);
    }
  }

  void PutRight(int x, int y, ReadOnlySpan<char> text, int width, Attribute attr)
  {
    if (text.Length >= width)
    {
      Put(x, y, text, width, attr);
      return;
    }
    Put(x, y, "", width - text.Length, attr);
    Put(x + width - text.Length, y, text, text.Length, attr);
  }

  /// <summary>Writes text at x without padding, stopping at <paramref name="maxX"/>; returns the x after it.</summary>
  int Seg(int x, int y, ReadOnlySpan<char> text, int maxX, Attribute attr)
  {
    int room = maxX - x;
    if (room <= 0)
    {
      return x;
    }
    if (text.IsEmpty)
    {
      // Used to clear the rest of a line.
      Put(x, y, "", room, attr);
      return x;
    }
    int len = Math.Min(text.Length, room);
    Put(x, y, text, len, attr);
    return x + len;
  }

  static int Copy(ReadOnlySpan<char> s, Span<char> into)
  {
    s.CopyTo(into);
    return s.Length;
  }

  static int FormatInt(int v, Span<char> into)
  {
    v.TryFormat(into, out int n, default, CultureInfo.InvariantCulture);
    return n;
  }

  static int FormatPercent(double v, Span<char> into)
  {
    v.TryFormat(into, out int n, "0.0", CultureInfo.InvariantCulture);
    return n;
  }

  static int FormatBytes(long bytes, Span<char> into)
  {
    int n;
    if (bytes >= 1L << 30)
    {
      ((double)bytes / (1L << 30)).TryFormat(into, out n, "0.0", CultureInfo.InvariantCulture);
      into[n++] = 'G';
    }
    else if (bytes >= 1L << 20)
    {
      (bytes >> 20).TryFormat(into, out n, default, CultureInfo.InvariantCulture);
      into[n++] = 'M';
    }
    else if (bytes >= 1L << 10)
    {
      (bytes >> 10).TryFormat(into, out n, default, CultureInfo.InvariantCulture);
      into[n++] = 'K';
    }
    else
    {
      bytes.TryFormat(into, out n, default, CultureInfo.InvariantCulture);
      into[n++] = 'B';
    }
    return n;
  }
}
