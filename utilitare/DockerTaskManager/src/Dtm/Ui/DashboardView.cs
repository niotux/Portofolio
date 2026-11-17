using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dtm.Docker;
using Terminal.Gui.Input;
using Terminal.Gui.Views;

namespace Dtm.Ui;

public enum Mode
{
  Containers,
  Images,
  Logs,
  Help
}

/// <summary>An action that's running (or just finished) against a row; its lever shows "busy" meanwhile.</summary>
public struct Pending
{
  public RowKey Key;
  public ContextPoller Poller;
  public bool Done;
  /// <summary>The snapshot current when the action finished; pending clears once a newer one is published.</summary>
  public ContextSnapshot? SnapshotAtDone;
}

/// <summary>The whole app: one self-drawn, htop-style screen. Input handling lives here, drawing in DashboardView.Draw.cs.</summary>
public sealed partial class DashboardView : Runnable
{
  const int FirstRowY = 4;
  const int FilterCapacity = 64;
  static readonly TimeSpan ActionTimeout = TimeSpan.FromMinutes(3);
  static readonly TimeSpan LogsTimeout = TimeSpan.FromSeconds(20);
  static readonly TimeSpan MessageDuration = TimeSpan.FromSeconds(6);

  readonly Fleet _fleet;
  readonly List<Row> _rows = new();
  readonly List<Row> _scratch = new();
  readonly List<RowKey> _collapsed = new();
  readonly List<Pending> _pending = new();
  readonly RowComparer _comparer = new();
  readonly char[] _filter = new char[FilterCapacity];
  int _filterLength;
  bool _editingFilter;
  bool _runningOnly;

  Mode _mode = Mode.Containers;
  Mode _returnMode = Mode.Containers;
  RowKey _selectedKey;
  int _selected;
  int _scroll;

  bool _confirming;
  string _confirmText = "";
  RowKey _confirmKey;
  ContextPoller? _confirmPoller;
  string[] _confirmArgs = [];

  string? _message;
  bool _messageIsError;
  long _messageUntil;

  string _logTitle = "";
  string _logText = "";
  readonly List<int> _logLineStarts = new();
  int _logScroll;

  public DashboardView(Fleet fleet)
  {
    _fleet = fleet;
    CanFocus = true;
  }

  ReadOnlySpan<char> Filter
  {
    get { return _filter.AsSpan(0, _filterLength); }
  }

  int ListHeight
  {
    get { return Math.Max(0, Viewport.Height - FirstRowY - 2); }
  }

  /// <summary>Periodic UI-thread refresh: rebuild rows from the latest snapshots and redraw.</summary>
  public bool Tick()
  {
    for (int i = _pending.Count - 1; i >= 0; i--)
    {
      Pending p = _pending[i];
      if (p.Done && p.Poller.Snapshot != p.SnapshotAtDone)
      {
        _pending.RemoveAt(i);
      }
    }
    Rebuild();
    SetNeedsDraw();
    return true;
  }

  void Rebuild()
  {
    ContextPoller[] pollers = _fleet.Pollers;
    if (_mode == Mode.Images || (_mode != Mode.Containers && _returnMode == Mode.Images))
    {
      RowBuilder.Images(pollers, Filter, _comparer, _rows);
    }
    else
    {
      RowBuilder.Containers(pollers, Filter, _runningOnly, _collapsed, _comparer, _scratch, _rows);
    }

    int found = -1;
    for (int i = 0; i < _rows.Count; i++)
    {
      if (_rows[i].Key == _selectedKey)
      {
        found = i;
        break;
      }
    }
    _selected = found >= 0 ? found : Math.Min(_selected, _rows.Count - 1);
    if (_selected >= 0 && _selected < _rows.Count)
    {
      _selectedKey = _rows[_selected].Key;
    }
    ClampScroll();
  }

  void Select(int index)
  {
    if (_rows.Count == 0)
    {
      return;
    }
    _selected = Math.Clamp(index, 0, _rows.Count - 1);
    _selectedKey = _rows[_selected].Key;
    ClampScroll();
    SetNeedsDraw();
  }

