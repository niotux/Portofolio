using System.Diagnostics;
using System.Text.Json.Nodes;
using PiSharp.Core.Checkpoints;
using PiSharp.Core.Session;
using PiSharp.Tests.Tools;

namespace PiSharp.Tests.Checkpoints;

public class CheckpointStoreTests : IDisposable
{
    private readonly TempWorkspace _tmp;
    private readonly string _repo;

    public CheckpointStoreTests()
    {
        _tmp = new TempWorkspace();
        _repo = _tmp.Path;
        GitInit(_repo);
        // A baseline commit so rev-parse HEAD works.
        File.WriteAllText(Path.Combine(_repo, "readme.md"), "start\n");
        Git(_repo, "add", "-A");
        Git(_repo, "-c", "user.name=t", "-c", "user.email=t@t", "commit", "-q", "-m", "base");
    }

    public void Dispose() => _tmp.Dispose();

    private static void GitInit(string dir) => Git(dir, "init", "-q");

    private static string Git(string workDir, params string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var outp = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0) throw new Exception($"git {string.Join(' ', args)} failed: {p.StandardError.ReadToEnd()}");
        return outp;
    }

    private CheckpointStore Store(int timeoutSeconds = 10, long maxDirBytes = 1024L * 1024 * 1024, long maxFileBytes = 1024L * 1024 * 1024, int maxFiles = 20000, int keep = 50)
    {
        var checkpointsDir = Path.Combine(_tmp.Path, "checkpoints");
        return new CheckpointStore(_repo, checkpointsDir, new CheckpointConfig
        {
            TimeoutSeconds = timeoutSeconds,
            MaxDirBytes = maxDirBytes,
            MaxFileBytes = maxFileBytes,
            MaxFiles = maxFiles,
            Keep = keep,
        });
    }

    private string RealStatus() => Git(_repo, "status", "--porcelain");
    private string RealStash() => Git(_repo, "stash", "list");
    private string RealObjects() => Git(_repo, "count-objects");

    [Fact]
    public async Task Snapshot_CreatesShadowRefAndLeavesRealRepoUntouched()
    {
        var store = Store();
        File.WriteAllText(Path.Combine(_repo, "a.txt"), "alpha\n");
        var before = (RealStatus(), RealStash(), RealObjects(),
            Directory.EnumerateFiles(Path.Combine(_repo, ".git"), "index", SearchOption.AllDirectories).First());

        var result = await store.CreateSnapshotAsync();

        Assert.True(result.Created, "created; skip: " + result.SkipReason);
        Assert.NotNull(result.Commit);
        Assert.False(result.Partial);
        Assert.Equal(result.Commit, store.LastCommit());
        // The shadow ref chain exists in the shadow dir, not in the real repo.
        var refs = Git(_repo, "--git-dir", store.GitDir, "for-each-ref", "--format=%(refname)", "refs/pi/cp/");
        Assert.Contains("refs/pi/cp/1", refs);
        Assert.DoesNotContain("refs/pi", Git(_repo, "for-each-ref"));
        // Real git state is unchanged: status (untracked a.txt), stash, object count, index file.
        Assert.Equal(before.Item1, RealStatus());
        Assert.Equal(before.Item2, RealStash());
        Assert.Equal(before.Item3, RealObjects());
        Assert.True(File.Exists(before.Item4));
        // The shadow index exists inside the shadow dir.
        Assert.True(File.Exists(Path.Combine(store.GitDir, "index")));
        // a.txt was stored, readme.md was stored.
        Assert.Contains("a.txt", store.FilesOf(result.Commit!));
        Assert.Contains("readme.md", store.FilesOf(result.Commit!));
    }

    [Fact]
    public async Task NestedCloneRootsResolveIndependentlyAcrossFiveHundredMutatingBatches()
    {
        var cloneParent = Path.Combine(_tmp.Path, "work");
        var outer = Path.Combine(cloneParent, "outer-clone");
        var nested = Path.Combine(cloneParent, "nested-clone");
        Directory.CreateDirectory(cloneParent);
        Directory.CreateDirectory(outer);
        GitInit(outer);
        Directory.CreateDirectory(nested);
        GitInit(nested);
        foreach (var root in new[] { outer, nested })
        {
            File.WriteAllText(Path.Combine(root, "base.txt"), "base\n");
            Git(root, "add", "-A");
            Git(root, "-c", "user.name=t", "-c", "user.email=t@t", "commit", "-q", "-m", "base");
        }

        Assert.Equal(Path.GetFullPath(outer), CheckpointStore.ResolveWorkTreeRoot(Path.Combine(outer, "base.txt")));
        Assert.Equal(Path.GetFullPath(nested), CheckpointStore.ResolveWorkTreeRoot(nested));
        var checkpointDir = Path.Combine(_tmp.Path, "shadow-checkpoints");
        var stores = new[] { new CheckpointStore(outer, checkpointDir), new CheckpointStore(nested, checkpointDir) };
        Assert.NotEqual(stores[0].GitDir, stores[1].GitDir);
        SnapshotResult? lastOuter = null;
        SnapshotResult? lastNested = null;
        for (var batch = 0; batch < 500; batch++)
        {
            var index = batch % 2;
            var root = index == 0 ? outer : nested;
            File.WriteAllText(Path.Combine(root, "mutated.txt"), $"batch-{batch}\n");
            var snapshot = await stores[index].CreateSnapshotAsync(CancellationToken.None);
            Assert.True(snapshot.Created, snapshot.SkipReason);
            if (index == 0) lastOuter = snapshot; else lastNested = snapshot;
        }

        Assert.NotNull(lastOuter?.Commit);
        Assert.NotNull(lastNested?.Commit);
        Assert.Contains("mutated.txt", stores[0].FilesOf(lastOuter!.Commit!));
        Assert.Contains("mutated.txt", stores[1].FilesOf(lastNested!.Commit!));
        Assert.NotEqual(stores[0].GitDir, stores[1].GitDir);
    }

    [Fact]
    public async Task Snapshot_ContinuesCheckpointIndicesAcrossStoreInstances()
    {
        var firstStore = Store();
        File.WriteAllText(Path.Combine(_repo, "a.txt"), "first\n");
        var first = await firstStore.CreateSnapshotAsync();
        Assert.True(first.Created, first.SkipReason);

        var resumedStore = Store();
        File.WriteAllText(Path.Combine(_repo, "a.txt"), "second\n");
        var second = await resumedStore.CreateSnapshotAsync();
        Assert.True(second.Created, second.SkipReason);

        Assert.Equal(first.Commit, Git(_repo, "--git-dir", resumedStore.GitDir, "rev-parse", "refs/pi/cp/1").Trim());
        Assert.Equal(second.Commit, Git(_repo, "--git-dir", resumedStore.GitDir, "rev-parse", "refs/pi/cp/2").Trim());
    }

    [Fact]
    public async Task Snapshot_SkipsIgnoredBinDir()
    {
        var store = Store();
        File.WriteAllText(Path.Combine(_repo, ".gitignore"), "bin/\n");
        File.WriteAllText(Path.Combine(_repo, "src.txt"), "code\n");
        Directory.CreateDirectory(Path.Combine(_repo, "bin"));
        File.WriteAllText(Path.Combine(_repo, "bin", "junk.dll"), "zz\n");
        var result = await store.CreateSnapshotAsync();
        Assert.True(result.Created);
        Assert.DoesNotContain("bin/junk.dll", store.FilesOf(result.Commit!));
        Assert.Contains("src.txt", store.FilesOf(result.Commit!));
    }

    [Fact]
    public async Task Snapshot_SecondIsChainOfFirst()
    {
        var store = Store();
        File.WriteAllText(Path.Combine(_repo, "a.txt"), "alpha\n");
        var first = await store.CreateSnapshotAsync();
        Assert.True(first.Created, "first; skip: " + first.SkipReason);
        File.WriteAllText(Path.Combine(_repo, "b.txt"), "beta\n");
        var second = await store.CreateSnapshotAsync();
        Assert.True(second.Created, "second; skip: " + second.SkipReason);
        Assert.NotEqual(first.Commit, second.Commit);
        // Second commit is parented on the first: git log in the shadow repo shows two commits.
        var log = Git(_repo, "--git-dir", store.GitDir, "--work-tree", _repo, "log", "--oneline", "refs/pi/cp/2");
        Assert.Equal(2, log.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Equal(second.Commit!, store.LastCommit());
    }

    [Fact]
    public async Task Snapshot_SkipsWhenFingerprintUnchanged()
    {
        var store = Store();
        File.WriteAllText(Path.Combine(_repo, "a.txt"), "alpha\n");
        var first = await store.CreateSnapshotAsync();
        Assert.True(first.Created, "first; skip: " + first.SkipReason);
        // No change to the work tree: the fingerprint matches, the snapshot is skipped.
        var second = await store.CreateSnapshotAsync();
        Assert.False(second.Created);
        Assert.Equal("unchanged since last snapshot", second.SkipReason);
        Assert.Equal(first.Commit, store.LastCommit());
        // A change makes the next snapshot run again.
        File.AppendAllText(Path.Combine(_repo, "a.txt"), "more\n");
        var third = await store.CreateSnapshotAsync();
        Assert.True(third.Created, "third; skip: " + third.SkipReason);
    }

    [Fact]
    public async Task Snapshot_PartialWhenOverByteBudget()
    {
        // Byte budget 1500; a single 2 KB file cannot fit, so the snapshot is partial and
        // the 1 KB file is kept. (The ctor's readme.md is committed/tracked and so is not
        // part of the untracked enumeration the snapshot feeds, keeping the budget exact.)
        var store = Store(maxDirBytes: 1500);
        File.WriteAllText(Path.Combine(_repo, "ok.bin"), new string('x', 1024));
        File.WriteAllText(Path.Combine(_repo, "big.bin"), new string('y', 2 * 1024));
        var result = await store.CreateSnapshotAsync();
        Assert.True(result.Created, "created; skip: " + result.SkipReason);
        Assert.True(result.Partial);
        Assert.Contains("ok.bin", store.FilesOf(result.Commit!));
        Assert.DoesNotContain("big.bin", store.FilesOf(result.Commit!));
    }

    [Fact]
    public async Task Snapshot_SkipsSingleFileOverSizeCap()
    {
        var store = Store(maxFileBytes: 1024, maxDirBytes: 1024L * 1024);
        File.WriteAllText(Path.Combine(_repo, "ok.txt"), "small\n");
        File.WriteAllText(Path.Combine(_repo, "huge.bin"), new string('z', 2 * 1024 * 1024));
        var result = await store.CreateSnapshotAsync();
        Assert.True(result.Created);
        Assert.Contains("ok.txt", store.FilesOf(result.Commit!));
        Assert.DoesNotContain("huge.bin", store.FilesOf(result.Commit!));
    }

    [Fact]
    public async Task Snapshot_SkipsSymlinks()
    {
        var store = Store();
        File.WriteAllText(Path.Combine(_repo, "target.txt"), "t\n");
        var link = Path.Combine(_repo, "link.txt");
        File.CreateSymbolicLink(link, Path.Combine(_repo, "target.txt"));
        var result = await store.CreateSnapshotAsync();
        Assert.True(result.Created);
        Assert.Contains("target.txt", store.FilesOf(result.Commit!));
        Assert.DoesNotContain("link.txt", store.FilesOf(result.Commit!));
    }

    [Fact]
    public async Task Snapshot_SkipsWhenMergeInProgress()
    {
        var store = Store();
        // Simulate a merge in the real repo with a MERGE_HEAD file. rev-parse --git-dir
        // is relative to the repo (".git"), so resolve it against _repo, not the process CWD.
        var realGitDir = Path.Combine(_repo, Git(_repo, "rev-parse", "--git-dir").Trim());
        File.WriteAllText(Path.Combine(realGitDir, "MERGE_HEAD"), "deadbeef\n");
        try
        {
            File.WriteAllText(Path.Combine(_repo, "a.txt"), "alpha\n");
            var result = await store.CreateSnapshotAsync();
            Assert.False(result.Created);
            Assert.Equal("merge or rebase in progress", result.SkipReason);
        }
        finally
        {
            File.Delete(Path.Combine(realGitDir, "MERGE_HEAD"));
        }
    }

    [Fact]
    public void HashOf_IsStableForCwd()
    {
        var a = CheckpointStore.HashOf("/home/x/work/repo");
        var b = CheckpointStore.HashOf("/home/x/work/repo");
        Assert.Equal(a, b);
        Assert.NotEqual(a, CheckpointStore.HashOf("/home/x/work/other"));
    }

    // ---- pi-sharp-checkpoints-3: green markers ----

    [Fact]
    public async Task Green_MarkThenResolve()
    {
        var store = Store();
        Assert.Null(store.LastGreenIndex());
        File.WriteAllText(Path.Combine(_repo, "a.txt"), "alpha\n");
        var first = await store.CreateSnapshotAsync();
        Assert.True(first.Created, "first; skip: " + first.SkipReason);

        await store.MarkGreenAsync(first.Commit!, 1);
        Assert.Equal(1, store.LastGreenIndex());
        Assert.Equal(first.Commit, store.GreenCommitOf(1));
    }

    [Fact]
    public async Task Green_SecondSupersedesFirst()
    {
        var store = Store();
        File.WriteAllText(Path.Combine(_repo, "a.txt"), "alpha\n");
        var first = await store.CreateSnapshotAsync();
        Assert.True(first.Created);
        File.WriteAllText(Path.Combine(_repo, "b.txt"), "beta\n");
        var second = await store.CreateSnapshotAsync();
        Assert.True(second.Created);

        await store.MarkGreenAsync(first.Commit!, 1);
        Assert.Equal(1, store.LastGreenIndex());
        await store.MarkGreenAsync(second.Commit!, 2);
        Assert.Equal(2, store.LastGreenIndex());
        // The per-index refs both survive; only green/HEAD moves.
        Assert.Equal(first.Commit, store.GreenCommitOf(1));
        Assert.Equal(second.Commit, store.GreenCommitOf(2));
    }

    [Fact]
    public async Task Green_UnmarkedIndexIsNull()
    {
        var store = Store();
        File.WriteAllText(Path.Combine(_repo, "a.txt"), "alpha\n");
        var first = await store.CreateSnapshotAsync();
        Assert.True(first.Created);
        await store.MarkGreenAsync(first.Commit!, 1);
        Assert.Null(store.GreenCommitOf(2));
    }

    [Fact]
    public async Task Retention_500SyntheticBatchesStayWithinConfiguredKeepAndStoreCap()
    {
        const long oneGiB = 1024L * 1024 * 1024;
        var store = Store(maxDirBytes: oneGiB, keep: 50);
        var path = Path.Combine(_repo, "batch.txt");
        for (var batch = 0; batch < 500; batch++)
        {
            File.WriteAllText(path, $"synthetic batch {batch}\n");
            var snapshot = await store.CreateSnapshotAsync();
            Assert.True(snapshot.Created, $"batch {batch}: {snapshot.SkipReason}");
        }

        var refs = Git(_repo, "--git-dir", store.GitDir, "for-each-ref", "--format=%(refname)", "refs/pi/cp/")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(name => int.TryParse(name.AsSpan("refs/pi/cp/".Length), out _))
            .ToArray();
        Assert.Equal(50, refs.Length);
        Assert.InRange(store.GetStoreBytes(), 1, oneGiB);
    }

    [Fact]
    public async Task Retention_PreservesRecentGreenOutsideOrdinaryKeepWindow()
    {
        var store = Store(keep: 1);
        File.WriteAllText(Path.Combine(_repo, "a.txt"), "one\n");
        var first = await store.CreateSnapshotAsync();
        Assert.True(first.Created);
        await store.MarkGreenAsync(first.Commit!, 1);
        for (var index = 2; index <= 3; index++)
        {
            File.WriteAllText(Path.Combine(_repo, "a.txt"), $"{index}\n");
            var next = await store.CreateSnapshotAsync();
            Assert.True(next.Created);
        }

        Assert.Equal(1, store.LastGreenIndex());
        Assert.Equal(first.Commit, store.GreenCommitOf(1));
        var retained = Git(_repo, "--git-dir", store.GitDir, "for-each-ref", "--format=%(refname)", "refs/pi/cp/");
        Assert.Contains("refs/pi/cp/green/1", retained);
        Assert.Contains("refs/pi/cp/3", retained);
        Assert.DoesNotContain("refs/pi/cp/2\n", retained);
    }

    // host-02, 2026-10-29: a `git show` per ref diffed every checkpoint with rename detection, 10-27 s of CPU each,
    // and a seat's turn waited on all of them after every edit. Retention reads the times in one for-each-ref.
    [Fact]
    public async Task Retention_ReadsEveryCheckpointTimeInOneCall_AndDiffsNothing()
    {
        var store = Store(keep: 2);
        for (var index = 1; index <= 3; index++)
        {
            File.WriteAllText(Path.Combine(_repo, "a.txt"), $"{index}\n");
            Assert.True((await store.CreateSnapshotAsync()).Created);
        }
        await store.MarkGreenAsync(store.LastCommit()!, 3);

        var trace = Path.Combine(_tmp.Path, "git-trace.log");
        var previous = Environment.GetEnvironmentVariable("GIT_TRACE");
        Environment.SetEnvironmentVariable("GIT_TRACE", trace);
        try
        {
            File.WriteAllText(Path.Combine(_repo, "a.txt"), "4\n");
            Assert.True((await store.CreateSnapshotAsync()).Created);
        }
        finally { Environment.SetEnvironmentVariable("GIT_TRACE", previous); }

        // The trace carries every git call of the test process (other tests run in parallel) and never the --git-dir,
        // so it is read for the calls only retention makes.
        var calls = File.ReadAllLines(trace).Where(l => l.Contains("trace: built-in: git")).ToArray();
        Assert.DoesNotContain(calls, l => l.Contains("show -s") && l.Contains("%ct"));
        Assert.Contains(calls, l => l.Contains("for-each-ref") && l.Contains("committerdate:unix"));
        var retained = Git(_repo, "--git-dir", store.GitDir, "for-each-ref", "--format=%(refname)", "refs/pi/cp/");
        Assert.Contains("refs/pi/cp/green/3", retained);
        Assert.Contains("refs/pi/cp/4\n", retained);
        Assert.DoesNotContain("refs/pi/cp/2\n", retained);
    }
    [Fact]
    public async Task Snapshot_StoresTheFilesOwnBytes_NoCleanFilterRuns()
    {
        // strata, 2026-10-22: git-lfs's clean filter ran inside the shadow and copied 1.4 GB of assets into every store.
        File.WriteAllText(Path.Combine(_repo, ".gitattributes"), "*.bin filter=fake\n");
        var store = Store();
        File.AppendAllText(Path.Combine(store.GitDir, "config"), "[filter \"fake\"]\n\tclean = sed s/a/X/\n\trequired = true\n");
        File.WriteAllText(Path.Combine(_repo, "asset.bin"), "abc\n");

        var result = await store.CreateSnapshotAsync();

        Assert.True(result.Created, "created; skip: " + result.SkipReason);
        Assert.Equal("abc\n", Git(_repo, "--git-dir", store.GitDir, "cat-file", "-p", result.Commit + ":asset.bin"));
        Assert.False(Directory.Exists(Path.Combine(store.GitDir, "lfs")));
    }

    [Fact]
    public async Task AStoreWithAnLfsCopyStartsOver()
    {
        var store = Store();
        File.WriteAllText(Path.Combine(_repo, "a.txt"), "alpha\n");
        Assert.True((await store.CreateSnapshotAsync()).Created);
        Directory.CreateDirectory(Path.Combine(store.GitDir, "lfs", "objects", "ab"));
        File.WriteAllText(Path.Combine(store.GitDir, "lfs", "objects", "ab", "blob"), "copied asset");

        var reopened = Store();

        Assert.False(Directory.Exists(Path.Combine(reopened.GitDir, "lfs")));
        Assert.Null(reopened.LastCommit());
        Assert.True((await reopened.CreateSnapshotAsync()).Created);
    }

    [Fact]
    public async Task AStoreWhoseWorkTreeIsGoneIsPruned()
    {
        var gone = Path.Combine(_tmp.Path, "gone-checkout");
        Directory.CreateDirectory(gone);
        GitInit(gone);
        File.WriteAllText(Path.Combine(gone, "x.txt"), "x\n");
        Git(gone, "add", "-A");
        Git(gone, "-c", "user.name=t", "-c", "user.email=t@t", "commit", "-q", "-m", "base");
        var checkpointsDir = Path.Combine(_tmp.Path, "checkpoints");
        var other = new CheckpointStore(gone, checkpointsDir);
        Assert.True((await other.CreateSnapshotAsync()).Created);
        Directory.Delete(gone, recursive: true);

        var store = Store();

        Assert.False(Directory.Exists(other.GitDir));
        Assert.True(Directory.Exists(store.GitDir));
    }

    [Fact]
    public async Task OpeningAnyStoreDropsEveryOtherStoreThatCarriesAnLfsCopy()
    {
        var live = Path.Combine(_tmp.Path, "live-checkout");
        Directory.CreateDirectory(live);
        GitInit(live);
        File.WriteAllText(Path.Combine(live, "x.txt"), "x\n");
        Git(live, "add", "-A");
        Git(live, "-c", "user.name=t", "-c", "user.email=t@t", "commit", "-q", "-m", "base");
        var checkpointsDir = Path.Combine(_tmp.Path, "checkpoints");
        var inflated = new CheckpointStore(live, checkpointsDir);
        Assert.True((await inflated.CreateSnapshotAsync()).Created);
        Directory.CreateDirectory(Path.Combine(inflated.GitDir, "lfs", "objects"));

        Store();

        Assert.False(Directory.Exists(inflated.GitDir));
    }
}

