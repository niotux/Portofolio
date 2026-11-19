using PiSharp.Core.Notebook;
using PiSharp.Core.Messages;
using PiSharp.Tests.Tools;

namespace PiSharp.Tests.Notebook;

public class NotebookTests
{
    [Fact]
    public void ATooLongTaskIsReportedAsTheTaskNotTheId()
    {
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Path, "seat1");
        var e = Assert.Throws<ArgumentException>(() => store.Put("commit-6pi-restart-20260925", "commitment", "", new string('x', 161), "open", 0));
        Assert.StartsWith("task is a task key", e.Message);
        Assert.Throws<ArgumentException>(() => store.Put("bad id", "note", "", "chat-1", "open", 0)).Message.StartsWith("Notebook IDs");
    }

    [Fact]
    public async Task TransitionPreservesExactSourceAndRejectsStaleOrRuntimeChanges()
    {
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Path, "seat1");
        store.Put("s", "source", new string('x', 10000) + "late condition", "task", "open", 0);
        store.Put("c", "commitment", "verify", "task", "open", 0, "s");
        store.Put("job-1", "attempt", "runtime", "task", "open", 0);
        var tool = new NotebookTool(store);
        async Task<bool> Change(string id, long revision, string status) => (await tool.ExecuteAsync("t", new()
            { ["op"] = "transition", ["id"] = id, ["expectedRevision"] = revision, ["status"] = status }, null, default)).IsError;
        Assert.False(await Change("s", 1, "completed"));
        Assert.EndsWith("late condition", store.Get("s")!["text"]!.GetValue<string>());
        // A stale revision on a transition applies to the current one: it only changes the status.
        Assert.False(await Change("s", 1, "cancelled"));
        Assert.Equal("cancelled", store.Get("s")!["status"]!.GetValue<string>());
        Assert.EndsWith("late condition", store.Get("s")!["text"]!.GetValue<string>());
        Assert.True(await Change("job-1", 1, "completed"));
        Assert.True(await Change("c", 1, "done"));
        Assert.False(await Change("c", 1, "completed"));
        Assert.Equal("s", store.Get("c")!["sourceId"]!.GetValue<string>());
    }

    [Fact]
    public void RestartPreservesPendingCommitmentsAndAcknowledgements()
    {
        using var tmp = new TempWorkspace();
        using (var store = new NotebookStore(tmp.Path, "seat1"))
        {
            store.Put("s1", "source", "Do not deploy before tests pass", "t1", "open", 0, operationId: "op1");
            store.Put("c1", "commitment", "Run tests", "t1", "open", 0, "s1", "op2");
            store.Acknowledge("op1");
        }
        using var resumed = new NotebookStore(tmp.Path, "seat1");
        Assert.Equal("op2", Assert.Single(resumed.Pending())["operationId"]!.GetValue<string>());
        Assert.Contains("Run tests", resumed.Briefing());
        Assert.Contains("Do not deploy", resumed.Briefing());
    }

    [Fact]
    public void ReplaysAreIdempotentAndStaleOrChangedWritesFail()
    {
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Path, "seat1");
        var first = store.Put("c1", "commitment", "Test", "t", "open", 0, operationId: "op");
        Assert.Equal(first.ToJsonString(), store.Put("c1", "commitment", "Test", "t", "open", 0, operationId: "op").ToJsonString());
        Assert.Throws<InvalidOperationException>(() => store.Put("c1", "commitment", "Changed", "t", "open", 0, operationId: "op"));
        Assert.Throws<InvalidOperationException>(() => store.Put("c1", "commitment", "Changed", "t", "open", 0));
        store.Put("c1", "commitment", "Test", "t", "completed", 1);
        Assert.DoesNotContain("c1 r", store.Briefing());
    }

    [Fact]
    public void TornTailRecoveredButCompletedCorruptionRejected()
    {
        using var tmp = new TempWorkspace();
        using (var store = new NotebookStore(tmp.Path, "seat1")) store.Put("s", "source", "exact", "", "open", 0);
        var journal = Path.Combine(tmp.Path, "notebook.jsonl");
        File.AppendAllText(journal, "{\"broken");
        using (var store = new NotebookStore(tmp.Path, "seat1")) Assert.NotNull(store.Get("s"));
        File.AppendAllText(journal, "broken\n");
        Assert.ThrowsAny<Exception>(() => new NotebookStore(tmp.Path, "seat1"));
    }

    [Fact]
    public void ATornRecordCompletedByTheNextAppendIsSetAside_AndTheRecordsAroundItStay()
    {
        // strata 2026-10-21: a full disk tore one append, the next append completed that line with its own record,
        // and the seat (pi12) failed at every start on "<torn><record>\n".
        using var tmp = new TempWorkspace();
        using (var store = new NotebookStore(tmp.Path, "seat1"))
        {
            store.Put("a", "source", "first", "", "open", 0);
            store.Put("b", "source", "second", "", "open", 0);
        }
        var journal = Path.Combine(tmp.Path, "notebook.jsonl");
        var lines = File.ReadAllLines(journal);
        var head = string.Concat(lines[..^1].Select(l => l + "\n"));
        var last = lines[^1];
        File.WriteAllText(journal, head + last[..30] + last + "\n");

        using (var store = new NotebookStore(tmp.Path, "seat1"))
        {
            Assert.NotNull(store.Get("a"));
            Assert.NotNull(store.Get("b"));
        }
        Assert.Equal(head + last + "\n", File.ReadAllText(journal));
        Assert.Equal(last[..30], File.ReadAllText(Assert.Single(Directory.GetFiles(tmp.Path, "notebook.jsonl.torn-*"))));
        using (var store = new NotebookStore(tmp.Path, "seat1")) Assert.NotNull(store.Get("b"));
    }

    [Fact]
    public void LeaseAndOwnerPreventAccidentalCrossWriterUse()
    {
        using var tmp = new TempWorkspace();
        using (var store = new NotebookStore(tmp.Path, "seat1"))
        {
            store.Put("s", "source", "exact", "", "open", 0);
            Assert.Throws<IOException>(() => new NotebookStore(tmp.Path, "seat1"));
        }
        Assert.Throws<InvalidDataException>(() => new NotebookStore(tmp.Path, "seat2"));
    }

    [Fact]
    public void ExactSourceSurvivesTranscriptReplacementWithoutDuplication()
    {
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Path, "seat1");
        var messages = new Message[] { new UserMessage("Keep my pending tasks") };
        NotebookContext.Prepare(store, messages);
        NotebookContext.Prepare(store, messages);
        Assert.Single(store.List());
        var result = NotebookContext.Prepare(store, [], NotebookContext.Default() with { BaseCharacters = 120000 });
        Assert.Contains("Keep my pending tasks", Assert.IsType<UserMessage>(result[0]).Text);
    }
}