  void ClampScroll()
  {
    int h = ListHeight;
    if (_selected < _scroll)
    {
      _scroll = _selected;
    }
    if (h > 0 && _selected >= _scroll + h)
    {
      _scroll = _selected - h + 1;
    }
    _scroll = Math.Clamp(_scroll, 0, Math.Max(0, _rows.Count - h));
  }

  bool HasSelection
  {
    get { return _selected >= 0 && _selected < _rows.Count; }
  }

  // ---- Keyboard -------------------------------------------------------------------------------

  /// <summary>Hooked to the application's KeyDown so it runs before default bindings (Esc would quit otherwise).</summary>
  public void OnAppKeyDown(object? sender, Key key)
  {
    if (HandleKey(key))
    {
      key.Handled = true;
    }
  }

  bool HandleKey(Key key)
  {
    int rune = key.AsRune.Value;
    if (_confirming)
    {
      if (key == Key.Enter || rune == 'y' || rune == 'Y')
      {
        ConfirmYes();
      }
      else if (key == Key.Esc || rune == 'n' || rune == 'N')
      {
        _confirming = false;
      }
      SetNeedsDraw();
      return true;
    }

    if (_editingFilter)
    {
      if (key == Key.Esc)
      {
        _filterLength = 0;
        _editingFilter = false;
      }
      else if (key == Key.Enter || key == Key.F4)
      {
        _editingFilter = false;
      }
      else if (key == Key.Backspace)
      {
        if (_filterLength > 0)
        {
          _filterLength--;
        }
      }
      else if (rune >= 32 && rune < 0xFFFF && !key.IsCtrl && !key.IsAlt && _filterLength < FilterCapacity)
      {
        _filter[_filterLength++] = (char)rune;
      }
      else
      {
        return false;
      }
      Rebuild();
      SetNeedsDraw();
      return true;
    }

    if (_mode == Mode.Logs)
    {
      return HandleLogsKey(key, rune);
    }
    if (_mode == Mode.Help)
    {
      if (key == Key.Esc || key == Key.F1 || rune == 'q' || key == Key.Enter)
      {
        _mode = _returnMode;
        SetNeedsDraw();
      }
      return true;
    }

    if (TryFunctionKey(key))
    {
      return true;
    }
    if (key == Key.CursorUp)
    {
      Select(_selected - 1);
    }
    else if (key == Key.CursorDown)
    {
      Select(_selected + 1);
    }
    else if (key == Key.PageUp)
    {
      Select(_selected - Math.Max(1, ListHeight - 1));
    }
    else if (key == Key.PageDown)
    {
      Select(_selected + Math.Max(1, ListHeight - 1));
    }
    else if (key == Key.Home)
    {
      Select(0);
    }
    else if (key == Key.End)
    {
      Select(_rows.Count - 1);
    }
    else if (key == Key.Space)
    {
      ToggleSelected();
    }
    else if (key == Key.Enter)
    {
      ActivateSelected();
    }
    else if (key == Key.CursorLeft || key == Key.CursorRight)
    {
      if (HasSelection && _rows[_selected].Kind == RowKind.Project)
      {
        SetCollapsed(_rows[_selected].Key, key == Key.CursorLeft);
      }
      else if (HasSelection && key == Key.CursorLeft && _rows[_selected].Child)
      {
        SelectParent();
      }
    }
    else if (key == Key.Delete)
    {
      Run(9);
    }
    else if (key == Key.Esc)
    {
      _filterLength = 0;
      Rebuild();
      SetNeedsDraw();
    }
    else if (rune == '/')
    {
      Run(4);
    }
    else if (rune == 'q')
    {
      Run(10);
    }
    else
    {
      return false;
    }
    return true;
  }

  bool TryFunctionKey(Key key)
  {
    for (int i = 0; i < FunctionKeys.Length; i++)
    {
      if (key == FunctionKeys[i])
      {
        Run(i + 1);
        return true;
      }
    }
    return false;
  }

