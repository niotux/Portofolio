using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PiSharp.CodeIntel;
using PiSharp.CodeIntel.Git;

namespace PiSharp.Tests.CodeIntel;

public sealed class WorkspaceManagerMirrorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ci-mirror-{Guid.NewGuid():N}");

    public WorkspaceManagerMirrorTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public async Task EnsureMirror_RecoversAfterFailedCloneLeavesDirectory()
    {
        var workspaceRoot = Path.Combine(_root, "workspaces");
        var source = Path.Combine(_root, "source");
        Directory.CreateDirectory(source);
        var manager = CreateManager(workspaceRoot);

        var first = Assert.Throws<GitException>(() => EnsureMirror(manager, workspaceRoot, source));
        Assert.Contains("git clone", first.Message);
        var mirror = Path.Combine(workspaceRoot, "mirrors", "seat-repo");
        Assert.False(Directory.Exists(mirror));

        RunGit(source, "init", "-q");
        RunGit(source, "-c", "user.name=Test", "-c", "user.email=test@example.invalid", "commit", "--allow-empty", "-m", "initial", "-q");

        EnsureMirror(manager, workspaceRoot, source);
        await manager.DisposeAsync();

        Assert.True(File.Exists(Path.Combine(mirror, ".git", "HEAD")));
        Assert.Equal("true", RunGit(mirror, "rev-parse", "--is-inside-work-tree").Trim());
    }

    [Fact]
    public async Task EnsureMirror_RefreshPreservesValidMirrorAndItsWorktree()
    {
        var workspaceRoot = Path.Combine(_root, "preserve-workspaces");
        var source = Path.Combine(_root, "valid-source");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "keep.txt"), "kept");
        RunGit(source, "init", "-q");
        RunGit(source, "-c", "user.name=Test", "-c", "user.email=test@example.invalid", "add", "keep.txt");
        RunGit(source, "-c", "user.name=Test", "-c", "user.email=test@example.invalid", "commit", "-m", "initial", "-q");

        var manager = CreateManager(workspaceRoot);
        var mirror = Path.Combine(workspaceRoot, "mirrors", "seat-repo");
        EnsureMirror(manager, workspaceRoot, source);
        var worktree = Path.Combine(_root, "active-worktree");
        RunGit(mirror, "worktree", "add", "-b", "active", worktree);

        EnsureMirror(manager, workspaceRoot, source);
        await manager.DisposeAsync();

        Assert.True(File.Exists(Path.Combine(mirror, ".git", "HEAD")));
        Assert.Equal("kept", File.ReadAllText(Path.Combine(worktree, "keep.txt")));
        Assert.Equal(worktree, RunGit(worktree, "rev-parse", "--show-toplevel").Trim());
    }

    private WorkspaceManager CreateManager(string workspaceRoot)
    {
        var options = Options.Create(new CodeIntelOptions { WorkspaceRoot = workspaceRoot });
        var service = Options.Create(new CodeIntelServiceOptions());
        return new WorkspaceManager(options, service, NullLogger<WorkspaceManager>.Instance);
    }

    private static void EnsureMirror(WorkspaceManager manager, string workspaceRoot, string repo)
    {
        var method = typeof(WorkspaceManager).GetMethod("EnsureMirror", BindingFlags.Instance | BindingFlags.NonPublic)!;
        try { method.Invoke(manager, [Path.Combine(workspaceRoot, "mirrors", "seat-repo"), repo, CancellationToken.None]); }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
        }
    }

    private static string RunGit(string directory, params string[] args)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(30_000)) { process.Kill(true); throw new TimeoutException("git timed out"); }
        if (process.ExitCode != 0) throw new InvalidOperationException($"git {string.Join(' ', args)} failed: {error}");
        return output;
    }
}