/// <summary>
/// Plan/Restore tests (ADR pi-sharp-checkpoint-store-api v2 contract):
/// <see cref="CheckpointStore.PlanAsync"/> / <see cref="CheckpointStore.RestoreAsync"/> against a
/// shadow snapshot, exact refusal strings, and the no-op / applied / guarded-restore semantics.
/// </summary>
public class CheckpointStorePlanRestoreTests : IDisposable
{
    private readonly TempWorkspace _tmp;
    private readonly string _repo;

    public CheckpointStorePlanRestoreTests()
    {
        _tmp = new TempWorkspace();
        _repo = _tmp.Path;
        GitInit(_repo);
        // Baseline: two tracked files so a later snapshot has a stable baseline to plan/restore against.
        File.WriteAllText(Path.Combine(_repo, "readme.md"), "start\n");
        File.WriteAllText(Path.Combine(_repo, "keep.md"), "keep\n");
        Git(_repo, "add", "-A");
        Git(_repo, "-c", "user.name=t", "-c", "user.email=t@t", "commit", "-q", "-m", "base");
    }

    public void Dispose() => _tmp.Dispose();

    private static void GitInit(string dir) => Git(dir, "init", "-q");

    private static string Git(string workDir, params string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var outp = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0) throw new Exception($"git {string.Join(' ', args)} failed: {p.StandardError.ReadToEnd()}");
        return outp;
    }

    private static CheckpointStore Store(string cwd)
    {
        var dir = Path.Combine(Path.GetFullPath(cwd), "checkpoints");
        return new CheckpointStore(cwd, dir, new CheckpointConfig());
    }

    private string RealIndexMtime(string repo)
    {
        var gitDir = Path.Combine(repo, Git(repo, "rev-parse", "--git-dir").Trim());
        return File.GetLastWriteTimeUtc(Path.Combine(gitDir, "index")).Ticks.ToString();
    }

    [Fact]
    public async Task Plan_UnknownCheckpointIsRefused()
    {
        var store = Store(_repo);
        var plan = await store.PlanAsync("0000000000000000000000000000000000000000");
        Assert.False(plan.Ok);
        Assert.Equal("unknown checkpoint", plan.Reason);
        Assert.Equal(0, plan.Changed);
        Assert.Equal(0, plan.Deleted);
        Assert.Empty(plan.Paths);
    }

    [Fact]
    public async Task Plan_NotARepoIsRefused()
    {
        // A plain directory with no git repository anywhere above it (use a fresh root path).
        var isolated = new TempWorkspace();
        try
        {
            var plain = isolated.Path;
            Directory.CreateDirectory(plain);
            var store = new CheckpointStore(plain, Path.Combine(plain, "checkpoints"), new CheckpointConfig());
            var plan = await store.PlanAsync("anything");
            Assert.False(plan.Ok);
            Assert.Equal("cwd is not a git repository", plan.Reason);
        }
        finally { isolated.Dispose(); }
    }

    [Fact]
    public async Task Plan_ReportsChangedAndDeletedCountsAndPaths()
    {
        var store = Store(_repo);
        // Baseline snapshot of readme.md + keep.md.
        var snap = await store.CreateSnapshotAsync();
        Assert.True(snap.Created, "snap; " + snap.SkipReason);
        var commit = snap.Commit!;

        // Now change the work tree: rewrite keep.md, add a new file, and remove nothing yet.
        File.WriteAllText(Path.Combine(_repo, "keep.md"), "changed\n");
        File.WriteAllText(Path.Combine(_repo, "new.txt"), "n\n");
        // readme.md deleted from the work tree (it was in the snapshot).
        File.Delete(Path.Combine(_repo, "readme.md"));

        var plan = await store.PlanAsync(commit);
        Assert.True(plan.Ok, plan.Reason);
        // Changed = snapshot paths that will be written: keep.md (differs) + readme.md (in
        // snapshot but deleted from the work tree => restored). Deleted = work-tree paths
        // absent from the snapshot: new.txt.
        Assert.Equal(2, plan.Changed);
        Assert.Equal(1, plan.Deleted);
        // Paths is the write subset (uncapped up to 40): keep.md + readme.md. new.txt is a
        // deletion and is not in the write list.
        Assert.Contains("keep.md", plan.Paths);
        Assert.Contains("readme.md", plan.Paths);
        Assert.DoesNotContain("new.txt", plan.Paths);
        Assert.Equal(2, plan.Paths.Count);
    }

    [Fact]
    public async Task Restore_NoOpWhenTreeMatchesIsSuccessWithEmptyTouched()
    {
        var store = Store(_repo);
        var snap = await store.CreateSnapshotAsync();
        var commit = snap.Commit!;
        // Work tree already matches the snapshot exactly.
        var outcome = await store.RestoreAsync(commit, snap.Head!);
        Assert.True(outcome.Applied, outcome.Reason);
        Assert.Null(outcome.Reason);
        Assert.Empty(outcome.Touched);
    }

    [Fact]
    public async Task Restore_UnknownHeadRefusesBeforeAnyWrite()
    {
        var store = Store(_repo);
        var snap = await store.CreateSnapshotAsync();
        var commit = snap.Commit!;
        File.WriteAllText(Path.Combine(_repo, "mut.txt"), "m\n");
        var outcome = await store.RestoreAsync(commit, ""); // blank expectedHead
        Assert.False(outcome.Applied);
        Assert.Equal("head unknown", outcome.Reason);
        Assert.Empty(outcome.Touched);
        // No writes: the new file is still there with its original content.
        Assert.Equal("m\n", File.ReadAllText(Path.Combine(_repo, "mut.txt")));
    }

    [Fact]
    public async Task Restore_HeadMovedRefusesBeforeAnyWrite()
    {
        var store = Store(_repo);
        var snap = await store.CreateSnapshotAsync();
        var commit = snap.Commit!;
        File.WriteAllText(Path.Combine(_repo, "mut.txt"), "m\n");
        var expectedHead = "0123456789abcdef0123456789abcdef01234567";
        var currentHead = Git(_repo, "rev-parse", "HEAD").Trim();
        var outcome = await store.RestoreAsync(commit, expectedHead);
        Assert.False(outcome.Applied);
        Assert.Equal("head moved: " + expectedHead + ".." + currentHead, outcome.Reason);
        Assert.Empty(outcome.Touched);
        Assert.Equal("m\n", File.ReadAllText(Path.Combine(_repo, "mut.txt")));
    }

    [Fact]
    public async Task Restore_AppliedRewritesContentDeletesExtraAndTouchedUncapped()
    {
        var store = Store(_repo);
        // Snapshot baseline (readme.md + keep.md).
        var snap = await store.CreateSnapshotAsync();
        var commit = snap.Commit!;

        // Drift the work tree: rewrite keep.md, add an extra file that the snapshot lacks.
        File.WriteAllText(Path.Combine(_repo, "keep.md"), "drifted\n");
        File.WriteAllText(Path.Combine(_repo, "extra.txt"), "e\n");
        File.Delete(Path.Combine(_repo, "readme.md"));

        var beforeIndexMtime = RealIndexMtime(_repo);
        var outcome = await store.RestoreAsync(commit, snap.Head!);

        Assert.True(outcome.Applied, outcome.Reason);
        // keep.md rewritten + extra.txt deleted + readme.md rewritten => 3 touched, uncapped.
        Assert.Equal(3, outcome.Touched.Count);
        Assert.Contains("keep.md", outcome.Touched);
        Assert.Contains("extra.txt", outcome.Touched);
        Assert.Contains("readme.md", outcome.Touched);
        // keep.md content restored to the snapshot's baseline; extra.txt gone; readme.md back.
        Assert.Equal("keep\n", File.ReadAllText(Path.Combine(_repo, "keep.md")));
        Assert.False(File.Exists(Path.Combine(_repo, "extra.txt")));
        Assert.Equal("start\n", File.ReadAllText(Path.Combine(_repo, "readme.md")));
        // The real index was never opened: its mtime is unchanged.
        Assert.Equal(beforeIndexMtime, RealIndexMtime(_repo));
        // The real repo's refs/objects are untouched (no refs/pi in the real repo).
        Assert.DoesNotContain("refs/pi", Git(_repo, "for-each-ref"));
    }

    [Fact]
    public async Task Plan_PathsAreRepoRelativeForwardSlashes()
    {
        var store = Store(_repo);
        Directory.CreateDirectory(Path.Combine(_repo, "nested"));
        File.WriteAllText(Path.Combine(_repo, "nested", "deep.txt"), "d\n");
        var snap = await store.CreateSnapshotAsync();
        var commit = snap.Commit!;
        File.WriteAllText(Path.Combine(_repo, "nested", "deep.txt"), "changed\n");
        var plan = await store.PlanAsync(commit);
        Assert.True(plan.Ok, plan.Reason);
        Assert.Contains("nested/deep.txt", plan.Paths);
        // No leading slash, no backslash.
        Assert.All(plan.Paths, p =>
        {
            Assert.False(p.StartsWith('/'), p);
            Assert.DoesNotContain('\\', p);
        });
    }
}

