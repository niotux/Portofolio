// Shadow workspace: one folder per (mirror, base) pair, made with `git worktree add` so the
// LS sees a full tree at base_sha without touching any seat's working copy.
//
// Layout under WorkspaceRoot:
//   <root>/<mirrorName>-<baseSha8>/   worktree at base_sha
//
// The mirror is a normal (working-tree) clone of the repo, refreshed to origin before a worktree
// is added. An unknown base falls back to origin/<defaultBranch> per the design research.
using System.Diagnostics;

namespace PiSharp.CodeIntel.Git;

public sealed class ShadowWorkspace
{
    public required string Root { get; init; }

    /// <summary>Local mirror of the repo (full clone with a working tree; the worktree source).</summary>
    public required string Mirror { get; init; }

    public required string Worktree { get; init; }

    public required string RepoUrl { get; init; }

    public required string BaseSha { get; init; }

    public string WorkspaceUri { get; init; } = string.Empty;

    public static string FileUriOf(string path)
    {
        var full = Path.GetFullPath(path);
        var name = Uri.EscapeDataString(Path.GetFileName(full));
        var dir = Path.GetDirectoryName(full) ?? "/";
        var prefix = OperatingSystem.IsWindows() ? "file://" + (char.IsLetter(dir[0]) ? dir[..2] + '/' : dir[1..]) : "file://" + dir;
        return $"{prefix}/{name}";
    }

    /// <summary>Creates (or reuses) the shadow workspace for <paramref name="base"/>, applying the
    /// seat's overlay file map (relative path -> content) afterwards. Idempotent: re-attaching an
    /// existing workspace just re-applies the overlay.</summary>
    public static ShadowWorkspace Create(string root, string mirror, string repoUrl, string baseRevision,
        IReadOnlyDictionary<string, string>? overlay = null, CancellationToken ct = default)
    {
        // 1. Make sure the mirror has the revision. An unknown base falls back to origin/<branch>:
        //    the caller passes "origin/main" explicitly in that case.
        var sha = Git.ResolveCommit(mirror, baseRevision).TrimEnd('\r', '\n');
        var base8 = sha[..8];

        // 2. worktree dir name: stable per (mirror name, base sha).
        var mirrorName = Path.GetFileName(mirror.TrimEnd('/'));
        var worktree = Path.Combine(root, $"{mirrorName}-{base8}");

        if (!Directory.Exists(worktree))
        {
            // Refresh the mirror's origin without touching its working tree. The worktree is created
            // inside the mirror repository, so the SHA must already be reachable there.
            //
            // `git worktree add` silently runs `git worktree prune` first: a worktree directory
            // left over from an earlier run (e.g. a previous test execution) is REMOVED before the
            // add, so the directory must not be created here. The parent directory is created so
            // that the path's parent exists; the worktree directory itself is created by git.
            var parent = Path.GetDirectoryName(worktree);
            if (parent is not null) Directory.CreateDirectory(parent);
            RunGit(mirror, ct, "fetch", "--quiet", "origin");
            RunGit(mirror, ct, "worktree", "add", worktree, sha, "--detach");
        }

        var workspace = new ShadowWorkspace
        {
            Root = root,
            Mirror = mirror,
            Worktree = worktree,
            RepoUrl = repoUrl,
            BaseSha = sha,
            WorkspaceUri = FileUriOf(worktree),
        };

        // 3. Apply the overlay: the seat's local edits on top of base_sha.
        if (overlay is not null && overlay.Count > 0)
        {
            foreach (var (relativePath, content) in overlay)
            {
                var target = SafeCombine(worktree, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllText(target, content);
            }
            // Make the overlay visible to `git ls-files`/status inside the worktree (CP1 finding:
            // ls-files reads the worktree's index and needs an explicit add for new files).
            foreach (var relativePath in overlay.Keys)
            {
                if (!File.Exists(SafeCombine(worktree, relativePath))) continue;
                Git.TryRun(worktree, out _, "add", "--", relativePath);
            }
        }

        return workspace;
    }

    private static void RunGit(string dir, CancellationToken ct, params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = dir,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        var result = PiSharp.Core.Processes.ProcessRunner.RunAsync(psi, TimeSpan.FromMinutes(5), ct).GetAwaiter().GetResult();
        if (result.ExitCode != 0)
            throw new GitException($"git {string.Join(' ', args)} exited {result.ExitCode}", result.Error);
    }

    private static string SafeCombine(string root, string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/').TrimStart('/');
        if (normalized.Split('/').Any(seg => seg is ".." or ""))
            throw new ArgumentException($"unsafe overlay path: {relativePath}", nameof(relativePath));
        var full = Path.GetFullPath(Path.Combine(root, normalized));
        if (!full.StartsWith(Path.GetFullPath(root), StringComparison.Ordinal))
            throw new ArgumentException($"overlay path escapes the workspace: {relativePath}", nameof(relativePath));
        return full;
    }

    public void WriteFile(string relativePath, string content, CancellationToken ct = default)
    {
        var target = SafeCombine(Worktree, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, content);
    }

    /// <summary>Removes the worktree (and only the worktree; the mirror survives).
    /// Idempotent: a missing worktree dir is not an error.</summary>
    public static void Remove(string mirror, string worktree, CancellationToken ct = default)
    {
        if (!Directory.Exists(worktree)) return;
        RunGit(mirror, ct, "worktree", "remove", "--force", worktree);
        // The detached checkout dir is gone; its entry in .git/worktrees may linger on error.
        RunGit(mirror, ct, "worktree", "prune");
    }
}