  /// <summary>Runs the F-key command <paramref name="f"/> (1..10); keys and the clickable bar both end up here.</summary>
  void Run(int f)
  {
    switch (f)
    {
      case 1:
        if (_mode != Mode.Help)
        {
          _returnMode = _mode;
          _mode = Mode.Help;
        }
        break;
      case 2:
        _mode = _mode == Mode.Images ? Mode.Containers : Mode.Images;
        _returnMode = _mode;
        _selected = 0;
        _scroll = 0;
        if (_mode == Mode.Images)
        {
          KickAll(true);
        }
        break;
      case 3:
        OpenLogs();
        break;
      case 4:
        _editingFilter = true;
        break;
      case 5:
        PowerSelected(true, false);
        break;
      case 6:
        PowerSelected(false, false);
        break;
      case 7:
        PowerSelected(true, true);
        break;
      case 8:
        AskDeleteSelected();
        break;
      case 9:
        _runningOnly = !_runningOnly;
        break;
      case 10:
        App?.RequestStop();
        break;
    }
    Rebuild();
    SetNeedsDraw();
  }

  bool HandleLogsKey(Key key, int rune)
  {
    int h = Math.Max(1, Viewport.Height - 2);
    if (key == Key.Esc || key == Key.F3 || rune == 'q')
    {
      _mode = _returnMode;
    }
    else if (rune == 'r' || rune == 'R')
    {
      ReloadLogs();
    }
    else if (key == Key.CursorUp)
    {
      ScrollLogs(-1);
    }
    else if (key == Key.CursorDown)
    {
      ScrollLogs(1);
    }
    else if (key == Key.PageUp)
    {
      ScrollLogs(-h);
    }
    else if (key == Key.PageDown || key == Key.Space)
    {
      ScrollLogs(h);
    }
    else if (key == Key.Home)
    {
      _logScroll = 0;
    }
    else if (key == Key.End)
    {
      ScrollLogs(int.MaxValue / 2);
    }
    SetNeedsDraw();
    return true;
  }

  void ScrollLogs(int delta)
  {
    int h = Math.Max(1, Viewport.Height - 2);
    _logScroll = Math.Clamp(_logScroll + delta, 0, Math.Max(0, _logLineStarts.Count - h));
    SetNeedsDraw();
  }

  // ---- Mouse ----------------------------------------------------------------------------------

  protected override bool OnMouseEvent(Mouse mouse)
  {
    if (mouse.Position is not System.Drawing.Point pos)
    {
      return false;
    }
    MouseFlags flags = mouse.Flags;
    if ((flags & (MouseFlags.WheeledUp | MouseFlags.WheeledDown)) != 0)
    {
      int delta = (flags & MouseFlags.WheeledUp) != 0 ? -3 : 3;
      if (_mode == Mode.Logs)
      {
        ScrollLogs(delta);
      }
      else
      {
        _scroll = Math.Clamp(_scroll + delta, 0, Math.Max(0, _rows.Count - ListHeight));
        SetNeedsDraw();
      }
      return true;
    }

    bool clicked = (flags & MouseFlags.LeftButtonClicked) != 0;
    bool doubleClicked = (flags & MouseFlags.LeftButtonDoubleClicked) != 0;
    if (!clicked && !doubleClicked)
    {
      return false;
    }

    int h = Viewport.Height;
    if (pos.Y == h - 1)
    {
      int f = FunctionKeyAt(pos.X);
      if (f > 0)
      {
        if (_mode == Mode.Logs || _mode == Mode.Help)
        {
          HandleKey(Key.Esc);
        }
        else
        {
          Run(f);
        }
      }
      return true;
    }
    if (_mode == Mode.Logs || _mode == Mode.Help)
    {
      return true;
    }
    if (pos.Y == h - 2 && _confirming)
    {
      if (pos.X >= _confirmYesX && pos.X < _confirmYesX + 5)
      {
        ConfirmYes();
      }
      else if (pos.X >= _confirmNoX && pos.X < _confirmNoX + 4)
      {
        _confirming = false;
      }
      SetNeedsDraw();
      return true;
    }
    if (pos.Y == 2 && pos.X >= _showToggleX && pos.X < _showToggleX + 9)
    {
      Run(9);
      return true;
    }
    if (pos.Y == 2 && pos.X >= _filterX && pos.X < _showToggleX - 2)
    {
      Run(4);
      return true;
    }
    if (pos.Y == FirstRowY - 1)
    {
      SortBy(ColumnAt(pos.X));
      return true;
    }

    int index = _scroll + pos.Y - FirstRowY;
    if (pos.Y < FirstRowY || index < 0 || index >= _rows.Count || pos.Y >= h - 2)
    {
      return true;
    }
    Select(index);
    Row row = _rows[index];
    Columns col = ColumnLayout();
    if (pos.X >= col.Lever && pos.X < col.Lever + 5 && row.Kind != RowKind.Image)
    {
      ToggleSelected();
    }
    else if (pos.X >= col.Delete - 1 && pos.X <= col.Delete + 1)
    {
      AskDeleteSelected();
    }
    else if (row.Kind == RowKind.Project && pos.X >= col.Name && pos.X < col.Name + 2)
    {
      SetCollapsed(row.Key, !_collapsed.Contains(row.Key));
    }
    else if (doubleClicked)
    {
      ActivateSelected();
    }
    return true;
  }

