// Git helper for the shadow workspaces. code_attach clones the repo with
// `git worktree add <dir> <base_sha>` so the shadow tree does not touch the caller's working
// copy, applies the seat's overlay on top, and code_sync writes the seat's changed files in.
// Restore removes the worktree.
//
// CP1 findings (git 2.43, verified on coder-06, see notebook cp1-git-semantics-findings):
//   - `git worktree add <dir> <sha>` on a bare repo is unusable: the bare clone has no working
//     tree and the new worktree would need one; we therefore clone with a working tree and add
//     the worktree from it.
//   - `git ls-files` in a worktree reads the *worktree's* index; an overlay applied after the
//     worktree was added does not show up in ls-files until `git update-index --add` is run.
using System.Diagnostics;
using System.Text;

namespace PiSharp.CodeIntel.Git;

public sealed class GitException : Exception
{
    public GitException(string message, string? detail = null) : base(detail is null ? message : $"{message}\n{detail}") { }
}

/// <summary>Runs git in a directory. Every command is a short array of arguments; no shell.</summary>
public static class Git
{
    public static string Run(string workingDirectory, params string[] args)
    {
        var (exit, stdout, stderr) = RunCapture(workingDirectory, args);
        if (exit != 0)
            throw new GitException($"git {string.Join(' ', args)} exited {exit} in {workingDirectory}", stderr);
        return stdout;
    }

    public static bool TryRun(string workingDirectory, out string stderr, params string[] args)
    {
        var (exit, _, err) = RunCapture(workingDirectory, args);
        stderr = err;
        return exit == 0;
    }

    private static (int Exit, string Stdout, string Stderr) RunCapture(string workingDirectory, string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        var timeout = TimeSpan.FromMinutes(5);
        try
        {
            var result = PiSharp.Core.Processes.ProcessRunner.RunAsync(psi, timeout).GetAwaiter().GetResult();
            return (result.ExitCode, result.Output, result.Error);
        }
        catch (Exception e) when (e is System.IO.IOException or TimeoutException)
        {
            throw new GitException($"git {string.Join(' ', args)} could not start: {e.Message}");
        }
    }

    public static string ResolveCommit(string repository, string revision) =>
        Run(repository, "rev-parse", "--verify", revision + "^{commit}");
}
