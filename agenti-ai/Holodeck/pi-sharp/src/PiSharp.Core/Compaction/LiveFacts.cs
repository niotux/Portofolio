using System.Diagnostics;
using System.Text;

namespace PiSharp.Core.Compaction;

/// <summary>The working tree as git sees it: branch, short HEAD sha, and the (capped) porcelain status.</summary>
public sealed record GitFacts(string? Branch, string? Head, IReadOnlyList<string> Status, int StatusTotal);

/// <summary>
/// Facts re-read from the world right after a compaction, so the model re-grounds on the present rather
/// than on its summary: the UTC time and the git state of the working directory. Every fact is optional;
/// one that cannot be read is left out and never fails the compaction.
/// </summary>
public static class LiveFacts
{
    public const int MaxStatusLines = 20;
    public static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(3);

    public static string Format(DateTimeOffset now, GitFacts? git)
    {
        var sb = new StringBuilder();
        sb.Append("[live facts after compaction]\n");
        sb.Append("UTC now: ").Append(now.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")).Append('\n');
        if (git is not null)
        {
            sb.Append("git: branch ").Append(git.Branch ?? "(detached)")
              .Append(" @ ").Append(git.Head ?? "(no commits)").Append('\n');
            if (git.StatusTotal == 0)
                sb.Append("git status: clean\n");
            else
            {
                sb.Append("git status --porcelain (").Append(git.StatusTotal).Append(" entries):\n");
                foreach (var line in git.Status) sb.Append("  ").Append(line).Append('\n');
                if (git.StatusTotal > git.Status.Count)
                    sb.Append("  ... and ").Append(git.StatusTotal - git.Status.Count).Append(" more\n");
            }
        }
        return sb.ToString().TrimEnd('\n');
    }

    /// <summary>The current facts for <paramref name="cwd"/>; never throws.</summary>
    public static string Read(string cwd)
    {
        GitFacts? git = null;
        try { git = ReadGit(cwd); } catch { }
        return Format(DateTimeOffset.UtcNow, git);
    }

    /// <summary>Git state of <paramref name="cwd"/>, or null when it is not a repository, git is missing or
    /// slow.</summary>
    public static GitFacts? ReadGit(string cwd)
    {
        var inside = RunGit(cwd, "rev-parse --is-inside-work-tree");
        if (inside?.Trim() != "true") return null;
        var branch = RunGit(cwd, "symbolic-ref --short -q HEAD")?.Trim();
        var head = RunGit(cwd, "rev-parse --short HEAD")?.Trim();
        var status = RunGit(cwd, "status --porcelain");
        if (status is null) return null;
        var lines = status.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToList();
        return new GitFacts(
            string.IsNullOrEmpty(branch) ? null : branch,
            string.IsNullOrEmpty(head) ? null : head,
            lines.Take(MaxStatusLines).ToList(),
            lines.Count);
    }

    private static string? RunGit(string cwd, string args)
    {
        try
        {
            var psi = new ProcessStartInfo("git", args)
            {
                WorkingDirectory = cwd,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            var result = PiSharp.Core.Processes.ProcessRunner.RunAsync(psi, GitTimeout).GetAwaiter().GetResult();
            return result.ExitCode == 0 ? result.Output : null;
        }
        catch
        {
            return null;
        }
    }
}