  void SortBy(SortColumn column)
  {
    if (_comparer.Column == column)
    {
      _comparer.Descending = !_comparer.Descending;
    }
    else
    {
      _comparer.Column = column;
      _comparer.Descending = false;
    }
    Rebuild();
    SetNeedsDraw();
  }

  // ---- Actions --------------------------------------------------------------------------------

  void SetCollapsed(RowKey key, bool collapse)
  {
    int at = _collapsed.IndexOf(key);
    if (collapse && at < 0)
    {
      _collapsed.Add(key);
    }
    else if (!collapse && at >= 0)
    {
      _collapsed.RemoveAt(at);
    }
    Rebuild();
    SetNeedsDraw();
  }

  void SelectParent()
  {
    for (int i = _selected - 1; i >= 0; i--)
    {
      if (_rows[i].Kind == RowKind.Project)
      {
        Select(i);
        return;
      }
    }
  }

  void ActivateSelected()
  {
    if (!HasSelection)
    {
      return;
    }
    Row row = _rows[_selected];
    if (row.Kind == RowKind.Project)
    {
      SetCollapsed(row.Key, !_collapsed.Contains(row.Key));
    }
    else if (row.Kind == RowKind.Container)
    {
      OpenLogs();
    }
  }

  void ToggleSelected()
  {
    if (!HasSelection)
    {
      return;
    }
    Row row = _rows[_selected];
    if (row.Kind == RowKind.Project || row.Kind == RowKind.Container)
    {
      PowerSelected(row.Running == 0, false);
    }
  }

  /// <summary>Start (on), stop (off) or restart the selected container or whole compose project.</summary>
  void PowerSelected(bool on, bool restart)
  {
    if (!HasSelection)
    {
      return;
    }
    Row row = _rows[_selected];
    if (row.Kind != RowKind.Project && row.Kind != RowKind.Container)
    {
      return;
    }
    if (IsBusy(row))
    {
      Flash("still working on " + row.Name + "…", false);
      return;
    }
    string verb = restart ? "restart" : on ? "start" : "stop";
    string[] args = row.Kind == RowKind.Project ? ["compose", "-p", row.Name, verb] : [verb, row.Container.Id];
    StartAction(row.Key, row.Poller, args, verb + " " + row.Name, false);
  }

  void AskDeleteSelected()
  {
    if (!HasSelection)
    {
      return;
    }
    Row row = _rows[_selected];
    switch (row.Kind)
    {
      case RowKind.Project:
        _confirmText = "Delete compose project " + row.Name + " on " + row.Snapshot.Name + " (compose down)?";
        _confirmArgs = ["compose", "-p", row.Name, "down"];
        break;
      case RowKind.Container:
        _confirmText = "Delete container " + row.Name + " on " + row.Snapshot.Name + "?";
        _confirmArgs = ["rm", "-f", row.Container.Id];
        break;
      case RowKind.Image:
        Image img = row.Image;
        _confirmText = "Delete image " + img.Repository + ":" + img.Tag + " on " + row.Snapshot.Name + "?";
        _confirmArgs = ["image", "rm", img.Repository == "<none>" ? img.Id : img.Repository + ":" + img.Tag];
        break;
      default:
        return;
    }
    _confirmKey = row.Key;
    _confirmPoller = row.Poller;
    _confirming = true;
    SetNeedsDraw();
  }

  void ConfirmYes()
  {
    _confirming = false;
    if (_confirmPoller != null)
    {
      StartAction(_confirmKey, _confirmPoller, _confirmArgs, "delete", _confirmKey.Kind == RowKind.Image);
    }
  }

