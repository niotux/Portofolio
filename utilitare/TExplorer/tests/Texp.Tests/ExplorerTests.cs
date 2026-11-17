using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using Texp.Commands;
using Texp.Core;
using Terminal.Gui.Input;
using Command = Texp.Commands.Command;

namespace Texp.Tests;

public class CommandRegistryTests
{
  [Fact]
  public void EveryNameAliasAndKeyIsUnique()
  {
    Command[] all = Command.DiscoverAll();
    List<string> names = new();
    List<Key> keys = new();
    for (int i = 0; i < all.Length; i++)
    {
      Assert.False(string.IsNullOrEmpty(all[i].Help), all[i].Name + " needs help text");
      names.Add(all[i].Name);
      names.AddRange(all[i].Aliases);
      keys.AddRange(all[i].Keys);
    }
    for (int i = 0; i < names.Count; i++)
    {
      for (int j = i + 1; j < names.Count; j++)
      {
        Assert.False(string.Equals(names[i], names[j], StringComparison.OrdinalIgnoreCase), "duplicate command name " + names[i]);
      }
    }
    for (int i = 0; i < keys.Count; i++)
    {
      for (int j = i + 1; j < keys.Count; j++)
      {
        Assert.False(keys[i] == keys[j], "duplicate key " + keys[i]);
      }
    }
  }

  [Fact]
  public void KeyLabelsAreShort()
  {
    Assert.Equal("^C", Command.KeyLabel(Key.C.WithCtrl));
    Assert.Equal("Del", Command.KeyLabel(Key.Delete));
  }
}

public class ExplorerTests
{
  /// <summary>Background jobs post back here; <see cref="Settle"/> runs them like the UI thread would.</summary>
  readonly BlockingCollection<Action> _posted = new();

  Explorer Make(string folder)
  {
    Explorer x = new(Command.DiscoverAll(), folder);
    x.Post = a => _posted.Add(a);
    return x;
  }

  void Settle(Explorer x)
  {
    while (x.Jobs.Count > 0)
    {
      Assert.True(_posted.TryTake(out Action? a, TimeSpan.FromSeconds(10)), "job never finished");
      a!();
    }
  }

  static void Type(Explorer x, string text)
  {
    x.Line.Clear();
    x.Line.Insert(text);
    x.LineChanged();
  }

  [Fact]
  public void TypedCommandsRunAndTextFilters()
  {
    using TempDir t = new();
    t.File("alpha.txt", "a");
    t.File("beta.txt", "b");
    Explorer x = Make(t.Path);

    Type(x, "be");
    Assert.Single(x.Tab.Rows);
    Assert.Null(x.LineCommand(out _));
    Assert.Equal("beta.txt", x.Tab.Current.Name);

    Type(x, "MKDIR new folder");
    Assert.NotNull(x.LineCommand(out ReadOnlySpan<char> args));
    Assert.Equal("new folder", args.ToString());
    Assert.True(x.Filter.IsEmpty);
    x.Submit();
    Assert.True(Directory.Exists(Path.Combine(t.Path, "new folder")));
    Assert.Equal("new folder", x.Tab.Current.Name);
    Assert.True(x.Line.IsEmpty);
  }

  [Fact]
  public void RenameShortcutPrefillsTheLine()
  {
    using TempDir t = new();
    t.File("notes.md", "n");
    Explorer x = Make(t.Path);
    Command rename = x.Find("rename")!;
    Assert.Equal("rename notes.md", rename.Prefill(x));

    Type(x, "rename todo.md");
    x.Submit();
    Assert.True(File.Exists(Path.Combine(t.Path, "todo.md")));
    Assert.Equal("todo.md", x.Tab.Current.Name);
  }

  [Fact]
  public void CopyThenPasteInAnotherTab()
  {
    using TempDir t = new();
    t.File("a.txt", "a");
    t.File("b.txt", "b");
    string other = t.Dir("other");
    Explorer x = Make(t.Path);

    x.Tab.Select("a.txt");
    x.Run(x.Find("mark")!, "");
    x.Tab.Select("b.txt");
    x.Run(x.Find("mark")!, "");
    x.Run(x.Find("copy")!, "");
    Assert.Equal(2, x.Clipboard.Count);
    Assert.Empty(x.Tab.Marked);

    x.Run(x.Find("tab")!, other);
    Assert.Equal(2, x.Tabs.Count);
    x.Run(x.Find("paste")!, "");
    Settle(x);
    Assert.False(x.MessageIsError, x.Message);
    Assert.True(File.Exists(Path.Combine(other, "a.txt")));
    Assert.True(File.Exists(Path.Combine(other, "b.txt")));
    Assert.Equal(2, x.Tab.ItemCount);
    Assert.True(File.Exists(Path.Combine(t.Path, "a.txt")));
  }

