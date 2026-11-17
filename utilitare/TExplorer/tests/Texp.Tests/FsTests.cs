using System;
using System.Collections.Generic;
using System.IO;
using Texp.Core;
using Texp.Fs;

namespace Texp.Tests;

public class ListingTests
{
  [Fact]
  public void NaturalOrderComparesNumbersByValue()
  {
    Assert.True(Listing.Natural("file2", "file10") < 0);
    Assert.True(Listing.Natural("File10", "file9") > 0);
    Assert.True(Listing.Natural("a", "B") < 0);
    Assert.True(Listing.Natural("img007", "img7") != 0);
    Assert.Equal(0, Listing.Natural("same", "same"));
  }

  [Fact]
  public void FoldersComeFirstWhateverTheSort()
  {
    List<Entry> rows = new()
    {
      new Entry("b.txt", false, false, 10, default, 0),
      new Entry("zdir", true, false, 0, default, 0),
      new Entry("a.txt", false, false, 99, default, 0),
    };
    Listing.Comparer c = new() { By = SortBy.Size, Descending = true };
    rows.Sort(c);
    Assert.Equal("zdir", rows[0].Name);
    Assert.Equal("a.txt", rows[1].Name);
  }
}

public class FileOpsTests
{
  [Fact]
  public void FreeNameAddsCopyThenNumbers()
  {
    using TempDir t = new();
    Assert.Equal("a.txt", FileOps.FreeName(t.Path, "a.txt", true));
    t.File("a.txt", "x");
    Assert.Equal("a (copy).txt", FileOps.FreeName(t.Path, "a.txt", true));
    t.File("a (copy).txt", "x");
    Assert.Equal("a (copy 2).txt", FileOps.FreeName(t.Path, "a.txt", true));
    Assert.Equal("a (2).txt", FileOps.FreeName(t.Path, "a.txt", false));
    t.File(".bashrc", "x");
    Assert.Equal(".bashrc (copy)", FileOps.FreeName(t.Path, ".bashrc", true));
  }

  [Fact]
  public void CheckNameRejectsSlashesAndDots()
  {
    Assert.NotNull(FileOps.CheckName(""));
    Assert.NotNull(FileOps.CheckName("a/b"));
    Assert.NotNull(FileOps.CheckName(".."));
    Assert.Null(FileOps.CheckName("ok name.txt"));
  }

  [Fact]
  public void IsInsideRespectsPathBoundaries()
  {
    Assert.True(FileOps.IsInside("/a/b/c", "/a/b"));
    Assert.True(FileOps.IsInside("/a/b", "/a/b"));
    Assert.False(FileOps.IsInside("/a/bc", "/a/b"));
    Assert.True(FileOps.IsInside("/x", "/"));
  }

  [Fact]
  public void CopyKeepsTreeLinksAndModesAndNeverOverwrites()
  {
    using TempDir t = new();
    string src = t.Dir("src");
    t.File("src/sub/deep.txt", "deep");
    string script = t.File("src/run.sh", "#!/bin/sh");
    File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    File.CreateSymbolicLink(Path.Combine(src, "link"), "sub/deep.txt");
    string dest = t.Dir("dest");
    t.Dir("dest/src");

    Job job = new("test");
    List<string> created = FileOps.Transfer([src], dest, false, job);

    Assert.Equal(["src (copy)"], created);
    string copy = Path.Combine(dest, "src (copy)");
    Assert.Equal("deep", File.ReadAllText(Path.Combine(copy, "sub", "deep.txt")));
    Assert.Equal("sub/deep.txt", new FileInfo(Path.Combine(copy, "link")).LinkTarget);
    Assert.True((File.GetUnixFileMode(Path.Combine(copy, "run.sh")) & UnixFileMode.UserExecute) != 0);
    Assert.Equal(job.Total, job.Done);
    Assert.True(Directory.Exists(src));
  }

  [Fact]
  public void CopyIntoSameFolderMakesADuplicate()
  {
    using TempDir t = new();
    string a = t.File("a.txt", "x");
    List<string> created = FileOps.Transfer([a], t.Path, false, new Job("t"));
    Assert.Equal(["a (copy).txt"], created);
  }

  [Fact]
  public void MoveRelocatesAndSkipsItsOwnFolder()
  {
    using TempDir t = new();
    string a = t.File("a.txt", "x");
    string dest = t.Dir("dest");
    Assert.Empty(FileOps.Transfer([a], t.Path, true, new Job("t")));
    Assert.True(File.Exists(a));

    FileOps.Transfer([a], dest, true, new Job("t"));
    Assert.False(File.Exists(a));
    Assert.True(File.Exists(Path.Combine(dest, "a.txt")));
  }

  [Fact]
  public void CannotCopyAFolderIntoItself()
  {
    using TempDir t = new();
    string d = t.Dir("d");
    string inner = t.Dir("d/inner");
    Assert.Throws<IOException>(() => FileOps.Transfer([d], inner, false, new Job("t")));
  }

