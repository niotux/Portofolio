using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Texp.Commands;
using Texp.Fs;

namespace Texp.Core;

/// <summary>
/// Everything texp knows: tabs, the command line, clipboard, running jobs, and what's shown over the list.
/// Commands work through this; the view only draws it and feeds it keys.
/// </summary>
public sealed class Explorer
{
  static readonly TimeSpan MessageDuration = TimeSpan.FromSeconds(5);

  public readonly Command[] Commands;
  public readonly List<Tab> Tabs = new();
  public readonly LineEditor Line = new();
  public readonly List<Place> Places = new();
  public readonly List<string> Clipboard = new();
  public readonly List<Job> Jobs = new();
  public int Active;
  public bool ShowHidden;
  public bool ClipboardIsCut;
  public Overlay? Overlay;
  public bool QuitRequested;

  public string? Question;
  Action? _onYes;

  public string? Message;
  public bool MessageIsError;
  long _messageUntil;

  /// <summary>Runs an action on the UI thread; background jobs report back through it.</summary>
  public Action<Action> Post = static a => a();

  public Explorer(Command[] commands, string folder)
  {
    Commands = commands;
    Fs.Places.Load(Places);
    OpenTab(folder);
  }

  public Tab Tab
  {
    get { return Tabs[Active]; }
  }

  public bool HasMessage
  {
    get { return Message != null && Environment.TickCount64 < _messageUntil; }
  }

  // ---- The command line -----------------------------------------------------------------------

  /// <summary>Finds a command by name or alias, ignoring case.</summary>
  public Command? Find(ReadOnlySpan<char> name)
  {
    if (name.IsEmpty)
    {
      return null;
    }
    for (int i = 0; i < Commands.Length; i++)
    {
      Command c = Commands[i];
      if (name.Equals(c.Name, StringComparison.OrdinalIgnoreCase))
      {
        return c;
      }
      string[] aliases = c.Aliases;
      for (int a = 0; a < aliases.Length; a++)
      {
        if (name.Equals(aliases[a], StringComparison.OrdinalIgnoreCase))
        {
          return c;
        }
      }
    }
    return null;
  }

  /// <summary>Splits a line into its first word and the rest (trimmed).</summary>
  public static void Split(ReadOnlySpan<char> line, out ReadOnlySpan<char> word, out ReadOnlySpan<char> args)
  {
    line = line.TrimStart(' ');
    int space = line.IndexOf(' ');
    word = space < 0 ? line : line.Slice(0, space);
    args = space < 0 ? ReadOnlySpan<char>.Empty : line.Slice(space + 1).Trim(' ');
  }

  /// <summary>The command the line starts with, or null when the line is a filter or a path.</summary>
  public Command? LineCommand(out ReadOnlySpan<char> args)
  {
    Split(Line.Text, out ReadOnlySpan<char> word, out args);
    return Find(word);
  }

  /// <summary>True when the line is a path to jump to ("/etc", "~/Downloads").</summary>
  public bool LineIsPath
  {
    get
    {
      ReadOnlySpan<char> t = Line.Text;
      return !t.IsEmpty && (t[0] == '/' || t[0] == '~');
    }
  }

  /// <summary>The text that filters the list: the line, unless it is a command or a path.</summary>
  public ReadOnlySpan<char> Filter
  {
    get
    {
      if (LineIsPath || LineCommand(out _) != null)
      {
        return ReadOnlySpan<char>.Empty;
      }
      return Line.Text;
    }
  }

  /// <summary>The command whose name starts with what's typed, to suggest (and complete with Tab).</summary>
  public Command? Suggestion()
  {
    ReadOnlySpan<char> t = Line.Text;
    if (t.IsEmpty || t.Contains(' ') || LineIsPath)
    {
      return null;
    }
    for (int i = 0; i < Commands.Length; i++)
    {
      if (Commands[i].Name.AsSpan().StartsWith(t, StringComparison.OrdinalIgnoreCase))
      {
        return Commands[i];
      }
    }
    return null;
  }

  /// <summary>Call after every change to the line: re-filters the list.</summary>
  public void LineChanged()
  {
    ReadOnlySpan<char> filter = Filter;
    Tab.Rebuild(filter, ShowHidden);
    if (!filter.IsEmpty)
    {
      Tab.JumpToMatch(filter);
    }
  }

