// pi-sharp-checkpoints-1 (slice 1 of the pi-sharp-checkpoints epic, doc `research`): the shadow
// snapshot store. A per-workspace git dir that holds checkpoint commits; the seat's own .git,
// index, refs and objects are never opened by it. Every git call runs with
// --git-dir=<shadow> --work-tree=<cwd> and GIT_INDEX_FILE=<shadow>/index.
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace PiSharp.Core.Checkpoints;

/// <summary>Options for the shadow snapshot store. Defaults from the epic's design (doc `research`).</summary>
public sealed record CheckpointConfig
{
    /// <summary>Number of recent non-green checkpoints to retain per workspace.</summary>
    public int Keep { get; init; } = 50;
    /// <summary>Maximum bytes of one file that enters a snapshot; larger files are skipped.</summary>
    public long MaxFileBytes { get; init; } = 5 * 1024 * 1024;
    /// <summary>Maximum total bytes of one snapshot; past it the snapshot is partial.</summary>
    public long MaxDirBytes { get; init; } = 1024L * 1024 * 1024;
    /// <summary>Maximum files of one snapshot (the WorkspaceEvidence budget).</summary>
    public int MaxFiles { get; init; } = 20000;
    /// <summary>Seconds for one snapshot (the WorkspaceEvidence budget).</summary>
    public int TimeoutSeconds { get; init; } = 10;
}

/// <summary>Outcome of checkpoint retention and storage maintenance.</summary>
public sealed record CheckpointMaintenanceResult
{
    public int RemovedCheckpoints { get; init; }
    public long StoreBytes { get; init; }
    public bool GarbageCollected { get; init; }
    /// <summary>Non-null when enforcing the size cap required dropping a green checkpoint from the last 24 hours.</summary>
    public string? Alert { get; init; }
}

/// <summary>Outcome of one <see cref="CheckpointStore.CreateSnapshotAsync"/>.</summary>
public sealed record SnapshotResult
{
    public required bool Created { get; init; }
    public string? Commit { get; init; }
    public int? Files { get; init; }
    public bool Partial { get; init; }
    /// <summary>
    /// Why no snapshot was created (unchanged, merge in progress, budget, ...). Null when one was.
    /// </summary>
    public string? SkipReason { get; init; }
    /// <summary>
    /// The real <c>rev-parse HEAD</c> captured before the tree is written: what the session
    /// stood on when this checkpoint was taken. Null when no snapshot was created or HEAD was
    /// unreadable. The rewind guard compares it against the current HEAD before restoring.
    /// </summary>
    public string? Head { get; init; }
}

/// <summary>
/// Result of <see cref="CheckpointStore.PlanAsync"/>: what a restore of <c>shadowCommit</c> would do.
/// Counts are exact; <see cref="Paths"/> is an uncapped, unordered subset of the changed paths
/// (length at most 40) for the dry-run report. Paths are repo-relative (forward slashes, no
/// leading slash), the same form as <see cref="CheckpointStore.FilesOf"/>. Refusal never throws:
/// <see cref="Ok"/> is false and <see cref="Reason"/> names why; <see cref="Reason"/> is null on success.
/// </summary>
public sealed record RewindPlan
{
    public required bool Ok { get; init; }
    public string? Reason { get; init; }
    public int Changed { get; init; }
    public int Deleted { get; init; }
    public IReadOnlyList<string> Paths { get; init; } = [];
}

/// <summary>
/// Result of <see cref="CheckpointStore.RestoreAsync"/>: <see cref="Touched"/> is every path that
/// was written or deleted (uncapped), repo-relative, the same form as <see cref="CheckpointStore.FilesOf"/>.
/// A no-op restore (the tree already matches) is a success: <see cref="Applied"/> true, empty
/// <see cref="Touched"/>. Refusal never throws: <see cref="Applied"/> false, <see cref="Reason"/>
/// names why ("head unknown" or "head moved: \u003cold\u003e..\u003cnew\u003e"), no writes were made.
/// </summary>
public sealed record RewindOutcome
{
    public required bool Applied { get; init; }
    public string? Reason { get; init; }
    public IReadOnlyList<string> Touched { get; init; } = [];
}

/// <summary>
/// One shadow git dir per workspace: <c>Path.GetFullPath(Path.Combine(sessionDir, "..", "checkpoints"))/
/// <see cref="HashOf(string)"/>.git</c>. Snapshots are written through the seat's work tree with
/// <c>update-index --add --remove</c> + <c>write-tree</c> + <c>commit-tree</c>; the chain is the ref
/// <c>refs/pi/cp/N</c>. Skips nested repos (gitlinks), submodules, symlinks and files over
/// <see cref="CheckpointConfig.MaxFileBytes"/>. Budgets reuse the WorkspaceEvidence ones (20000 files,
/// 128 MB default, 10 s); over the byte budget a snapshot is created from what fit and flagged
/// <see cref="SnapshotResult.Partial"/>.
/// </summary>
public sealed class CheckpointStore
{
    private readonly string _cwd;
    private readonly CheckpointConfig _config;
    private long _lastFingerprint;
    private int _count;
    private bool _countInitialized;

    public string Cwd => _cwd;
    public CheckpointConfig Config => _config;
    public string GitDir { get; }

    /// <summary>SHA-1 hex of the normalized cwd: the per-workspace store name.</summary>
    public static string HashOf(string cwd)
    {
        var normalized = Path.GetFullPath(cwd).TrimEnd('/', '\\').Replace('\\', '/');
        if (OperatingSystem.IsWindows()) normalized = normalized.ToLowerInvariant();
        return Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
    }

    public static string DefaultDir(string sessionDir) =>
        Path.Combine(Path.GetFullPath(Path.Combine(sessionDir, "..")), "checkpoints");