  void StartAction(RowKey key, ContextPoller poller, string[] args, string label, bool images)
  {
    Pending p = new();
    p.Key = key;
    p.Poller = poller;
    _pending.Add(p);
    SetNeedsDraw();
    _ = RunActionAsync(key, poller, args, label, images);
  }

  async Task RunActionAsync(RowKey key, ContextPoller poller, string[] args, string label, bool images)
  {
    CliResult r = await poller.RunAsync(args, ActionTimeout);
    App?.Invoke(() =>
    {
      for (int i = 0; i < _pending.Count; i++)
      {
        if (_pending[i].Key == key)
        {
          Pending p = _pending[i];
          p.Done = true;
          p.SnapshotAtDone = poller.Snapshot;
          _pending[i] = p;
        }
      }
      if (r.Ok)
      {
        Flash(label + ": done", false);
      }
      else
      {
        Flash(label + " failed: " + r.ErrorLine(), true);
      }
      poller.Kick(images);
    });
  }

  bool IsBusy(Row row)
  {
    for (int i = 0; i < _pending.Count; i++)
    {
      RowKey k = _pending[i].Key;
      if (k.Context != row.Snapshot.Name)
      {
        continue;
      }
      if (k == row.Key)
      {
        return true;
      }
      // A container inside a project that's being switched is busy too.
      if (row.Kind == RowKind.Container && k.Kind == RowKind.Project && row.Container.Project == k.Id)
      {
        return true;
      }
    }
    return false;
  }

  void KickAll(bool images)
  {
    ContextPoller[] pollers = _fleet.Pollers;
    for (int i = 0; i < pollers.Length; i++)
    {
      pollers[i].Kick(images);
    }
  }

  void Flash(string message, bool error)
  {
    _message = message;
    _messageIsError = error;
    _messageUntil = Environment.TickCount64 + (long)MessageDuration.TotalMilliseconds * (error ? 2 : 1);
    SetNeedsDraw();
  }

  // ---- Logs -----------------------------------------------------------------------------------

  ContextPoller? _logPoller;
  string[] _logArgs = [];

  void OpenLogs()
  {
    if (!HasSelection)
    {
      return;
    }
    Row row = _rows[_selected];
    if (row.Kind == RowKind.Project)
    {
      _logArgs = ["compose", "-p", row.Name, "logs", "--tail", "200", "--no-color"];
    }
    else if (row.Kind == RowKind.Container)
    {
      _logArgs = ["logs", "--tail", "300", row.Container.Id];
    }
    else
    {
      return;
    }
    _logPoller = row.Poller;
    _logTitle = row.Name + " @ " + row.Snapshot.Name;
    _returnMode = _mode;
    _mode = Mode.Logs;
    ReloadLogs();
  }

  void ReloadLogs()
  {
    if (_logPoller == null)
    {
      return;
    }
    SetLogText("loading…");
    ContextPoller poller = _logPoller;
    string[] args = _logArgs;
    _ = LoadLogsAsync(poller, args);
  }

  async Task LoadLogsAsync(ContextPoller poller, string[] args)
  {
    CliResult r = await poller.RunAsync(args, LogsTimeout);
    // docker logs replays the container's stderr on its own stderr; ordering between the two is lost.
    string text;
    if (!r.Ok && r.Stdout.Length == 0)
    {
      text = "failed: " + r.ErrorLine();
    }
    else if (r.Stderr.Length == 0)
    {
      text = r.Stdout;
    }
    else if (r.Stdout.Length == 0)
    {
      text = r.Stderr;
    }
    else
    {
      text = r.Stdout + "\n──── stderr ────\n" + r.Stderr;
    }
    App?.Invoke(() =>
    {
      SetLogText(text);
      ScrollLogs(int.MaxValue / 2);
    });
  }

  void SetLogText(string text)
  {
    _logText = text;
    _logLineStarts.Clear();
    _logScroll = 0;
    ReadOnlySpan<char> s = text;
    int start = 0;
    while (start < s.Length)
    {
      _logLineStarts.Add(start);
      int nl = s.Slice(start).IndexOf('\n');
      if (nl < 0)
      {
        break;
      }
      start += nl + 1;
    }
    SetNeedsDraw();
  }
}
