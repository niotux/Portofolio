using System.Text.Json.Nodes;
using PiSharp.Core.Notebook;
using PiSharp.Tests.Tools;

namespace PiSharp.Tests.Notebook;

public sealed class NotebookUsageTests
{
    [Fact]
    public void UsageIsAppendOnlyLocalFoldAndDoesNotTouchNotebookState()
    {
        using var tmp = new TempWorkspace();
        int pending;
        string payloadHash;
        using (var store = new NotebookStore(tmp.Path, "seat"))
        {
            store.Put("n", "note", "durable search memory", "chat-1", "open", 0);
            pending = store.PendingCount;
            payloadHash = JsonNode.Parse(File.ReadAllLines(Path.Combine(tmp.Path, "notebook.jsonl"))[0])!["payloadHash"]!.GetValue<string>();
            store.RecordUsage("n", "recalled", "chat-1");
            store.RecordUsage("n", "recalled", "chat-1");
            store.RecordUsage("n", "used", "chat-1");
            Assert.Equal(pending, store.PendingCount);
            Assert.Equal(1, store.Get("n")!["revision"]!.GetValue<long>());
            Assert.Equal(payloadHash, JsonNode.Parse(File.ReadAllLines(Path.Combine(tmp.Path, "notebook.jsonl"))[0])!["payloadHash"]!.GetValue<string>());
            Assert.Equal(1, store.Usage("n").RecalledCount);
            Assert.Equal(1, store.Usage("n").UsedCount);
        }
        Assert.True(File.Exists(Path.Combine(tmp.Path, "usage.jsonl")));
        using var reopened = new NotebookStore(tmp.Path, "seat");
        Assert.Equal(1, reopened.Usage("n").RecalledCount);
        Assert.Equal(1, reopened.Usage("n").UsedCount);
        Assert.Equal(pending, reopened.PendingCount);
    }

    [Fact]
    public void TornTailIsCutAsideAndBadCompletedSidecarIsQuarantinedWithoutLosingNotebook()
    {
        using var tmp = new TempWorkspace();
        using (var store = new NotebookStore(tmp.Path, "seat"))
        {
            store.Put("n", "note", "keep me", "chat-1", "open", 0);
            store.RecordUsage("n", "used", "chat-1");
        }
        var sidecar = Path.Combine(tmp.Path, "usage.jsonl");
        File.AppendAllText(sidecar, "{torn");
        using (var store = new NotebookStore(tmp.Path, "seat")) Assert.Equal(1, store.Usage("n").UsedCount);
        File.WriteAllText(sidecar, "{bad json}\n");
        using (var store = new NotebookStore(tmp.Path, "seat"))
        {
            Assert.NotNull(store.Get("n"));
            Assert.Equal(0, store.Usage("n").UsedCount);
        }
        Assert.NotEmpty(Directory.GetFiles(tmp.Path, "usage.jsonl.quarantine-*"));
    }

