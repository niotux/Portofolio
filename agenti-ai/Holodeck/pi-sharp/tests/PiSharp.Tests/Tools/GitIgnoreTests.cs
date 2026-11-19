using PiSharp.Tools;

namespace PiSharp.Tests.Tools;

public class GitIgnoreTests
{
    [Fact]
    public void StarMatchesInAnyDirectory()
    {
        using var tmp = new TempWorkspace();
        tmp.File(".gitignore", "*.log\n");
        tmp.File("a.log", "x");
        tmp.File("keep.txt", "x");
        tmp.File("sub/b.log", "x");
        tmp.File("sub/keep.txt", "x");

        var files = GitIgnore.WalkFiles(tmp.Path).Select(p => ToolPaths.RelativePosix(tmp.Path, p)).ToHashSet();
        Assert.Contains("keep.txt", files);
        Assert.Contains("sub/keep.txt", files);
        Assert.DoesNotContain("a.log", files);
        Assert.DoesNotContain("sub/b.log", files);
    }

    [Fact]
    public void NegationReIncludes()
    {
        using var tmp = new TempWorkspace();
        tmp.File(".gitignore", "*.log\n!keep.log\n");
        tmp.File("a.log", "x");
        tmp.File("keep.log", "x");

        var files = GitIgnore.WalkFiles(tmp.Path).Select(p => ToolPaths.RelativePosix(tmp.Path, p)).ToHashSet();
        Assert.Contains("keep.log", files);
        Assert.DoesNotContain("a.log", files);
    }

    [Fact]
    public void SkipsGitDirectory()
    {
        using var tmp = new TempWorkspace();
        tmp.File(".git/HEAD", "ref: refs/heads/main");
        tmp.File("ok.txt", "x");
        var files = GitIgnore.WalkFiles(tmp.Path).Select(p => ToolPaths.RelativePosix(tmp.Path, p)).ToHashSet();
        Assert.Contains("ok.txt", files);
        Assert.DoesNotContain(files, f => f.StartsWith(".git"));
    }

    [Fact]
    public void IncludesHiddenFiles()
    {
        using var tmp = new TempWorkspace();
        tmp.File(".secret", "x");
        var files = GitIgnore.WalkFiles(tmp.Path).Select(p => ToolPaths.RelativePosix(tmp.Path, p)).ToHashSet();
        Assert.Contains(".secret", files);
    }

    [Fact]
    public void NestedGitignore()
    {
        using var tmp = new TempWorkspace();
        tmp.File(".gitignore", "*.tmp\n");
        tmp.File("sub/.gitignore", "*.skip\n");
        tmp.File("a.tmp", "x");
        tmp.File("sub/b.tmp", "x");
        tmp.File("sub/c.skip", "x");
        tmp.File("sub/ok.txt", "x");

        var files = GitIgnore.WalkFiles(tmp.Path).Select(p => ToolPaths.RelativePosix(tmp.Path, p)).ToHashSet();
        Assert.Contains("sub/ok.txt", files);
        Assert.DoesNotContain("a.tmp", files);
        Assert.DoesNotContain("sub/b.tmp", files);
        Assert.DoesNotContain("sub/c.skip", files);
    }

    [Fact]
    public void AnchoredSlashOnlyMatchesAtIgnoreRoot()
    {
        var rules = GitIgnore.Parse("/secret.txt\n");
        var file = new GitIgnoreFile { Directory = "/repo", Rules = rules, HasNegation = false };
        Assert.True(GitIgnore.IsIgnored("secret.txt", false, "/repo", [file]));
        Assert.False(GitIgnore.IsIgnored("nested/secret.txt", false, "/repo", [file]));
    }

    [Fact]
    public void FindGlobBasenameVsFullPath()
    {
        Assert.True(Glob.MatchFind("src/foo.ts", "*.ts"));
        Assert.False(Glob.MatchFind("src/foo.ts", "*.json"));
        Assert.True(Glob.MatchFind("src/foo.spec.ts", "src/**/*.spec.ts"));
        Assert.False(Glob.MatchFind("other/foo.spec.ts", "src/**/*.spec.ts"));
        Assert.True(Glob.MatchFind("foo.ts", "*.ts"));
    }
}
