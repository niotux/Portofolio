using System;
using System.Collections.Generic;
using System.IO;
using Texp.Core;
using Texp.Fs;
using Terminal.Gui.Input;

namespace Texp.Commands;

public sealed class RenameCommand : Command
{
  public override string Name
  {
    get { return "rename"; }
  }

  public override string[] Aliases
  {
    get { return ["ren"]; }
  }

  public override string Help
  {
    get { return "Rename the entry under the cursor"; }
  }

  public override Key[] Keys
  {
    get { return [Key.R.WithCtrl, Key.F2]; }
  }

  public override string Usage
  {
    get { return "rename <new name>"; }
  }

  public override string? Bar
  {
    get { return "rename"; }
  }

  public override int Order
  {
    get { return 30; }
  }

  public override string? Prefill(Explorer x)
  {
    return x.Tab.HasItem ? "rename " + x.Tab.Current.Name : null;
  }

  public override string Hint(Explorer x, ReadOnlySpan<char> args)
  {
    if (!x.Tab.HasItem)
    {
      return "nothing to rename";
    }
    if (args.IsEmpty)
    {
      return "rename " + x.Tab.Current.Name + " to …";
    }
    return FileOps.CheckName(args) ?? x.Tab.Current.Name + " → " + args.ToString();
  }

  public override void Run(Explorer x, ReadOnlySpan<char> args)
  {
    if (!x.Tab.HasItem)
    {
      return;
    }
    string name = x.Tab.Current.Name;
    if (args.IsEmpty)
    {
      x.Prefill("rename " + name);
      return;
    }
    if (args.SequenceEqual(name))
    {
      return;
    }
    string newName = args.ToString();
    FileOps.Rename(x.Tab.PathOf(name), newName);
    x.Tab.Load();
    x.Tab.Rebuild(x.Filter, x.ShowHidden);
    x.Tab.Select(newName);
    x.Flash("renamed to " + newName);
  }
}

public sealed class MkdirCommand : Command
{
  public override string Name
  {
    get { return "mkdir"; }
  }

  public override string[] Aliases
  {
    get { return ["folder", "md"]; }
  }

  public override string Help
  {
    get { return "Create a folder here"; }
  }

  public override Key[] Keys
  {
    get { return [Key.D.WithCtrl]; }
  }

  public override string Usage
  {
    get { return "mkdir <name>"; }
  }

  public override string? Bar
  {
    get { return "folder"; }
  }

  public override int Order
  {
    get { return 31; }
  }

  public override string? Prefill(Explorer x)
  {
    return "mkdir ";
  }

  public override string Hint(Explorer x, ReadOnlySpan<char> args)
  {
    if (args.IsEmpty)
    {
      return "type the new folder's name";
    }
    return FileOps.CheckName(args) ?? "create folder " + args.ToString() + " here";
  }

  public override void Run(Explorer x, ReadOnlySpan<char> args)
  {
    if (args.IsEmpty)
    {
      x.Prefill("mkdir ");
      return;
    }
    string name = args.ToString();
    FileOps.MakeFolder(x.Tab.Folder, name);
    x.Tab.Load();
    x.Tab.Rebuild(x.Filter, x.ShowHidden);
    x.Tab.Select(name);
  }
}

public sealed class NewCommand : Command
{
  const string EmptyFile = "Empty file";

  public override string Name
  {
    get { return "new"; }
  }

  public override string[] Aliases
  {
    get { return ["touch", "template"]; }
  }

  public override string Help
  {
    get { return "New file from a template (~/Templates); with a name, an empty file"; }
  }

  public override Key[] Keys
  {
    get { return [Key.N.WithCtrl]; }
  }

  public override string Usage
  {
    get { return "new [name]"; }
  }

  public override string? Bar
  {
    get { return "new"; }
  }

  public override int Order
  {
    get { return 32; }
  }

  public override string Hint(Explorer x, ReadOnlySpan<char> args)
  {
    if (args.IsEmpty)
    {
      return "pick a template";
    }
    return FileOps.CheckName(args) ?? "create empty file " + args.ToString();
  }

  public override void Run(Explorer x, ReadOnlySpan<char> args)
  {
    string folder = x.Tab.Folder;
    if (!args.IsEmpty)
    {
      string name = args.ToString();
      string? error = FileOps.CheckName(name);
      if (error != null)
      {
        x.Fail(error);
        return;
      }
      if (FileOps.Exists(Path.Combine(folder, name)))
      {
        x.Fail(name + " already exists");
        return;
      }
      FileOps.CreateFrom(folder, null, name);
      Created(x, name, false);
      return;
    }

    string templates = Places.Templates();
    List<Entry> found = new();
    Listing.Load(templates, found);
    found.Sort(new Listing.Comparer());
    List<string?> paths = new();
    Overlay picker = new("New file: pick a template", index =>
    {
      if (index >= paths.Count)
      {
        return;
      }
      string? template = paths[index];
      string name = template == null ? "Untitled" : Path.GetFileName(template);
      string created = FileOps.CreateFrom(folder, template, name);
      Created(x, created, true);
    });
    picker.Lines.Add(EmptyFile);
    paths.Add(null);
    for (int i = 0; i < found.Count; i++)
    {
      if (found[i].IsHidden)
      {
        continue;
      }
      picker.Lines.Add(found[i].IsDir ? found[i].Name + "/" : found[i].Name);
      paths.Add(Path.Combine(templates, found[i].Name));
    }
    if (picker.Lines.Count == 1)
    {
      picker.Lines.Add("(put files in " + Explorer.Tilde(templates) + " to get more choices)");
    }
    x.ShowOverlay(picker);
  }

  static void Created(Explorer x, string name, bool offerRename)
  {
    x.Tab.Load();
    x.Tab.Rebuild(x.Filter, x.ShowHidden);
    x.Tab.Select(name);
    if (offerRename)
    {
      // Like GNOME Files: the new file is created, then you're straight into naming it.
      x.Prefill("rename " + name);
      x.Tab.Select(name);
      x.Flash("created " + name + ": type a name and press Enter, or Esc to keep it");
    }
  }
}
