using PiSharp.Tools;

namespace PiSharp.Tests.Tools;

public class ToolPathsTests
{
    [Fact]
    public void ExpandPathStripsAtAndUnicodeSpaces()
    {
        var expanded = ToolPaths.ExpandPath("@foo\u00A0bar");
        Assert.Equal("foo bar", expanded);
    }

    [Fact]
    public void ExpandPathTildeGoesHome()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.Equal(home, ToolPaths.ExpandPath("~"));
        var nested = ToolPaths.ExpandPath("~/subdir");
        Assert.Equal(Path.Combine(home, "subdir"), nested);
    }

    [Fact]
    public void ResolveToCwdRelativeAndAbsolute()
    {
        using var tmp = new TempWorkspace();
        var relative = ToolPaths.ResolveToCwd("a/b.txt", tmp.Path);
        Assert.Equal(Path.GetFullPath(Path.Combine(tmp.Path, "a", "b.txt")), relative);

        var abs = Path.Combine(tmp.Path, "x");
        Assert.Equal(Path.GetFullPath(abs), ToolPaths.ResolveToCwd(abs, tmp.Path));
    }

    [Fact]
    public void RelativePosixUsesForwardSlashes()
    {
        using var tmp = new TempWorkspace();
        var file = tmp.File("sub/file.txt", "x");
        var rel = ToolPaths.RelativePosix(tmp.Path, file);
        Assert.Equal("sub/file.txt", rel);
    }

    [Fact]
    public void WindowsShellDrivePathIsNoOpOnLinux()
    {
        if (OperatingSystem.IsWindows()) return;
        Assert.Equal("/d/code", ToolPaths.NormalizeWindowsShellPath("/d/code"));
    }
}
