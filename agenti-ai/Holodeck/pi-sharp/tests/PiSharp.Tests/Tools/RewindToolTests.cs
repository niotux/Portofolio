using System.Diagnostics;
using PiSharp.Core.Checkpoints;
using PiSharp.Core.Session;
using PiSharp.Tools;

namespace PiSharp.Tests.Tools;

public sealed class RewindToolTests
{
    [Fact]
    public async Task DryRunShowsAtMostFortyPathsWithoutChangingTheWorktree()
    {
        using var ws = new TempWorkspace();
        InitGitRepo(ws.Path);
        for (var i = 0; i < 45; i++)
            ws.File($"file-{i:D2}.txt", $"snapshot-{i}\n");
        Git(ws.Path, "add", ".");
        Git(ws.Path, "commit", "-m", "checkpoint files");

        var store = NewStore(ws.Path);
        var snapshot = await store.CreateSnapshotAsync();
        Assert.True(snapshot.Created);
        Assert.NotNull(snapshot.Commit);
        Assert.NotNull(snapshot.Head);
        var session = SessionManager.InMemory(ws.Path);
        var id = session.AppendCheckpoint(new CheckpointEntry
        {
            Id = "",
            Commit = snapshot.Commit!,
            Head = snapshot.Head!,
        });

        for (var i = 0; i < 45; i++)
            File.Delete(Path.Combine(ws.Path, $"file-{i:D2}.txt"));

        var rewind = new RewindTool(ws.Path, store, session, new FileReadTracker());
        Assert.True(rewind.Parameters.ToJsonString().Length < 400);
        var result = await ToolRun.Exec(rewind, $$"""{"to":"{{id}}"}""");

        Assert.False(result.IsError);
        Assert.Contains("45 changed, 0 deleted", result.Text);
        Assert.Equal(40, result.Text.Split('\n').Count(line => line.StartsWith("- ", StringComparison.Ordinal)));
        Assert.All(Enumerable.Range(0, 45), i => Assert.False(File.Exists(Path.Combine(ws.Path, $"file-{i:D2}.txt"))));
    }

    [Fact]
    public async Task RealRestoreDeletesNewFilesPreservesIgnoredFilesAndForgetsTrackedRead()
    {
        using var ws = new TempWorkspace();
        InitGitRepo(ws.Path);
        ws.File(".gitignore", "ignored.bin\n");
        ws.File("story.txt", "before\n");
        Git(ws.Path, "add", ".");
        Git(ws.Path, "commit", "-m", "baseline");

        var store = NewStore(ws.Path);
        var snapshot = await store.CreateSnapshotAsync();
        Assert.True(snapshot.Created);
        Assert.NotNull(snapshot.Commit);
        Assert.NotNull(snapshot.Head);
        var session = SessionManager.InMemory(ws.Path);
        var targetId = session.AppendCheckpoint(new CheckpointEntry
        {
            Id = "",
            Commit = snapshot.Commit!,
            Head = snapshot.Head!,
            ToolCallIds = ["edit-call"],
        });

        ws.File("story.txt", "after\n");
        ws.File("new.txt", "new\n");
        ws.File("ignored.bin", "keep\n");

        var tracker = new FileReadTracker();
        var read = new ReadTool(ws.Path) { ReadTracker = tracker };
        var edit = new EditTool(ws.Path) { ReadTracker = tracker };
        await ToolRun.Exec(read, """{"path":"story.txt"}""");
        var rewind = new RewindTool(ws.Path, store, session, tracker);
        var dryRun = await ToolRun.Exec(rewind, """{"to":"before:edit-call","dryRun":true}""");
        Assert.False(dryRun.IsError);
        Assert.Contains("1 changed, 1 deleted", dryRun.Text);

        var result = await ToolRun.Exec(rewind, """{"to":"before:edit-call","dryRun":false}""");

        Assert.False(result.IsError);
        Assert.Equal("before\n", File.ReadAllText(Path.Combine(ws.Path, "story.txt")));
        Assert.False(File.Exists(Path.Combine(ws.Path, "new.txt")));
        Assert.Equal("keep\n", File.ReadAllText(Path.Combine(ws.Path, "ignored.bin")));
        Assert.Equal(targetId, session.Entries.OfType<CheckpointEntry>().Last().RewoundTo);

        var unread = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ToolRun.Exec(edit, """{"path":"story.txt","edits":[{"oldText":"before","newText":"changed"}]}"""));
        Assert.Contains("has not been read", unread.Message);
    }

    [Fact]
    public async Task RestoreRefusesAfterHeadMovesWithoutChangingWorktreeOrSession()
    {
        using var ws = new TempWorkspace();
        InitGitRepo(ws.Path);
        ws.File("story.txt", "before\n");
        Git(ws.Path, "add", ".");
        Git(ws.Path, "commit", "-m", "baseline");

        var store = NewStore(ws.Path);
        var snapshot = await store.CreateSnapshotAsync();
        Assert.True(snapshot.Created);
        Assert.NotNull(snapshot.Commit);
        Assert.NotNull(snapshot.Head);
        var session = SessionManager.InMemory(ws.Path);
        var targetId = session.AppendCheckpoint(new CheckpointEntry
        {
            Id = "",
            Commit = snapshot.Commit!,
            Head = snapshot.Head!,
        });
        ws.File("story.txt", "after\n");
        Git(ws.Path, "add", "story.txt");
        Git(ws.Path, "commit", "-m", "advance head");
        var movedHead = Git(ws.Path, "rev-parse", "HEAD");

        var result = await ToolRun.Exec(new RewindTool(ws.Path, store, session, new FileReadTracker()),
            $$"""{"to":"{{targetId}}","dryRun":false}""");

        Assert.True(result.IsError);
        Assert.Contains("head moved:", result.Text);
        Assert.Equal("after\n", File.ReadAllText(Path.Combine(ws.Path, "story.txt")));
        Assert.Equal(movedHead, Git(ws.Path, "rev-parse", "HEAD"));
        Assert.Single(session.Entries.OfType<CheckpointEntry>());
    }

    [Fact]
    public async Task PartialCheckpointIsRefusedBeforeRestore()
    {
        using var ws = new TempWorkspace();
        InitGitRepo(ws.Path);
        ws.File("story.txt", "before\n");
        Git(ws.Path, "add", ".");
        Git(ws.Path, "commit", "-m", "baseline");

        var store = NewStore(ws.Path);
        var snapshot = await store.CreateSnapshotAsync();
        Assert.True(snapshot.Created);
        Assert.NotNull(snapshot.Commit);
        Assert.NotNull(snapshot.Head);
        var session = SessionManager.InMemory(ws.Path);
        var targetId = session.AppendCheckpoint(new CheckpointEntry
        {
            Id = "",
            Commit = snapshot.Commit!,
            Head = snapshot.Head!,
            Partial = true,
        });
        ws.File("story.txt", "after\n");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ToolRun.Exec(new RewindTool(ws.Path, store, session, new FileReadTracker()),
                $$"""{"to":"{{targetId}}","dryRun":false}"""));

        Assert.Contains("partial checkpoint", error.Message);
        Assert.Equal("after\n", File.ReadAllText(Path.Combine(ws.Path, "story.txt")));
    }

    private static CheckpointStore NewStore(string cwd) =>
        new(cwd, Path.Combine(cwd, "checkpoint-shadow"));

    private static void InitGitRepo(string cwd)
    {
        Git(cwd, "init", "--quiet");
        Git(cwd, "config", "user.name", "Checkpoint tests");
        Git(cwd, "config", "user.email", "checkpoints@example.invalid");
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
