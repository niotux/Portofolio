using System;
using System.Collections.Generic;
using System.Reflection;
using Texp.Core;
using Terminal.Gui.Input;

namespace Texp.Commands;

/// <summary>
/// One thing texp can do. To add a command, drop a class deriving from this into the Commands folder and reinstall:
/// it's found automatically and shows up in the command line, its shortcuts, the bottom bar and `help`.
/// </summary>
public abstract class Command
{
  /// <summary>What you type in the command line.</summary>
  public abstract string Name { get; }

  /// <summary>One line for `help` and the command-line hint.</summary>
  public abstract string Help { get; }

  /// <summary>Other names that also work when typed.</summary>
  public virtual string[] Aliases
  {
    get { return []; }
  }

  /// <summary>
  /// Keys that run the command. Keys without Ctrl/Alt (Enter, Space, Delete, ...) only fire while the command line
  /// is empty, so they never get in the way of typing.
  /// </summary>
  public virtual Key[] Keys
  {
    get { return []; }
  }

  /// <summary>How to call it, for `help` (e.g. "mkdir &lt;name&gt;").</summary>
  public virtual string Usage
  {
    get { return Name; }
  }

  /// <summary>Label in the bottom shortcut bar, or null to keep it out of the bar.</summary>
  public virtual string? Bar
  {
    get { return null; }
  }

  /// <summary>Position in the bar and in `help`; lower comes first.</summary>
  public virtual int Order
  {
    get { return 100; }
  }

  /// <summary>
  /// For commands that need input: the text a shortcut puts in the command line (e.g. "rename notes.md") for you to
  /// edit and confirm with Enter. Null runs the command straight away.
  /// </summary>
  public virtual string? Prefill(Explorer x)
  {
    return null;
  }

  /// <summary>What running it now would do, shown next to the command line while you type.</summary>
  public virtual string Hint(Explorer x, ReadOnlySpan<char> args)
  {
    return Help;
  }

  public abstract void Run(Explorer x, ReadOnlySpan<char> args);

  /// <summary>Every command in the assembly, sorted by <see cref="Order"/> then name.</summary>
  public static Command[] DiscoverAll()
  {
    List<Command> found = new();
    Type[] types = typeof(Command).Assembly.GetTypes();
    for (int i = 0; i < types.Length; i++)
    {
      Type t = types[i];
      if (t.IsAbstract || !t.IsSubclassOf(typeof(Command)))
      {
        continue;
      }
      ConstructorInfo? ctor = t.GetConstructor(Type.EmptyTypes);
      if (ctor != null)
      {
        found.Add((Command)ctor.Invoke(null));
      }
    }
    found.Sort(static (a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : string.CompareOrdinal(a.Name, b.Name));
    return found.ToArray();
  }

  /// <summary>"^C", "Alt+←", "Del"... as shown in the bar and in help.</summary>
  public static string KeyLabel(Key key)
  {
    string text = key.ToString();
    ReadOnlySpan<char> s = text;
    if (s.StartsWith("Ctrl+", StringComparison.Ordinal) && s.Length == 6)
    {
      return string.Concat("^", s.Slice(5));
    }
    int plus = s.LastIndexOf('+');
    ReadOnlySpan<char> mods = plus < 0 ? ReadOnlySpan<char>.Empty : s.Slice(0, plus + 1);
    ReadOnlySpan<char> name = plus < 0 ? s : s.Slice(plus + 1);
    for (int i = 0; i < KeyNames.Length; i += 2)
    {
      if (name.SequenceEqual(KeyNames[i]))
      {
        return string.Concat(mods, KeyNames[i + 1]);
      }
    }
    return text;
  }

  /// <summary>Pairs of Terminal.Gui key names and how texp shows them.</summary>
  static readonly string[] KeyNames = ["CursorLeft", "←", "CursorRight", "→", "CursorUp", "↑", "CursorDown", "↓", "Delete", "Del", "Backspace", "Bksp", "PageUp", "PgUp", "PageDown", "PgDn", "Space", "Space", "Enter", "Enter"];
}