public class CheckpointEntryTests
{
    [Fact]
    public void SessionRoundTrip_PreservesCheckpointEntry()
    {
        using var tmp = new TempWorkspace();
        var session = SessionManager.Create(tmp.Path, tmp.Path);
        var entry = new CheckpointEntry
        {
            Id = "",
            Commit = "abc123",
            Head = "",
            ToolCallIds = ["t1", "t2"],
            Green = false,
            Partial = true,
        };
        var id = session.AppendCheckpoint(entry);
        Assert.NotNull(session.SessionFile);

        var loaded = SessionManager.Open(session.SessionFile!);
        var checkpoint = Assert.IsType<CheckpointEntry>(loaded.Entries.Single(e => e.Id == id));
        Assert.Equal("abc123", checkpoint.Commit);
        Assert.Equal(["t1", "t2"], checkpoint.ToolCallIds);
        Assert.True(checkpoint.Partial);
        Assert.False(checkpoint.Green);
    }

    [Fact]
    public void SessionRoundTrip_CheckpointWithoutToolCallIds()
    {
        using var tmp = new TempWorkspace();
        var session = SessionManager.Create(tmp.Path, tmp.Path);
        var id = session.AppendCheckpoint(new CheckpointEntry { Id = "", Commit = "def456" });

        var loaded = SessionManager.Open(session.SessionFile!);
        var checkpoint = Assert.IsType<CheckpointEntry>(loaded.Entries.Single(e => e.Id == id));
        Assert.Equal("def456", checkpoint.Commit);
        Assert.Empty(checkpoint.ToolCallIds);
    }

    [Fact]
    public void EntryToJson_MalformedToolCallIdsAreToleratedOnLoad()
    {
        using var tmp = new TempWorkspace();
        var session = SessionManager.Create(tmp.Path, tmp.Path);
        session.AppendCheckpoint(new CheckpointEntry { Id = "cp3", Commit = "ghi789" });
        var file = session.SessionFile!;

        // Corrupt the line: toolCallIds with a non-string element.
        var raw = File.ReadAllText(file).Split('\n').Select(l =>
            l.Contains("\"commit\":\"ghi789\"")
                ? "{\"type\":\"checkpoint\",\"id\":\"cp3\",\"commit\":\"ghi789\",\"toolCallIds\":[123],\"green\":false,\"partial\":false}"
                : l).Aggregate(string.Empty, (a, l) => a + l + "\n");
        File.WriteAllText(file, raw);

        var loaded = SessionManager.Open(file);
        Assert.Contains(loaded.Entries, e => e.Id == "cp3");
    }

}