  /// <summary>Puts text in the command line for the user to finish.</summary>
  public void Prefill(ReadOnlySpan<char> text)
  {
    Line.Set(text);
    LineChanged();
  }

  public void ClearLine()
  {
    if (Line.IsEmpty)
    {
      return;
    }
    Line.Clear();
    LineChanged();
  }

  /// <summary>Enter: run the typed command, jump to a typed path, or open the highlighted entry of a filter.</summary>
  public void Submit()
  {
    Command? cmd = LineCommand(out ReadOnlySpan<char> args);
    if (cmd != null)
    {
      string a = args.ToString();
      ClearLine();
      Run(cmd, a);
      return;
    }
    if (LineIsPath)
    {
      string path = ResolvePath(Line.Text);
      ClearLine();
      Go(path);
      return;
    }
    if (!Line.IsEmpty && Tab.ItemCount == 0)
    {
      Fail("nothing matches \"" + Line.Text.ToString() + "\"; type help for commands");
      return;
    }
    Command? open = Find("open");
    ClearLine();
    if (open != null)
    {
      Run(open, "");
    }
  }

  /// <summary>Runs a command; anything it throws becomes an error message.</summary>
  public void Run(Command cmd, ReadOnlySpan<char> args)
  {
    try
    {
      cmd.Run(this, args);
    }
    catch (Exception e)
    {
      Fail(cmd.Name + ": " + e.Message);
    }
    Tab.Rebuild(Filter, ShowHidden);
  }

  /// <summary>Tab completion: the command name first, then names in the current folder.</summary>
  public void Complete()
  {
    Command? suggestion = Suggestion();
    if (suggestion != null)
    {
      Line.Set(suggestion.Name);
      Line.Insert(' ');
      LineChanged();
      return;
    }
    ReadOnlySpan<char> text = Line.Text;
    int space = text.LastIndexOf(' ');
    if (space < 0)
    {
      return;
    }
    ReadOnlySpan<char> partial = text.Slice(space + 1);
    List<Entry> all = Tab.All;
    for (int i = 0; i < all.Count; i++)
    {
      string name = all[i].Name;
      if (name.Length > partial.Length && name.AsSpan().StartsWith(partial, StringComparison.OrdinalIgnoreCase))
      {
        Line.MoveTo(text.Length);
        for (int b = 0; b < partial.Length; b++)
        {
          Line.Backspace();
        }
        Line.Insert(name);
        LineChanged();
        return;
      }
    }
  }

  // ---- What commands act on -------------------------------------------------------------------

  /// <summary>Full paths of the marked entries, or of the entry under the cursor when nothing is marked.</summary>
  public List<string> Selection()
  {
    List<string> paths = new();
    Tab t = Tab;
    if (t.Marked.Count > 0)
    {
      for (int i = 0; i < t.Marked.Count; i++)
      {
        paths.Add(t.PathOf(t.Marked[i]));
      }
    }
    else if (t.HasItem)
    {
      paths.Add(t.PathOf(t.Current.Name));
    }
    return paths;
  }

  /// <summary>"notes.md" or "3 items", for messages and hints.</summary>
  public string SelectionLabel()
  {
    Tab t = Tab;
    if (t.Marked.Count > 1)
    {
      return t.Marked.Count + " items";
    }
    if (t.Marked.Count == 1)
    {
      return t.Marked[0];
    }
    return t.HasItem ? t.Current.Name : "nothing";
  }

  /// <summary>Turns typed text into a full path: "~" is home, relative paths are from the current folder.</summary>
  public string ResolvePath(ReadOnlySpan<char> text)
  {
    text = text.Trim();
    if (text.IsEmpty)
    {
      return Tab.Folder;
    }
    if (text[0] == '~')
    {
      return Path.GetFullPath(Path.Join(Fs.Places.Home(), text.Slice(1).TrimStart('/')));
    }
    return Path.GetFullPath(Path.Join(text[0] == '/' ? "/" : Tab.Folder, text));
  }

  /// <summary>A path for display, with the home folder shown as "~".</summary>
  public static string Tilde(string path)
  {
    string home = Fs.Places.Home();
    if (path == home)
    {
      return "~";
    }
    if (FileOps.IsInside(path, home) && home != "/")
    {
      return string.Concat("~", path.AsSpan(home.Length));
    }
    return path;
  }

  // ---- Navigation and tabs --------------------------------------------------------------------

