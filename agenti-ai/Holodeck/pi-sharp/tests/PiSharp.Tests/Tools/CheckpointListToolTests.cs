using System.Diagnostics;
using PiSharp.Core.Checkpoints;
using PiSharp.Core.Session;
using PiSharp.Tools;

namespace PiSharp.Tests.Tools;

// pi-sharp-cp6 (decision pi-sharp-cp6-list): acceptance
// 1: 5+ snapshots listed newest-first with all fields; 2: empty store -> empty list, no error;
// 3: never touches the seat's .git (no git process is started on the list path);
// 4: read-only (no shadow-store writes).
public sealed class CheckpointListToolTests
{
    [Fact]
    public async Task ListsFivePlusSnapshotsNewestFirstWithAllFieldsAndRespectsLimit()
    {
        using var ws = new TempWorkspace();
        InitGitRepo(ws.Path);
        ws.File("story.txt", "v0\n");
        Git(ws.Path, "add", ".");
        Git(ws.Path, "commit", "-m", "baseline");

        var store = NewStore(ws.Path);
        var session = SessionManager.InMemory(ws.Path);
        var ids = new List<string>();
        for (var i = 1; i <= 6; i++)
        {
            ws.File("story.txt", $"v{i}\n");
            Git(ws.Path, "add", "story.txt");
            Git(ws.Path, "commit", "-m", $"advance {i}");
            var snapshot = await store.CreateSnapshotAsync();
            Assert.True(snapshot.Created);
            Assert.NotNull(snapshot.Commit);
            ids.Add(session.AppendCheckpoint(new CheckpointEntry
            {
                Id = "",
                Commit = snapshot.Commit!,
                WorkspaceRoot = store.Cwd,
                Head = snapshot.Head!,
                Green = i == 6,
                Partial = false,
            }));
        }

        var tool = new CheckpointListTool(ws.Path, store, session);
        var result = await ToolRun.Exec(tool, "{}");

        Assert.False(result.IsError);
        // Default limit is 20, so all 6 rows come back, newest first (v6 snapshot first).
        var lines = result.Text.Split('\n');
        Assert.Contains(lines, line => line.StartsWith("- id=" + ids[5], StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith("- id=" + ids[0], StringComparison.Ordinal));
        var newest = lines.First(i => i.Contains(ids[5], StringComparison.Ordinal));
        var oldest = lines.First(i => i.Contains(ids[0], StringComparison.Ordinal));
        Assert.True(lines.ToList().IndexOf(newest) < lines.ToList().IndexOf(oldest));
        // Fields: ts, green, partial, commit prefix 8, head.
        Assert.Matches(@"^- id=[0-9a-f]{8} ts=\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}[+-]\d{2}:\d{2} green=yes partial=no commit=[0-9a-f]{8} head=[0-9a-f]{40}$", newest);
        Assert.Matches(@"^- id=[0-9a-f]{8} ts=\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}[+-]\d{2}:\d{2} green=no partial=no commit=[0-9a-f]{8} head=[0-9a-f]{40}$", oldest);
        var firstLine = lines.First();
        Assert.StartsWith("6 of 6 recent checkpoints for ", firstLine, StringComparison.Ordinal);

        // Explicit limit smaller than the count: exactly N newest rows.
        var limited = await ToolRun.Exec(tool, """{"limit":2}""");
        Assert.False(limited.IsError);
        var limitedLines = limited.Text.Split('\n').Where(line => line.StartsWith("- id=", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, limitedLines.Length);
        Assert.StartsWith("- id=" + ids[5], limitedLines[0], StringComparison.Ordinal);
        Assert.StartsWith("- id=" + ids[4], limitedLines[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmptyStoreReturnsEmptyListNotAnError()
    {
        using var ws = new TempWorkspace();
        InitGitRepo(ws.Path);
        ws.File("story.txt", "v0\n");
        Git(ws.Path, "add", ".");
        Git(ws.Path, "commit", "-m", "baseline");

        var store = NewStore(ws.Path);
        var session = SessionManager.InMemory(ws.Path);
        var tool = new CheckpointListTool(ws.Path, store, session);
        var result = await ToolRun.Exec(tool, "{}");

        Assert.False(result.IsError);
        Assert.Contains("No checkpoints recorded", result.Text);
    }

    [Fact]
    public async Task ListIgnoresSnapshotsOfOtherWorkspaces()
    {
        using var ws = new TempWorkspace();
        using var other = new TempWorkspace();
        InitGitRepo(ws.Path);
        InitGitRepo(other.Path);
        ws.File("story.txt", "v0\n");
        Git(ws.Path, "add", ".");
        Git(ws.Path, "commit", "-m", "baseline");

        var store = NewStore(ws.Path);
        var otherStore = new CheckpointStore(other.Path, Path.Combine(other.Path, "checkpoint-shadow"));
        var session = SessionManager.InMemory(ws.Path);
        var snapshot = await store.CreateSnapshotAsync();
        var otherSnapshot = await otherStore.CreateSnapshotAsync();
        Assert.True(snapshot.Created);
        Assert.True(otherSnapshot.Created);
        var mineId = session.AppendCheckpoint(new CheckpointEntry
        {
            Id = "",
            Commit = snapshot.Commit!,
            WorkspaceRoot = store.Cwd,
            Head = snapshot.Head!,
        });
        var theirsId = session.AppendCheckpoint(new CheckpointEntry
        {
            Id = "",
            Commit = otherSnapshot.Commit!,
            WorkspaceRoot = otherStore.Cwd,
            Head = otherSnapshot.Head!,
        });

        var tool = new CheckpointListTool(ws.Path, store, session);
        var result = await ToolRun.Exec(tool, "{}");

        Assert.False(result.IsError);
        Assert.Contains("id=" + mineId, result.Text);
        Assert.DoesNotContain("id=" + theirsId, result.Text);
        Assert.StartsWith("1 of 1 recent checkpoints", result.Text.Split('\n')[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListPathStartsNoGitProcessAndLeavesTheStoreUntouched()
    {
        using var ws = new TempWorkspace();
        InitGitRepo(ws.Path);
        ws.File("story.txt", "v0\n");
        Git(ws.Path, "add", ".");
        Git(ws.Path, "commit", "-m", "baseline");

        var store = NewStore(ws.Path);
        var snapshot = await store.CreateSnapshotAsync();
        Assert.True(snapshot.Created);
        var session = SessionManager.InMemory(ws.Path);
        var entryId = session.AppendCheckpoint(new CheckpointEntry
        {
            Id = "",
            Commit = snapshot.Commit!,
            WorkspaceRoot = store.Cwd,
            Head = snapshot.Head!,
        });

        // Fingerprint the whole tree (seat .git + shadow store) before and after the call.
        var before = TreeFingerprint(ws.Path);
        var tool = new CheckpointListTool(ws.Path, store, session);
        var result = await ToolRun.Exec(tool, "{}");
        var after = TreeFingerprint(ws.Path);

        Assert.False(result.IsError);
        Assert.Equal(before, after);

        // Acceptance 3+4: the list code path touches only the in-memory session entries. Run it
        // with PATH cleared so any accidental git (or other) shell-out fails instead of passing
        // silently: the listing must still succeed, proving it shelled nothing.
        var originalPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable("PATH", "/nonexistent-cp6-list");
            var reread = await ToolRun.Exec(tool, "{}");
            Assert.False(reread.IsError);
            Assert.Contains("id=" + entryId, reread.Text);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
        }
        Assert.Single(session.Entries.OfType<CheckpointEntry>());
    }

    private static CheckpointStore NewStore(string cwd) =>
        new(cwd, Path.Combine(cwd, "checkpoint-shadow"));

    private static string TreeFingerprint(string root)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("find")
            {
                WorkingDirectory = root,
                RedirectStandardOutput = true,
            },
        };
        process.StartInfo.ArgumentList.Add(root);
        process.StartInfo.ArgumentList.Add("-type");
        process.StartInfo.ArgumentList.Add("f");
        process.StartInfo.ArgumentList.Add("-exec");
        process.StartInfo.ArgumentList.Add("sha1sum");
        process.StartInfo.ArgumentList.Add("{}");
        process.StartInfo.ArgumentList.Add(";");
        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        var sb = new System.Text.StringBuilder();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .OrderBy(l => l, StringComparer.Ordinal))
        {
            var tab = line.IndexOf('\t');
            if (tab < 0) continue;
            sb.Append(line[..tab].Split(' ')[0]).Append('\0');
        }
        return sb.ToString();
    }

    private static void InitGitRepo(string cwd)
    {
        Git(cwd, "init", "--quiet");
        Git(cwd, "config", "user.name", "CP6 list tests");
        Git(cwd, "config", "user.email", "cp6@example.invalid");
    }

    private static string Git(string cwd, params string[] args)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("git")
            {
                WorkingDirectory = cwd,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
        };
        foreach (var arg in args) process.StartInfo.ArgumentList.Add(arg);
        process.Start();
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"git {string.Join(' ', args)} failed: {stderr}");
        return stdout.Trim();
    }
}