  [Fact]
  public void CutPasteMovesAndEmptiesTheClipboard()
  {
    using TempDir t = new();
    t.File("a.txt", "a");
    string other = t.Dir("other");
    Explorer x = Make(t.Path);
    x.Tab.Select("a.txt");
    x.Run(x.Find("cut")!, "");
    x.Go(other);
    x.Run(x.Find("paste")!, "");
    Settle(x);
    Assert.False(File.Exists(Path.Combine(t.Path, "a.txt")));
    Assert.True(File.Exists(Path.Combine(other, "a.txt")));
    Assert.Empty(x.Clipboard);
  }

  [Fact]
  public void PathsJumpAndUpLandsOnTheChild()
  {
    using TempDir t = new();
    string sub = t.Dir("zz/inner");
    t.File("aa.txt", "a");
    Explorer x = Make(t.Path);
    Type(x, sub);
    Assert.True(x.LineIsPath);
    x.Submit();
    Assert.Equal(sub, x.Tab.Folder);

    x.Run(x.Find("up")!, "");
    x.Run(x.Find("up")!, "");
    Assert.Equal(t.Path, x.Tab.Folder);
    Assert.Equal("zz", x.Tab.Current.Name);

    x.Run(x.Find("back")!, "");
    Assert.Equal(Path.Combine(t.Path, "zz"), x.Tab.Folder);
  }

  [Fact]
  public void TabCompletesCommandsThenNames()
  {
    using TempDir t = new();
    t.File("report-2026.pdf", "r");
    Explorer x = Make(t.Path);
    Type(x, "ren");
    Assert.Equal("rename", x.Suggestion()!.Name);
    x.Complete();
    Assert.Equal("rename ", x.Line.Text.ToString());
    x.Line.Insert("rep");
    x.Complete();
    Assert.Equal("rename report-2026.pdf", x.Line.Text.ToString());
  }

  [Fact]
  public void HiddenFilesShowOnRequest()
  {
    using TempDir t = new();
    t.File(".secret", "s");
    t.File("plain", "p");
    Explorer x = Make(t.Path);
    Assert.Equal(1, x.Tab.ItemCount);
    x.Run(x.Find("hidden")!, "");
    Assert.Equal(2, x.Tab.ItemCount);
  }

  [Fact]
  public void PermanentDeleteAsksFirst()
  {
    using TempDir t = new();
    string a = t.File("a.txt", "a");
    Explorer x = Make(t.Path);
    x.Run(x.Find("delete!")!, "");
    Assert.NotNull(x.Question);
    x.Answer(false);
    Assert.True(File.Exists(a));
    x.Run(x.Find("destroy")!, "");
    x.Answer(true);
    Settle(x);
    Assert.False(File.Exists(a));
  }

  [Fact]
  public void NewFromTemplatePicksThenOffersRename()
  {
    using TempDir t = new();
    Explorer x = Make(t.Path);
    x.Run(x.Find("new")!, "");
    Assert.NotNull(x.Overlay);
    Assert.True(x.Overlay!.IsPicker);
    x.PickOverlay(0);
    Assert.True(File.Exists(Path.Combine(t.Path, "Untitled")));
    Assert.Equal("rename Untitled", x.Line.Text.ToString());
  }

  [Fact]
  public void DotDotGoesUpAndIsNeverActedOn()
  {
    using TempDir t = new();
    string sub = t.Dir("sub");
    t.File("sub/a.txt", "a");
    Explorer x = Make(sub);
    Assert.True(x.Tab.Rows[0].IsParent);
    Assert.Equal("a.txt", x.Tab.Current.Name);

    x.Tab.Cursor = 0;
    Assert.Empty(x.Selection());
    Assert.Null(x.Find("rename")!.Prefill(x));
    x.Run(x.Find("all")!, "");
    Assert.Equal(["a.txt"], x.Tab.Marked);

    Type(x, "a.t");
    Assert.False(x.Tab.Rows[0].IsParent);

    x.ClearLine();
    x.Tab.Cursor = 0;
    x.Submit();
    Assert.Equal(t.Path, x.Tab.Folder);
    Assert.Equal("sub", x.Tab.Current.Name);
    Assert.DoesNotContain(Key.Backspace, x.Find("up")!.Keys);
  }
}