    [Fact]
    public async Task NotebookGetCountsUsageOncePerTask()
    {
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Path, "seat");
        store.Put("n", "note", "read me", "chat-1", "open", 0);
        var tool = new NotebookTool(store, () => "chat-1");
        for (var i = 0; i < 2; i++)
        {
            var result = await tool.ExecuteAsync("t", new() { ["op"] = "get", ["id"] = "n" }, null, default);
            Assert.False(result.IsError);
        }
        Assert.Equal(1, store.Usage("n").UsedCount);
    }

    [Fact]
    public void RecallRanksUsingLocalUtilityAndCountsOnlySelectedNewNotes()
    {
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Path, "seat");
        store.Put("cold", "note", "shared topic memory", "", "open", 0, title: "shared topic");
        store.Put("warm", "note", "shared topic memory", "", "open", 0, title: "shared topic");
        store.RecordUsage("warm", "used", "earlier-task");

        var block = NotebookContext.RecalledNotes(store, "shared topic", new HashSet<string>(), 2000, "current-task");

        Assert.NotNull(block);
        Assert.True(block.IndexOf("warm", StringComparison.Ordinal) < block.IndexOf("cold", StringComparison.Ordinal));
        Assert.Equal(1, store.Usage("cold").RecalledCount);
        Assert.Equal(1, store.Usage("warm").RecalledCount);
        _ = NotebookContext.RecalledNotes(store, "shared topic", new HashSet<string>(), 2000, "current-task");
        Assert.Equal(1, store.Usage("cold").RecalledCount);
        Assert.Equal(1, store.Usage("warm").RecalledCount);
    }

    [Fact]
    public void DecayArchivesOnlyUnusedMemoryAndSearchHitRevalidatesIt()
    {
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Path, "seat");
        store.Put("old-note", "note", "distinctive archive query", "", "open", 0, title: "old note");
        store.Put("obligation", "commitment", "must remain visible", "", "open", 0);
        store.Put("used-note", "decision", "used stays active", "", "open", 0);
        store.RecordUsage("used-note", "used", "task-1");

        var archived = store.ArchiveUnused(DateTimeOffset.UtcNow.AddDays(15));

        Assert.Contains("old-note", archived);
        Assert.DoesNotContain("obligation", archived);
        Assert.DoesNotContain("used-note", archived);
        Assert.DoesNotContain("old-note", store.BriefingEntries().Select(e => e.Key));
        Assert.Null(NotebookContext.RecalledNotes(store, "distinctive archive query"));
        Assert.True(store.IsArchived("old-note"));
        Assert.Contains("old-note", store.Search("distinctive archive query", null, null, 5, out _, ["note"], includeArchived: true)
            .Select(h => h.Record["id"]!.GetValue<string>()));
        Assert.False(store.IsArchived("old-note"));
        Assert.Contains("old-note", store.BriefingEntries().Select(e => e.Key));
    }

    [Fact]
    public void DecayExemptsObligationsGuardRulesAndRuntimeEvidence()
    {
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Path, "seat");
        store.Put("instruction", "instruction", "active instruction", "", "open", 0);
        store.Put("commitment", "commitment", "open obligation", "", "open", 0);
        store.Put("check", "check", "runtime gate", "", "open", 0);
        store.Put("attempt", "attempt", "runtime evidence", "", "open", 0);
        store.Put("source", "source", "captured evidence", "", "open", 0);
        store.Put("guard", "decision", "active guard rule", "", "open", 0, tags: ["guard-rule"]);
        store.Put("runtime", "note", "runtime evidence note", "", "open", 0, tags: ["runtime-evidence"]);

        var archived = store.ArchiveUnused(DateTimeOffset.UtcNow.AddDays(15));

        Assert.Empty(archived);
        foreach (var id in new[] { "instruction", "commitment", "check", "attempt", "source", "guard", "runtime" })
            Assert.False(store.IsArchived(id));
    }

    [Fact]
    public void FreshContextPerformsDecay()
    {
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Path, "seat");
        store.Put("harmful", "note", "bad memory", "", "open", 0);
        store.RecordUsage("harmful", "harmful", "task-1");

        _ = NotebookContext.Prepare(store, [new PiSharp.Core.Messages.UserMessage("[fresh context] new task")]);

        Assert.True(store.IsArchived("harmful"));
    }

    [Fact]
    public void ConsolidationRejectsLostShaAndSupersedesOnlyAfterAValidMerge()
    {
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Path, "seat");
        const string sha = "abcdef1234567890";
        store.Put("first", "note", $"shared memory topic durable recurring note {sha}", "task", "open", 0, tags: ["topic"]);
        store.Put("second", "note", "shared memory topic durable recurring note", "task", "open", 0, tags: ["topic"]);

        Assert.Null(store.ConsolidatePair("first", "second", "merged-bad", "shared memory topic durable recurring note"));
        Assert.Equal("open", store.Get("first")!["status"]!.GetValue<string>());
        var merged = store.ConsolidatePair("first", "second", "merged-good", $"shared memory topic durable recurring note {sha}");

        Assert.NotNull(merged);
        Assert.Equal("superseded", store.Get("first")!["status"]!.GetValue<string>());
        Assert.Equal("superseded", store.Get("second")!["status"]!.GetValue<string>());
        Assert.Contains(sha, merged!["text"]!.GetValue<string>());
    }

    [Fact]
    public void HarmfulMemoryIsArchivedRegardlessOfAgeAndArchiveStateSurvivesRestart()
    {
        using var tmp = new TempWorkspace();
        using (var store = new NotebookStore(tmp.Path, "seat"))
        {
            store.Put("bad", "decision", "harmful old behavior", "", "open", 0);
            store.RecordUsage("bad", "harmful", "task-1");
            store.RecordUsage("bad", "used", "task-1");
            Assert.Contains("bad", store.ArchiveUnused(DateTimeOffset.UtcNow));
        }
        using var reopened = new NotebookStore(tmp.Path, "seat");
        Assert.True(reopened.IsArchived("bad"));
        Assert.Equal(1, reopened.Usage("bad").Harmful);
    }
}