  [Fact]
  public void RenameRefusesToClobber()
  {
    using TempDir t = new();
    string a = t.File("a.txt", "a");
    t.File("b.txt", "b");
    Assert.Throws<IOException>(() => FileOps.Rename(a, "b.txt"));
    FileOps.Rename(a, "c.txt");
    Assert.Equal("a", File.ReadAllText(Path.Combine(t.Path, "c.txt")));
  }

  [Fact]
  public void DeletingALinkLeavesItsTarget()
  {
    using TempDir t = new();
    string d = t.Dir("real");
    t.File("real/keep.txt", "k");
    string link = Path.Combine(t.Path, "link");
    Directory.CreateSymbolicLink(link, d);
    FileOps.DeletePermanently(link);
    Assert.False(FileOps.Exists(link));
    Assert.True(File.Exists(Path.Combine(d, "keep.txt")));
  }

  [Fact]
  public void CreateFromTemplateCopiesItUnderAFreeName()
  {
    using TempDir t = new();
    string template = t.File("tpl/Doc.txt", "hello");
    string here = t.Dir("here");
    Assert.Equal("Doc.txt", FileOps.CreateFrom(here, template, "Doc.txt"));
    Assert.Equal("Doc (2).txt", FileOps.CreateFrom(here, template, "Doc.txt"));
    Assert.Equal("hello", File.ReadAllText(Path.Combine(here, "Doc (2).txt")));
  }
}

public class PlacesTests
{
  [Fact]
  public void UserDirsExpandHome()
  {
    string file = "# comment\nXDG_DESKTOP_DIR=\"$HOME/Desktop\"\nXDG_MUSIC_DIR=\"/data/music/\"\nXDG_PUBLICSHARE_DIR=\"$HOME/\"\n";
    Assert.Equal("/home/u/Desktop", Places.UserDir(file, "XDG_DESKTOP_DIR", "/home/u"));
    Assert.Equal("/data/music", Places.UserDir(file, "XDG_MUSIC_DIR", "/home/u"));
    Assert.Equal("/home/u", Places.UserDir(file, "XDG_PUBLICSHARE_DIR", "/home/u"));
    Assert.Null(Places.UserDir(file, "XDG_VIDEOS_DIR", "/home/u"));
  }

  [Fact]
  public void BookmarksDecodeAndKeepLabels()
  {
    List<Place> into = new();
    Places.ParseBookmarks("file:///Space/My%20Code Work\nsftp://host/x\nfile:///tmp\n", into);
    Assert.Equal(2, into.Count);
    Assert.Equal("/Space/My Code", into[0].Path);
    Assert.Equal("Work", into[0].Label);
    Assert.Equal("tmp", into[1].Label);
  }

  [Fact]
  public void MountsUnderMediaAreDrives()
  {
    List<Place> into = new();
    Places.ParseMounts("/dev/sda1 / ext4 rw 0 0\n/dev/sdb1 /run/media/u/My\\040Disk vfat rw 0 0\n", into);
    Assert.Single(into);
    Assert.Equal("/run/media/u/My Disk", into[0].Path);
    Assert.Equal("My Disk", into[0].Label);
  }
}

public class LauncherTests
{
  [Fact]
  public void DetachedProgramsRunOnTheirOwn()
  {
    using TempDir t = new();
    string marker = Path.Combine(t.Path, "ran");
    Assert.Null(Launcher.Detached("touch", [marker], t.Path));
    for (int i = 0; i < 50 && !File.Exists(marker); i++)
    {
      System.Threading.Thread.Sleep(100);
    }
    Assert.True(File.Exists(marker));
  }

  [Fact]
  public void MissingProgramsAreReported()
  {
    Assert.NotNull(Launcher.Detached("no-such-program-texp", [], "/"));
  }

  [Fact]
  public void ProgramsRunInATerminalThatWaitsForEnter()
  {
    using TempDir t = new();
    string log = Path.Combine(t.Path, "terminal.log");
    // A stand-in terminal: records what it was asked to run, then runs it with Enter already pressed.
    string fake = t.File("fake-term", "#!/bin/sh\necho \"$@\" > '" + log + "'\n[ \"$1\" = -e ] && shift\necho | \"$@\" > '" + log + ".out'\n");
    File.SetUnixFileMode(fake, (UnixFileMode)0x1ED);
    string program = t.File("hello.sh", "#!/bin/sh\necho hi from $(basename \"$PWD\")\n");
    File.SetUnixFileMode(program, (UnixFileMode)0x1ED);

    string? before = Environment.GetEnvironmentVariable("TERMINAL");
    Environment.SetEnvironmentVariable("TERMINAL", fake);
    try
    {
      Assert.Null(Launcher.RunInTerminal(program, t.Path));
      string output = log + ".out";
      for (int i = 0; i < 50 && (!File.Exists(output) || new FileInfo(output).Length == 0); i++)
      {
        System.Threading.Thread.Sleep(100);
      }
      System.Threading.Thread.Sleep(200);
      string text = File.ReadAllText(output);
      Assert.Contains("hi from " + Path.GetFileName(t.Path), text);
      Assert.Contains("[hello.sh exited with code 0. Press Enter to close.]", text);
      Assert.StartsWith("-e sh -c", File.ReadAllText(log));
    }
    finally
    {
      Environment.SetEnvironmentVariable("TERMINAL", before);
    }
  }
}