    /// <summary>Resolve a path inside a work tree to its canonical Git work-tree root.
    /// Returning null is intentional: callers must skip rather than guess a workspace.</summary>
    public static string? ResolveWorkTreeRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            var fullPath = Path.GetFullPath(path);
            if (File.Exists(fullPath)) fullPath = Path.GetDirectoryName(fullPath)!;
            if (!Directory.Exists(fullPath)) return null;
            var psi = new ProcessStartInfo("git")
            {
                WorkingDirectory = fullPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("rev-parse");
            psi.ArgumentList.Add("--show-toplevel");
            var result = PiSharp.Core.Processes.ProcessRunner.RunAsync(psi, TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
            var output = result.Output.Trim();
            if (result.ExitCode != 0 || output.Length == 0) return null;
            var info = new DirectoryInfo(Path.GetFullPath(output));
            while (info.LinkTarget is not null && info.ResolveLinkTarget(true) is { } target)
                info = new DirectoryInfo(target.FullName);
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(info.FullName));
        }
        catch { return null; }
    }

    /// <summary>The cwd must be a git repository; the shadow dir itself must not be inside it.</summary>
    public CheckpointStore(string cwd, string checkpointsDir, CheckpointConfig? config = null)
    {
        _cwd = ResolveWorkTreeRoot(cwd) ?? Path.GetFullPath(cwd);
        _config = config ?? new CheckpointConfig();
        GitDir = Path.Combine(Path.GetFullPath(checkpointsDir), HashOf(_cwd) + ".git");
        // Previous releases folded Linux paths to lower case. Leave those stores intact; new identities
        // start independently so an ambiguous legacy store can never be silently attached to another tree.
        if (File.Exists(Path.Combine(GitDir, "config")))
        {
            var registered = File.ReadLines(Path.Combine(GitDir, "config"))
                .Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("worktree =", StringComparison.Ordinal));
            if (registered is not null && !string.Equals(Path.GetFullPath(registered[(registered.IndexOf('=') + 1)..].Trim()), _cwd,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new InvalidDataException("Checkpoint store belongs to a different worktree; existing snapshots were preserved.");
        }
        // The shadow dir must be a real, non-bare git dir (bare=false + core.worktree=<cwd>)
        // before any shadow command runs. A bare dir is a no-go: git 2.43 rejects
        // --git-dir=<bare> --work-tree=<path> wholesale ("not a git repository"), and a
        // git init'ed bare dir is unusable for our purposes. core.worktree plus per-call
        // --git-dir/--work-tree covers every shadow call; a relative core.worktree would be
        // resolved from the process CWD, so it is always absolute.
        // core.untrackedCache=true keeps the first ls-files over a large tree fast (no
        // per-file lstat of untracked files); without it the first snapshot is slow.
        // A store made before info/attributes existed ran the work tree's own filters: in an LFS repository
        // (hc2000-emu) git-lfs's clean filter copied every asset into <store>/lfs/objects, 1.4 GB per store,
        // which no gc reclaims (strata, 2026-10-22: 7.4 GB in one seat). Its snapshots hold LFS pointers, not
        // the files, so a restore from it would be wrong too: such a store starts over.
        if (Directory.Exists(Path.Combine(GitDir, "lfs")))
        {
            try { Directory.Delete(GitDir, recursive: true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Trace.TraceWarning("Checkpoint store reset failed: {0}", e.Message); }
        }
        PruneOrphans(Path.GetFullPath(checkpointsDir), GitDir);
        Directory.CreateDirectory(GitDir);
        Directory.CreateDirectory(Path.Combine(GitDir, "objects", "info"));
        Directory.CreateDirectory(Path.Combine(GitDir, "info"));
        // Highest-precedence attributes: no clean/smudge filter (git-lfs or any other) runs in the shadow, so a
        // snapshot stores the file's own bytes, under MaxFileBytes like any file, and nothing lands outside objects/.
        File.WriteAllText(Path.Combine(GitDir, "info", "attributes"), "* -filter\n");
        Directory.CreateDirectory(Path.Combine(GitDir, "refs", "heads"));
        Directory.CreateDirectory(Path.Combine(GitDir, "refs", "tags"));
        File.WriteAllText(Path.Combine(GitDir, "HEAD"), "ref: refs/heads/main\n");
        var repoConfig = new StringBuilder();
        repoConfig.AppendLine("[core]");
        repoConfig.AppendLine("\trepositoryformatversion = 0");
        repoConfig.AppendLine("\tfilemode = true");
        repoConfig.AppendLine("\tbare = false");
        repoConfig.AppendLine("\tlogallrefupdates = true");
        repoConfig.AppendLine($"\tworktree = {_cwd}");
        repoConfig.AppendLine("\tuntrackedCache = true");
        File.WriteAllText(Path.Combine(GitDir, "config"), repoConfig.ToString());
    }

    /// <summary>
    /// Deletes the stores beside <paramref name="keep"/> whose work tree is gone (one store per checkout, and a
    /// seat's checkouts go when their item leaves it, so without this their stores stayed for ever) and every
    /// store that still carries an LFS copy. Runs whenever a store is opened, so the first start of a fixed
    /// pi-sharp clears a seat's whole checkpoints directory.
    /// </summary>
    public static int PruneOrphans(string checkpointsDir, string? keep = null)
    {
        if (!Directory.Exists(checkpointsDir)) return 0;
        var removed = 0;
        foreach (var store in Directory.EnumerateDirectories(checkpointsDir, "*.git"))
        {
            if (keep is not null && Path.GetFullPath(store) == Path.GetFullPath(keep)) continue;
            string? worktree = null;
            try
            {
                foreach (var line in File.ReadLines(Path.Combine(store, "config")))
                {
                    var t = line.Trim();
                    if (t.StartsWith("worktree", StringComparison.Ordinal) && t.IndexOf('=') is var eq and > 0)
                        worktree = t[(eq + 1)..].Trim();
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
            // A store with an LFS copy (made before info/attributes) is dropped too: it is the 1.4 GB per checkout
            // this sweep exists for, and its snapshots hold pointers, not files. Its workspace starts a new one.
            var lfsCopy = Directory.Exists(Path.Combine(store, "lfs"));
            if (!lfsCopy && (string.IsNullOrEmpty(worktree) || !Path.IsPathFullyQualified(worktree) || Directory.Exists(worktree))) continue;
            try { Directory.Delete(store, recursive: true); removed++; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Trace.TraceWarning("Checkpoint store prune failed: {0}", e.Message); }
        }
        return removed;
    }

    private sealed record GitResult(int ExitCode, string Stdout, string Stderr);

    /// <summary>
    /// Drop entries that live inside the shadow git dir. The shadow dir is created under the
    /// checkpoints root, which can itself be inside the work tree; <c>ls-files --others</c> would
    /// then list the shadow's own files (index, objects, refs) as untracked, and those change on
    /// every snapshot - so the fingerprint would never be stable and the snapshot itself would
    /// swallow its own store. A path is excluded when its full path is GitDir or nested under it.
    /// </summary>
    private bool IsShadowPath(string relative)
    {
        if (relative == ".git" || relative.StartsWith(".git/", StringComparison.Ordinal)) return true;
        var full = Path.GetFullPath(Path.Combine(_cwd, relative));
        var git = Path.GetFullPath(GitDir);
        return full == git || full.StartsWith(git + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private async Task<GitResult> Git(
        string workingDir,
        Dictionary<string, string?>? extraEnv,
        byte[]? stdinData,
        string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdinData is not null,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        if (extraEnv is not null)
        {
            foreach (var (key, value) in extraEnv)
                psi.Environment[key] = value ?? "";
        }
        using var p = Process.Start(psi)!;
        if (stdinData is not null)
        {
            await p.StandardInput.BaseStream.WriteAsync(stdinData);
            p.StandardInput.Close();
        }
        var stdout = await p.StandardOutput.ReadToEndAsync();
        var stderr = await p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        return new GitResult(p.ExitCode, stdout, stderr);
    }

    /// <summary>Git inside the real repository (working tree <c>cwd</c>); never points at the shadow dir.</summary>
    private Task<GitResult> RealGit(params string[] args) => Git(_cwd, null, null, args);

    /// <summary>Git inside the shadow repository: its own index, its own refs, the seat's work tree.</summary>
    private Task<GitResult> ShadowGit(string command, byte[]? stdinData, params string[] args)
    {
        var env = new Dictionary<string, string?>
        {
            ["GIT_INDEX_FILE"] = Path.Combine(GitDir, "index"),
            ["GIT_AUTHOR_NAME"] = "pi-sharp",
            ["GIT_AUTHOR_EMAIL"] = "pi-sharp@localhost",
            ["GIT_COMMITTER_NAME"] = "pi-sharp",
            ["GIT_COMMITTER_EMAIL"] = "pi-sharp@localhost",
        };
        var prefix = new[] { "--git-dir", GitDir, "--work-tree", _cwd };
        var all = new string[prefix.Length + 1 + args.Length];
        Array.Copy(prefix, all, prefix.Length);
        all[prefix.Length] = command;
        Array.Copy(args, 0, all, prefix.Length + 1, args.Length);
        return Git(_cwd, env, stdinData, all);
    }

    /// <summary>
    /// Tree fingerprint for skip-on-unchanged. The shadow index's stat cache is useless here:
    /// <c>update-index --add</c> refreshes it to the current worktree stat on every snapshot, so
    /// it is stable across a real edit and the shadow index alone is blind to changes. The
    /// fingerprint therefore reads the worktree files directly: the real HEAD (moving it means
    /// the baseline changed) and, for every enumerated path, its mtime (nanosecond precision)
    /// and size. An edit, add or delete changes that set, so the next snapshot runs. It
    /// deliberately does NOT include the shadow checkpoint ref, which advances on every
    /// successful snapshot by design and would otherwise make every fingerprint differ.
    /// 0 when the fingerprint is not available (not a repository yet); the first snapshot always
    /// runs.
    /// </summary>
    public async Task<long> FingerprintAsync(CancellationToken ct = default)
    {
        var head = await RealGit("rev-parse", "HEAD");
        if (head.ExitCode != 0) return 0;
        var root = await RealRootAsync();
        if (root is null) return 0;
        var listed = (await ShadowGit("ls-files", null, "--cached", "--others", "--exclude-standard", "-z")).Stdout
            .Split('\0', StringSplitOptions.RemoveEmptyEntries).ToArray();
        var sb = new StringBuilder(head.Stdout.Trim().Length + listed.Length * 8);
        sb.Append(head.Stdout.Trim()).Append('\0');
        Array.Sort(listed, StringComparer.Ordinal);
        foreach (var relative in listed)
        {
            ct.ThrowIfCancellationRequested();
            if (IsShadowPath(relative)) continue;
            var file = new FileInfo(Path.Combine(root, relative));
            sb.Append(relative)
              .Append(' ').Append(file.Exists ? file.LastWriteTimeUtc.Ticks.ToString() : "-")
              .Append(' ').Append(file.Exists ? file.Length.ToString() : "-")
              .Append('\0');
        }
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        unchecked
        {
            return ((long)digest[0] << 56 | (long)digest[1] << 48 | (long)digest[2] << 40 | (long)digest[3] << 32 |
                    (long)digest[4] << 24 | (long)digest[5] << 16 | (long)digest[6] << 8 | digest[7]) & 0x7FFFFFFFFFFFFFFFL;
        }
    }

    /// <summary>Merge or rebase in progress in the real repository (MERGE_HEAD or a rebase dir).</summary>
    public async Task<bool> InMergeOrRebaseAsync(CancellationToken ct = default)
    {
        var root = await RealGit("rev-parse", "--git-dir");
        if (root.ExitCode != 0) return false;
        // The value is relative to the working directory (".git"), so resolve it against the
        // repo, not the process CWD, before GetFullPath.
        var gitDir = root.Stdout.Trim();
        if (gitDir.Length > 0 && gitDir[0] != '/') gitDir = Path.Combine(_cwd, gitDir);
        gitDir = Path.GetFullPath(gitDir);
        if (File.Exists(Path.Combine(gitDir, "MERGE_HEAD"))) return true;
        foreach (var dir in new[] { "rebase-merge", "rebase-apply" })
            if (Directory.Exists(Path.Combine(gitDir, dir))) return true;
        return false;
    }

    private async Task<string?> RealRootAsync()
    {
        var root = await RealGit("rev-parse", "--show-toplevel");
        return root.ExitCode == 0 ? root.Stdout.Trim() : null;
    }

    /// <summary>
    /// Create one checkpoint of the current work tree. Skips (returns a result with
    /// <see cref="SnapshotResult.SkipReason"/>) when the fingerprint is unchanged since the last
    /// snapshot, or a merge/rebase is in progress. Otherwise: enumerate the tree with
    /// <c>ls-files --cached --others --exclude-standard -z</c>, skip nested repos, submodules,
    /// symlinks and files over the size cap, feed the list to the shadow index with
    /// <c>update-index --add --remove -z --stdin</c>, then <c>write-tree</c> + <c>commit-tree</c>
    /// parented on the previous checkpoint and update <c>refs/pi/cp/N</c>.
    /// </summary>
    public async Task<SnapshotResult> CreateSnapshotAsync(CancellationToken ct = default)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(_config.TimeoutSeconds));

            if (await InMergeOrRebaseAsync(timeout.Token))
                return new SnapshotResult { Created = false, SkipReason = "merge or rebase in progress" };

            var fingerprint = await FingerprintAsync(timeout.Token);
            if (fingerprint != 0 && fingerprint == _lastFingerprint)
                return new SnapshotResult { Created = false, SkipReason = "unchanged since last snapshot" };

            var root = await RealRootAsync();
            if (root is null)
                return new SnapshotResult { Created = false, SkipReason = "cwd is not a git repository" };

            if (!_countInitialized)
            {
                var existing = await ShadowGit("for-each-ref", null, "--format=%(refname)", "refs/pi/cp/");
                if (existing.ExitCode == 0)
                    _count = existing.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                        .Select(name => name.StartsWith("refs/pi/cp/", StringComparison.Ordinal) ? name["refs/pi/cp/".Length..] : "")
                        .Where(name => int.TryParse(name, out _))
                        .Select(name => int.Parse(name, System.Globalization.CultureInfo.InvariantCulture))
                        .DefaultIfEmpty(0).Max();
                _countInitialized = true;
            }

            var realHead = (await RealGit("rev-parse", "HEAD")).Stdout.Trim();

            // --cached --others enumerates the union of the shadow index and the work tree:
            // every previous snapshot that is still present, plus everything new or re-added.
            // (Plain --cached would return the index contents only and miss work-tree files
            // that are not in the shadow index.)
            var listed = (await ShadowGit("ls-files", null, "--cached", "--others", "--exclude-standard", "-z")).Stdout
                .Split('\0', StringSplitOptions.RemoveEmptyEntries)
                .Where(p => !IsShadowPath(p))
                .ToArray();
            if (listed.Length > _config.MaxFiles)
                return new SnapshotResult { Created = false, Partial = true, SkipReason = $"file budget exceeded: {listed.Length} > {_config.MaxFiles}" };

            var files = new List<string>(listed.Length);
            long totalBytes = 0;
            var partial = false;
            foreach (var relative in listed)
            {
                timeout.Token.ThrowIfCancellationRequested();
                // Nested repositories: a .git entry inside the tree, or a gitlink (mode 160000, submodule).
                // (The shadow dir itself is already excluded via IsShadowPath above.)
                var segments = relative.Split('/', 2);
                if (segments.Length == 2 && segments[1] == ".git") continue;

                var file = new FileInfo(Path.Combine(root, relative));
                if (!file.Exists) continue; // vanished between enumeration and stat
                if (file.LinkTarget is not null) { partial = false; continue; } // symlinks are not stored
                try
                {
                    var length = file.Length;
                    if (length > _config.MaxFileBytes)
                    {
                        // A per-file cap also makes the snapshot partial: something the agent
                        // wrote is not in the checkpoint.
                        partial = true;
                        continue;
                    }
                    if (totalBytes + length > _config.MaxDirBytes) { partial = true; continue; }
                    totalBytes += length;
                    files.Add(relative);
                }
                catch (IOException) { continue; }
            }

            // Feed the list to the shadow index. update-index rewrites it wholesale: --add every
            // listed path, --remove clears everything not listed (the list is the whole tree).
            var removeFirst = (await ShadowGit("ls-files", null, "--cached", "-z")).Stdout
                .Split('\0', StringSplitOptions.RemoveEmptyEntries).ToArray();
            var keep = new HashSet<string>(files, StringComparer.Ordinal);
            var removals = removeFirst.Where(p => !keep.Contains(p)).ToArray();
            var addInput = string.Join('\0', files) + (files.Count > 0 ? "\0" : "");
            var removeInput = string.Join('\0', removals) + (removals.Length > 0 ? "\0" : "");
            if (removals.Length > 0)
            {
                var r = await ShadowGit("update-index", Encoding.UTF8.GetBytes(removeInput),
                    "--force-remove", "-z", "--stdin");
                if (r.ExitCode != 0) return new SnapshotResult { Created = false, SkipReason = "update-index --force-remove failed: " + r.Stderr.Trim() };
            }
            var a = await ShadowGit("update-index", Encoding.UTF8.GetBytes(addInput),
                "--add", "-z", "--stdin");
            if (a.ExitCode != 0)
                return new SnapshotResult { Created = false, SkipReason = "update-index --add failed: " + a.Stderr.Trim() };

            var tree = (await ShadowGit("write-tree", null)).Stdout.Trim();
            var parentRef = (await ShadowGit("rev-parse", null, "--verify", "refs/pi/cp/HEAD")).ExitCode == 0
                ? (await ShadowGit("rev-parse", null, "refs/pi/cp/HEAD")).Stdout.Trim()
                : null;

            var message = $"checkpoint {_count + 1}: {files.Count} files";
            string[] commitArgs = parentRef is null ? [tree] : ["-p", parentRef, tree];
            var commit = (await ShadowGit(
                "commit-tree", Encoding.UTF8.GetBytes(message), commitArgs))
                .Stdout.Trim();

            _count += 1;
            var refName = "refs/pi/cp/" + _count;
            // No old-value for a brand-new ref: with a stale old-value, git tries to create a
            // reflog and fails to resolve a ref that does not exist yet ("cannot lock ref").
            var update = await ShadowGit("update-ref", null, refName, commit);
            if (update.ExitCode != 0)
                return new SnapshotResult { Created = false, SkipReason = "update-ref failed: " + update.Stderr.Trim() };
            var headUpdate = await ShadowGit("update-ref", null, "refs/pi/cp/HEAD", commit);
            if (headUpdate.ExitCode != 0)
                return new SnapshotResult { Created = false, SkipReason = "update-ref failed: " + headUpdate.Stderr.Trim() };

            // The snapshot writes only to the shadow index/refs, never the worktree files, so
            // the pre-snapshot worktree fingerprint is exactly what a next call will re-observe;
            // store it. (Storing the post-write state would differ in cp/HEAD and re-run.)
            _lastFingerprint = fingerprint;
            try { await MaintainRetentionAsync(ct).ConfigureAwait(false); }
            catch (Exception maintenanceError) { Trace.TraceWarning("Checkpoint retention failed: {0}", maintenanceError.Message); }
            return new SnapshotResult { Created = true, Commit = commit, Files = files.Count, Partial = partial, Head = realHead };
        }
        catch (OperationCanceledException)
        {
            return new SnapshotResult { Created = false, SkipReason = "snapshot timed out or was cancelled" };
        }
        catch (Exception ex)
        {
            return new SnapshotResult { Created = false, SkipReason = "snapshot failed: " + ex.Message };
        }
    }

    /// <summary>The shadow HEAD of the checkpoint chain (null when no snapshot exists yet).</summary>
    public string? LastCommit()
    {
        var result = ShadowGit("rev-parse", null, "refs/pi/cp/HEAD").GetAwaiter().GetResult();
        return result.ExitCode == 0 && result.Stdout.Trim().Length > 0 ? result.Stdout.Trim() : null;
    }

    // pi-sharp-checkpoints-3: the green marker. A successful run of a configured green command
    // (settings checkpoints.greenCommands) marks the latest checkpoint green. The marker is a ref
    // (refs/pi/cp/green/<n> -> checkpoint commit, plus refs/pi/cp/green/HEAD as the current one),
    // not a commit: the checkpoint chain stays untouched and a later prune (checkpoints-4) can
    // drop green refs like any other ref. Indices are 1-based, matching the checkpoint numbers.

    /// <summary>
    /// Marks <paramref name="shadowCommit"/> (one of this store's checkpoint commits) as green
    /// under <paramref name="index"/> (1-based checkpoint number) and makes it the current green.
    /// A later green call supersedes the earlier one at <c>green/HEAD</c>; the per-index ref stays.
    /// </summary>
    public Task MarkGreenAsync(string shadowCommit, int index, CancellationToken ct = default)
    {
        if (index < 1) throw new ArgumentOutOfRangeException(nameof(index), "green indices are 1-based");
        return UpdateRefAsync($"refs/pi/cp/green/{index}", shadowCommit, ct);
    }

    private async Task UpdateRefAsync(string @ref, string commit, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var result = await ShadowGit("update-ref", null, @ref, commit).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"git update-ref {@ref} failed: {result.Stderr.Trim()}");
        // The per-index ref is the record; green/HEAD is the pointer the rest of the system reads.
        var head = await ShadowGit("update-ref", null, "refs/pi/cp/green/HEAD", commit).ConfigureAwait(false);
        if (head.ExitCode != 0)
            throw new InvalidOperationException($"git update-ref green/HEAD failed: {head.Stderr.Trim()}");
    }

    /// <summary>The index (1-based) of the current green checkpoint, or null when none is marked.</summary>
    public int? LastGreenIndex()
    {
        // Resolve green/HEAD to a commit, then find which green/<n> ref points at it. The number of
        // green refs at or below that point is not the index; the ref name is. Two greens can share
        // a commit only when the tree never changed between them, in which case either name is a
        // faithful answer; take the largest.
        var head = ShadowGit("rev-parse", null, "refs/pi/cp/green/HEAD").GetAwaiter().GetResult();
        if (head.ExitCode != 0) return null;
        var target = head.Stdout.Trim();
        var list = ShadowGit("for-each-ref", null, "--format=%(refname)", "refs/pi/cp/green/").GetAwaiter().GetResult();
        if (list.ExitCode != 0) return null;
        var best = 0;
        foreach (var line in list.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var name = line.Trim();
            if (!name.StartsWith("refs/pi/cp/green/", StringComparison.Ordinal) || name.EndsWith("/HEAD", StringComparison.Ordinal)) continue;
            var num = name[("refs/pi/cp/green/".Length)..];
            if (!int.TryParse(num, out var index) || index < 1) continue;
            var commit = ShadowGit("rev-parse", null, name).GetAwaiter().GetResult();
            if (commit.ExitCode == 0 && commit.Stdout.Trim() == target && index > best) best = index;
        }
        return best > 0 ? best : null;
    }

    /// <summary>The shadow commit marked green under <paramref name="index"/>, or null when unmarked.</summary>
    public string? GreenCommitOf(int index)
    {
        var result = ShadowGit("rev-parse", null, $"refs/pi/cp/green/{index}").GetAwaiter().GetResult();
        return result.ExitCode == 0 && result.Stdout.Trim().Length > 0 ? result.Stdout.Trim() : null;
    }

    /// <summary>
    /// Retains the newest configured checkpoint indices plus green checkpoints from the last
    /// 24 hours. Runs Git's prune immediately when a week has elapsed, or sooner if the store
    /// exceeds <see cref="CheckpointConfig.MaxDirBytes"/>. The returned alert identifies a
    /// store that remains over its hard cap after the oldest refs were discarded.
    /// </summary>
    public async Task<CheckpointMaintenanceResult> MaintainRetentionAsync(CancellationToken ct = default)
    {
        // Every ref's commit time comes from this one call. A `git show -s --format=%ct` per ref diffed each checkpoint
        // against its parent with rename detection, -s or not: on host-02 (2026-10-29) one call took 10-27 s of CPU and
        // retention ran it for every ref after every snapshot, so a seat's turn waited minutes on each file edit.
        var refs = await ShadowGit("for-each-ref", null, "--format=%(refname) %(objectname) %(committerdate:unix)", "refs/pi/cp/").ConfigureAwait(false);
        if (refs.ExitCode != 0) throw new InvalidOperationException("Unable to enumerate checkpoint refs: " + refs.Stderr.Trim());

        var checkpoints = new List<(int Index, string Ref, string Commit, DateTimeOffset Time)>();
        var greens = new List<(int Index, string Ref, string Commit, DateTimeOffset Time)>();
        foreach (var line in refs.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            ct.ThrowIfCancellationRequested();
            var fields = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length != 3) continue;
            var prefix = "refs/pi/cp/";
            if (!fields[0].StartsWith(prefix, StringComparison.Ordinal)) continue;
            var name = fields[0][prefix.Length..];
            if (name == "HEAD" || name == "green/HEAD") continue;
            var isGreen = name.StartsWith("green/", StringComparison.Ordinal);
            var number = isGreen ? name["green/".Length..] : name;
            if (!int.TryParse(number, out var index) || index < 1) continue;
            if (!long.TryParse(fields[2], out var seconds)) continue;
            var entry = (index, fields[0], fields[1], DateTimeOffset.FromUnixTimeSeconds(seconds));
            if (isGreen) greens.Add(entry); else checkpoints.Add(entry);
        }

        var keep = new HashSet<string>(checkpoints.OrderByDescending(c => c.Index).Take(Math.Max(0, _config.Keep)).Select(c => c.Ref), StringComparer.Ordinal);
        var greenCutoff = DateTimeOffset.UtcNow.AddHours(-24);
        foreach (var green in greens.Where(g => g.Time >= greenCutoff)) keep.Add(green.Ref);
        var delete = checkpoints.Where(c => !keep.Contains(c.Ref)).Select(c => c.Ref)
            .Concat(greens.Where(g => !keep.Contains(g.Ref)).Select(g => g.Ref)).ToList();
        var removed = 0;
        foreach (var name in delete.OrderBy(n => n, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            var result = await ShadowGit("update-ref", null, "-d", name).ConfigureAwait(false);
            if (result.ExitCode == 0) removed++;
        }

        var currentGreen = greens.Where(g => keep.Contains(g.Ref)).OrderByDescending(g => g.Index).FirstOrDefault();
        var greenHead = await ShadowGit("rev-parse", null, "--verify", "refs/pi/cp/green/HEAD").ConfigureAwait(false);
        if (greenHead.ExitCode == 0 && !greens.Any(g => keep.Contains(g.Ref) && g.Commit == greenHead.Stdout.Trim()))
        {
            if (currentGreen.Ref is not null)
                await ShadowGit("update-ref", null, "refs/pi/cp/green/HEAD", currentGreen.Commit).ConfigureAwait(false);
            else
                await ShadowGit("update-ref", null, "-d", "refs/pi/cp/green/HEAD").ConfigureAwait(false);
        }

        var storeBytes = GetStoreBytes();
        var overCap = storeBytes > _config.MaxDirBytes;
        if (overCap)
        {
            // A hard cap is stronger than retention: discard oldest remaining checkpoint refs
            // first, including green refs only if necessary. Each removal is followed by GC.
            var oldestFirst = checkpoints.OrderBy(c => c.Time).Select(c => c.Ref)
                .Concat(greens.OrderBy(g => g.Time).Select(g => g.Ref));
            foreach (var name in oldestFirst)
            {
                if (storeBytes <= _config.MaxDirBytes) break;
                var result = await ShadowGit("update-ref", null, "-d", name).ConfigureAwait(false);
                if (result.ExitCode == 0) removed++;
                await ShadowGit("gc", null, "--prune=now").ConfigureAwait(false);
                storeBytes = GetStoreBytes();
            }
            var remainingGreens = await ShadowGit("for-each-ref", null, "--format=%(refname) %(objectname)", "refs/pi/cp/green/").ConfigureAwait(false);
            var remaining = remainingGreens.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .Where(fields => fields.Length == 2 && !fields[0].EndsWith("/HEAD", StringComparison.Ordinal))
                .ToArray();
            greenHead = await ShadowGit("rev-parse", null, "--verify", "refs/pi/cp/green/HEAD").ConfigureAwait(false);
            if (greenHead.ExitCode == 0 && !remaining.Any(fields => fields[1] == greenHead.Stdout.Trim()))
            {
                if (remaining.Length > 0)
                    await ShadowGit("update-ref", null, "refs/pi/cp/green/HEAD", remaining[^1][1]).ConfigureAwait(false);
                else
                    await ShadowGit("update-ref", null, "-d", "refs/pi/cp/green/HEAD").ConfigureAwait(false);
            }
        }

        var gcMarker = Path.Combine(GitDir, "pi-sharp-last-gc");
        var lastGc = File.Exists(gcMarker) && long.TryParse(await File.ReadAllTextAsync(gcMarker, ct).ConfigureAwait(false), out var ticks)
            ? new DateTimeOffset(ticks, TimeSpan.Zero) : DateTimeOffset.MinValue;
        var garbageCollected = overCap || DateTimeOffset.UtcNow - lastGc >= TimeSpan.FromDays(7);
        if (garbageCollected)
        {
            var gc = await ShadowGit("gc", null, "--prune=now").ConfigureAwait(false);
            if (gc.ExitCode == 0) await File.WriteAllTextAsync(gcMarker, DateTimeOffset.UtcNow.UtcTicks.ToString(), ct).ConfigureAwait(false);
            storeBytes = GetStoreBytes();
        }

        var alert = storeBytes > _config.MaxDirBytes
            ? $"checkpoint store remains over cap ({storeBytes} > {_config.MaxDirBytes} bytes) after pruning oldest checkpoints"
            : null;
        if (alert is not null) Trace.TraceWarning(alert);
        return new CheckpointMaintenanceResult { RemovedCheckpoints = removed, StoreBytes = storeBytes, GarbageCollected = garbageCollected, Alert = alert };
    }

    /// <summary>Current on-disk byte count for this workspace's shadow store.</summary>
    public long GetStoreBytes()
    {
        try
        {
            return Directory.EnumerateFiles(GitDir, "*", SearchOption.AllDirectories)
                .Sum(path => { try { return new FileInfo(path).Length; } catch (IOException) { return 0L; } });
        }
        catch (DirectoryNotFoundException) { return 0; }
    }

    /// <summary>The files recorded in <paramref name="commit"/> of the shadow repository.</summary>
    public IReadOnlyList<string> FilesOf(string commit)
    {
        var result = ShadowGit("ls-tree", null, "-r", "-z", commit).GetAwaiter().GetResult();
        return result.ExitCode == 0
            ? result.Stdout.Split('\0', StringSplitOptions.RemoveEmptyEntries).Select(l =>
            {
                var tab = l.IndexOf('\t');
                return tab >= 0 ? l[(tab + 1)..] : l;
            }).ToArray()
            : [];
    }

    private static readonly RewindPlan NotARepo = new() { Ok = false, Reason = "cwd is not a git repository", Changed = 0, Deleted = 0, Paths = [] };
    private static readonly RewindPlan UnknownCheckpoint = new() { Ok = false, Reason = "unknown checkpoint", Changed = 0, Deleted = 0, Paths = [] };
    private static readonly RewindOutcome HeadUnknown = new() { Applied = false, Reason = "head unknown", Touched = [] };

    /// <summary>
    /// What a restore of <paramref name="shadowCommit"/> would change in the work tree, without
    /// touching it. <c>Changed</c> counts the snapshot's files that differ from the work tree
    /// (or are missing from it); <c>Deleted</c> counts non-ignored work-tree files that are
    /// absent from the snapshot. Refusals (not a repository, unknown commit) never throw and
    /// come back as <see cref="RewindPlan.Reason"/>.
    /// </summary>
    public async Task<RewindPlan> PlanAsync(string shadowCommit, CancellationToken ct = default)
    {
        try
        {
            var root = await RealRootAsync();
            if (root is null) return NotARepo;

            var verify = await ShadowGit("rev-parse", null, "--verify", shadowCommit + "^{commit}");
            if (verify.ExitCode != 0) return UnknownCheckpoint;

            var plan = await ComputePlanAsync(root, shadowCommit, ct);
            if (plan is null) return new RewindPlan { Ok = false, Reason = "plan failed: could not read the checkpoint tree", Paths = [] };
            return new RewindPlan
            {
                Ok = true,
                Changed = plan.Changed,
                Deleted = plan.Deleted,
                Paths = plan.WritePaths.Take(40).ToArray(),
            };
        }
        catch (OperationCanceledException) { return new RewindPlan { Ok = false, Reason = "plan cancelled", Paths = [] }; }
        catch (Exception ex) { return new RewindPlan { Ok = false, Reason = "plan failed: " + ex.Message, Paths = [] }; }
    }

    /// <summary>
    /// Restore the work tree to <paramref name="shadowCommit"/>: write every file that differs
    /// (or is missing) and delete non-ignored files absent from the snapshot. The guard runs
    /// before any write: <c>expectedHead</c> must be the current real HEAD ("head unknown" /
    /// "head moved: \u003cold\u003e..\u003cnew\u003e", no writes on refusal). Isolation: the tree comes
    /// from a throwaway index under the shadow dir (<c>read-tree</c> + <c>checkout-index -a -f</c>); the
    /// seat repository is never checked out, reset, committed or re-reffed, and its index file
    /// is never opened. A no-op restore is a success with an empty <see cref="RewindOutcome.Touched"/>.
    /// </summary>
    public async Task<RewindOutcome> RestoreAsync(string shadowCommit, string expectedHead, CancellationToken ct = default)
    {
        try
        {
            var root = await RealRootAsync();
            if (root is null) return new RewindOutcome { Applied = false, Reason = "cwd is not a git repository", Touched = [] };

            if (string.IsNullOrWhiteSpace(expectedHead)) return HeadUnknown;
            var currentHead = (await RealGit("rev-parse", "HEAD")).Stdout.Trim();
            if (!string.Equals(currentHead, expectedHead.Trim(), StringComparison.Ordinal))
                return new RewindOutcome { Applied = false, Reason = "head moved: " + expectedHead + ".." + currentHead, Touched = [] };

            var verify = await ShadowGit("rev-parse", null, "--verify", shadowCommit + "^{commit}");
            if (verify.ExitCode != 0) return new RewindOutcome { Applied = false, Reason = "unknown checkpoint", Touched = [] };

            var plan = await ComputePlanAsync(root, shadowCommit, ct);
            if (plan is null) return new RewindOutcome { Applied = false, Reason = "restore failed: could not read the checkpoint tree", Touched = [] };
            if (plan.Changed == 0 && plan.Deleted == 0) return new RewindOutcome { Applied = true, Touched = [] };

            // Temp index: the snapshot's tree, restored over the work tree. The real index is
            // never opened (GIT_INDEX_FILE points at the shadow dir), so its mtime is untouched.
            var tempIndex = Path.Combine(GitDir, "restore-" + Guid.NewGuid().ToString("N") + ".idx");
            try
            {
                var tempRead = await ShadowGitWithIndex(tempIndex, "read-tree", null, shadowCommit);
                if (tempRead.ExitCode != 0)
                    return new RewindOutcome { Applied = false, Reason = "restore failed: read-tree: " + tempRead.Stderr.Trim(), Touched = [] };

                var checkout = await ShadowGitWithIndex(tempIndex, "checkout-index", null, "-a", "-f");
                if (checkout.ExitCode != 0)
                    return new RewindOutcome { Applied = false, Reason = "restore failed: checkout-index: " + checkout.Stderr.Trim(), Touched = [] };

                var touched = new List<string>(plan.WritePaths.Count);
                foreach (var p in plan.WritePaths) touched.Add(p);

                foreach (var p in plan.DeletePaths)
                {
                    var full = Path.Combine(root, p);
                    try { File.Delete(full); } catch (IOException) { continue; } // vanished between plan and delete
                    touched.Add(p);
                }

                return new RewindOutcome { Applied = true, Touched = touched };
            }
            finally
            {
                try { if (File.Exists(tempIndex)) File.Delete(tempIndex); } catch (IOException) { }
            }
        }
        catch (OperationCanceledException) { return new RewindOutcome { Applied = false, Reason = "restore cancelled", Touched = [] }; }
        catch (Exception ex) { return new RewindOutcome { Applied = false, Reason = "restore failed: " + ex.Message, Touched = [] }; }
    }

    private sealed record PlanCounts(int Changed, int Deleted, IReadOnlyList<string> WritePaths, IReadOnlyList<string> DeletePaths);

    /// <summary>
    /// Compute the changed/deleted sets for <paramref name="shadowCommit"/> against the work
    /// tree, without touching either. The comparison is byte-exact where possible: a snapshot
    /// blob is compared to the work-tree file by hash (the store keeps no per-file content),
    /// so a temp index <c>read-tree</c> + <c>checkout-index</c> on a scratch copy is overkill;
    /// instead each path is compared by the shadow repository's own <c>hash-object</c> of the
    /// work-tree file against the blob sha in the snapshot. Paths with a different blob (or
    /// missing in the work tree) are written; non-ignored work-tree paths missing from the
    /// snapshot are deleted.
    /// </summary>
    private async Task<PlanCounts?> ComputePlanAsync(string root, string shadowCommit, CancellationToken ct)
    {
        // Snapshot side: the tree entries (mode, type, sha, path) of the commit.
        var tree = (await ShadowGit("ls-tree", null, "-r", "-z", shadowCommit)).Stdout;
        if (tree.Length == 0)
        {
            // An empty commit is not an error (an empty snapshot restores to an empty tree),
            // but ls-tree exits non-zero on a missing commit; distinguish the two.
            var verify = await ShadowGit("rev-parse", null, "--verify", shadowCommit + "^{commit}");
            if (verify.ExitCode != 0) return null;
        }
        var snapshot = new Dictionary<string, string>(StringComparer.Ordinal); // path -> blob sha
        foreach (var line in tree.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var tab = line.IndexOf('\t');
            if (tab < 0) continue;
            var meta = line[..tab].Split(' ');
            if (meta.Length < 3) continue;
            var type = meta[1];
            var sha = meta[2];
            var path = line[(tab + 1)..].Replace('\\', '/');
            if (type != "blob") continue; // submodules (commit) are not restored file-by-file
            snapshot[path] = sha;
        }

        // Work-tree side: the same enumeration the snapshot uses (the shadow index covers the
        // tree after the first snapshot; --cached --others is the union either way).
        var listed = (await ShadowGit("ls-files", null, "--cached", "--others", "--exclude-standard", "-z")).Stdout
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !IsShadowPath(p))
            .ToArray();
        var workTree = new HashSet<string>(listed, StringComparer.Ordinal);

        var write = new List<string>(snapshot.Count);
        foreach (var (path, sha) in snapshot)
        {
            ct.ThrowIfCancellationRequested();
            if (!workTree.Contains(path)) { write.Add(path); continue; }
            var file = new FileInfo(Path.Combine(root, path));
            if (!file.Exists) { write.Add(path); continue; } // vanished between enumeration and hash
            var hash = await ShadowGit("hash-object", Encoding.UTF8.GetBytes(path + "\0"), "--stdin-paths");
            if (hash.ExitCode != 0 || !string.Equals(hash.Stdout.Trim(), sha, StringComparison.OrdinalIgnoreCase))
                write.Add(path);
        }

        var delete = new List<string>();
        foreach (var path in listed)
        {
            ct.ThrowIfCancellationRequested();
            if (snapshot.ContainsKey(path)) continue;
            delete.Add(path);
        }

        return new PlanCounts(write.Count, delete.Count, write, delete);
    }

    /// <summary>Git inside the shadow repository with an explicit index file (temp indexes).</summary>
    private Task<GitResult> ShadowGitWithIndex(string indexFile, string command, byte[]? stdinData, params string[] args)
    {
        var env = new Dictionary<string, string?>
        {
            ["GIT_INDEX_FILE"] = indexFile,
            ["GIT_AUTHOR_NAME"] = "pi-sharp",
            ["GIT_AUTHOR_EMAIL"] = "pi-sharp@localhost",
            ["GIT_COMMITTER_NAME"] = "pi-sharp",
            ["GIT_COMMITTER_EMAIL"] = "pi-sharp@localhost",
        };
        var prefix = new[] { "--git-dir", GitDir, "--work-tree", _cwd };
        var all = new string[prefix.Length + 1 + args.Length];
        Array.Copy(prefix, all, prefix.Length);
        all[prefix.Length] = command;
        Array.Copy(args, 0, all, prefix.Length + 1, args.Length);
        return Git(_cwd, env, stdinData, all);
    }
}