  public bool Go(string folder)
  {
    if (!Tab.Navigate(folder, true))
    {
      Fail(Tab.Error ?? "can't open " + folder);
      return false;
    }
    Tab.Rebuild(Filter, ShowHidden);
    return true;
  }

  public void OpenTab(string folder)
  {
    Tab t = new(folder);
    if (!t.Navigate(folder, false))
    {
      string error = t.Error ?? "can't open " + folder;
      t = new Tab(Fs.Places.Home());
      t.Navigate(t.Folder, false);
      Fail(error);
    }
    Tabs.Add(t);
    Active = Tabs.Count - 1;
    ClearLine();
    Tab.Rebuild(Filter, ShowHidden);
  }

  public void CloseTab()
  {
    if (Tabs.Count == 1)
    {
      QuitRequested = true;
      return;
    }
    Tabs.RemoveAt(Active);
    Active = Math.Min(Active, Tabs.Count - 1);
    ClearLine();
    Tab.Rebuild(Filter, ShowHidden);
  }

  public void SwitchTab(int index)
  {
    if (Tabs.Count == 0)
    {
      return;
    }
    Active = ((index % Tabs.Count) + Tabs.Count) % Tabs.Count;
    ClearLine();
    if (Tab.IsStale())
    {
      Tab.Load();
    }
    Tab.Rebuild(Filter, ShowHidden);
  }

  /// <summary>Re-reads every open folder from disk and the sidebar.</summary>
  public void Reload()
  {
    for (int i = 0; i < Tabs.Count; i++)
    {
      Tabs[i].Load();
    }
    Fs.Places.Load(Places);
    Tab.Rebuild(Filter, ShowHidden);
  }

  /// <summary>Called about once a second: picks up changes made on disk by others.</summary>
  public void Tick()
  {
    if (Tab.IsStale())
    {
      Tab.Load();
      Tab.Rebuild(Filter, ShowHidden);
    }
  }

  // ---- Feedback -------------------------------------------------------------------------------

  public void Flash(string message)
  {
    Show(message, false);
  }

  public void Fail(string message)
  {
    Show(message, true);
  }

  void Show(string message, bool error)
  {
    Message = message;
    MessageIsError = error;
    _messageUntil = Environment.TickCount64 + (long)MessageDuration.TotalMilliseconds * (error ? 2 : 1);
  }

  /// <summary>Asks a yes/no question in the status line; <paramref name="yes"/> runs on yes.</summary>
  public void Ask(string question, Action yes)
  {
    Question = question;
    _onYes = yes;
  }

  public void Answer(bool yes)
  {
    Action? onYes = _onYes;
    Question = null;
    _onYes = null;
    if (yes && onYes != null)
    {
      try
      {
        onYes();
      }
      catch (Exception e)
      {
        Fail(e.Message);
      }
      Tab.Rebuild(Filter, ShowHidden);
    }
  }

  public void ShowOverlay(Overlay overlay)
  {
    Overlay = overlay;
  }

  public void PickOverlay(int index)
  {
    Overlay? o = Overlay;
    Overlay = null;
    if (o?.OnPick == null || index < 0 || index >= o.Lines.Count)
    {
      return;
    }
    try
    {
      o.OnPick(index);
    }
    catch (Exception e)
    {
      Fail(e.Message);
    }
    Tab.Rebuild(Filter, ShowHidden);
  }

  // ---- Background work ------------------------------------------------------------------------

  /// <summary>
  /// Runs <paramref name="work"/> off the UI thread with a progress line. What it returns runs back on the UI thread
  /// when it's done (e.g. to select what was created); then every tab reloads.
  /// </summary>
  public void Start(Job job, Func<Job, Action?> work)
  {
    Jobs.Add(job);
    _ = Task.Run(() =>
    {
      Action? after = null;
      string? error = null;
      try
      {
        after = work(job);
      }
      catch (OperationCanceledException)
      {
        error = "cancelled";
      }
      catch (Exception e)
      {
        error = e.Message;
      }
      Post(() =>
      {
        Jobs.Remove(job);
        Reload();
        if (error != null)
        {
          Fail(job.Label + ": " + error);
        }
        else
        {
          Flash(job.Label + ": done");
          after?.Invoke();
        }
        Tab.Rebuild(Filter, ShowHidden);
      });
    });
  }
}
