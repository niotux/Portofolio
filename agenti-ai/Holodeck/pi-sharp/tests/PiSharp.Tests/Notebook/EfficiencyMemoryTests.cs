using System.Text.Json.Nodes;
using PiSharp.Core.Messages;
using PiSharp.Core.Notebook;
using PiSharp.Core.Verification;
using PiSharp.Tests.Tools;

namespace PiSharp.Tests.Notebook;

public class EfficiencyMemoryTests
{
    [Theory]
    [InlineData(0)] [InlineData(25)] [InlineData(100)] [InlineData(5000)] [InlineData(50000)]
    public void CombinedBudgetIncludesPlanRecallAndWrappersOnSmallRequests(int size)
    {
        using var tmp = new TempWorkspace(); using var store = new NotebookStore(tmp.Path, "seat");
        for (var i = 0; i < 20; i++) store.Put("c" + i, "commitment", new string('x', 200), "t", "open", 0);
        store.Write("retry-note", "timeout retry process lesson", status: "completed");
        store.Write("s", "timeout retry process plan", kind: "plan-step");
        var messages = new Message[] { new UserMessage(new string('a', size)) };
        var prepared = NotebookContext.Prepare(store, messages, NotebookContext.Default(), 1, 1);
        var share = NotebookShareTracker.Measure(prepared);
        Assert.True(share.Share <= .15, $"size={size}, share={share.Share}");
        Assert.True(share.NotebookChars <= NotebookContext.BudgetMaxChars);
    }

    [Fact]
    public void RecitationNeverChangesAnEarlierMessage()
    {
        using var tmp = new TempWorkspace(); using var store = new NotebookStore(tmp.Path, "seat");
        store.Write("s", "implement feature", kind: "plan-step");
        var budget = NotebookContext.Default() with { BaseCharacters = 10000 };
        var messages = new List<Message> { new AssistantMessage { Model = "test", Content = [new TextContent("work")] } };
        var first = NotebookContext.Prepare(store, messages, budget, 1, 100);
        messages.Add(new AssistantMessage { Model = "test", Content = [new TextContent("more work")] });
        var second = NotebookContext.Prepare(store, messages, budget, 1, 100);
        Assert.Equal(first, second.Take(first.Count));
        Assert.True(second.Count > first.Count + 1);
    }

    [Fact]
    public void CompactionAndNewTaskShareOneRecallAllowanceAndInvalidationsAppend()
    {
        using var tmp = new TempWorkspace(); using var store = new NotebookStore(tmp.Path, "seat");
        store.Write("alpha", "alpha retry policy " + new string('x', 500), status: "completed");
        store.Write("beta", "beta garden procedure " + new string('y', 500), status: "completed");
        var budget = NotebookContext.Default() with { BaseCharacters = 5000 };
        var messages = new List<Message> { new CompactionSummaryMessage("alpha retry", 10), new UserMessage("beta garden") { TaskStart = true } };
        var first = NotebookContext.Prepare(store, messages, budget);
        Assert.True(NotebookShareTracker.Measure(first, new string('s', budget.BaseCharacters)).Share <= .15);
        store.Transition("alpha", "superseded");
        messages.Add(new AssistantMessage { Model = "test", Content = [new TextContent(new string('z', 2000))] });
        var second = NotebookContext.Prepare(store, messages, budget);
        Assert.Equal(first, second.Take(first.Count));
        Assert.Contains(second.OfType<UserMessage>(), m => m.Text.Contains("Recalled memory alpha changed or became inactive"));
        Assert.True(NotebookShareTracker.Measure(second, new string('s', budget.BaseCharacters)).Share <= .15);
    }

    [Fact]
    public void UsageIsDeduplicatedAcrossRestart()
    {
        using var tmp = new TempWorkspace();
        using (var store = new NotebookStore(tmp.Path, "seat")) store.RecordUsage("n", "helpful", "task1");
        using var restarted = new NotebookStore(tmp.Path, "seat");
        restarted.RecordUsage("n", "helpful", "task1");
        restarted.RecordUsage("n", "helpful", "task2");
        Assert.Equal(2, restarted.Usage("n").Helpful);
    }

    [Fact]
    public void ExplicitHarnessReminderDoesNotStartRecallAndGitWorktreesShareScope()
    {
        using var tmp = new TempWorkspace(); using var store = new NotebookStore(tmp.Dir("memory"), "seat");
        store.Write("n", "retry timeout policy", status: "completed");
        var reminder = new UserMessage("retry timeout policy reminder") { TaskStart = false };
        var prepared = NotebookContext.Prepare(store, [reminder], NotebookContext.Default() with { BaseCharacters = 10000 });
        Assert.DoesNotContain(prepared.OfType<UserMessage>(), m => m.Text.StartsWith("<recalled-notes>"));
        var main = tmp.Dir("main"); var gitDir = tmp.Dir("main/.git");
        var worktree = tmp.Dir("worktree"); var perWorktree = tmp.Dir("main/.git/worktrees/feature");
        System.IO.File.WriteAllText(System.IO.Path.Combine(perWorktree, "commondir"), "../..\n");
        System.IO.File.WriteAllText(System.IO.Path.Combine(worktree, ".git"), "gitdir: " + perWorktree + "\n");
        Assert.Equal(NotebookLessons.RepositoryScope(main), NotebookLessons.RepositoryScope(worktree));
    }

    [Fact]
    public async Task PlainPromptRecallsParaphrasedMemoryAfterRestartWithEmbeddingCache()
    {
        using var tmp = new TempWorkspace(); var embeddings = new SynonymEmbeddings();
        using (var store = new NotebookStore(tmp.Path, "seat"))
        {
            store.Write("kill-note", "Terminate process using its group identifier", status: "completed", tags: ["repo:one"]);
            store.Write("wrong-repo", "Terminate process using its group identifier", status: "completed", tags: ["repo:two"]);
            store.Write("obsolete", "Terminate process using its group identifier", status: "superseded");
            store.RecallScope = "repo:one";
            store.SemanticIndex = new(tmp.Path, store.Owner, embeddings);
            await store.PrepareRecallAsync("stop worker", default);
        }
        var documentCalls = embeddings.Documents;
        using var restarted = new NotebookStore(tmp.Path, "seat") { RecallScope = "repo:one" };
        restarted.SemanticIndex = new(tmp.Path, restarted.Owner, embeddings);
        await restarted.PrepareRecallAsync("stop worker", default);
        Assert.Equal(documentCalls, embeddings.Documents);
        var prepared = NotebookContext.Prepare(restarted, [new UserMessage("stop worker")], NotebookContext.Default() with { BaseCharacters = 10000 });
        var recall = prepared.OfType<UserMessage>().Single(m => m.Text.StartsWith("<recalled-notes>")).Text;
        Assert.Contains("kill-note", recall);
        Assert.DoesNotContain("wrong-repo", recall); Assert.DoesNotContain("obsolete", recall);
        restarted.Write("kill-note", "garden planting procedure", status: "completed");
        Assert.Empty(restarted.RecallSearch("stop worker", "stop or worker", 5)); // stale vector cannot match new revision
    }

    [Fact]
    public async Task EmbeddingFailureFallsBackToLexicalSearch()
    {
        using var tmp = new TempWorkspace(); using var store = new NotebookStore(tmp.Path, "seat");
        store.Write("n", "retry timeout fix", status: "completed");
        store.SemanticIndex = new(tmp.Path, store.Owner, new BrokenEmbeddings());
        await store.PrepareRecallAsync("retry timeout", default);
        Assert.Contains("n", NotebookContext.RecalledNotes(store, "retry timeout"));
        Assert.NotNull(store.SemanticIndex.LastError);
    }

    [Fact]
    public void VerificationProducesBoundedRecoveryLessonsAndFeedbackRequiresEvidence()
    {
        using var tmp = new TempWorkspace(); using var store = new NotebookStore(tmp.Path, "seat") { RecallScope = "repo:test" };
        store.Put("source", "source", "fix retry timeout behavior", "t", "open", 0);
        var failed = VerificationRecords.Record(store, Report(false));
        var passed = VerificationRecords.Record(store, Report(true));
        var lessons = store.List(task: "t", kind: "note");
        Assert.Equal(2, lessons.Count);
        var recovery = lessons.Single(n => n["text"]!.GetValue<string>().Contains("verified-recovery"));
        Assert.Contains(failed, recovery["text"]!.GetValue<string>());
        var id = recovery["id"]!.GetValue<string>();
        Assert.Throws<ArgumentException>(() => NotebookLessons.Feedback(store, id, "helpful", "t", failed));
        NotebookLessons.Feedback(store, id, "helpful", "t", passed);
        Assert.Equal(1, store.Usage(id).Helpful);
        for (var i = 0; i < 4; i++) VerificationRecords.Record(store, Report(true));
        Assert.Equal(NotebookLessons.MaxLessonsPerTask, store.List(task: "t", kind: "note").Count);
    }

    private static VerificationReport Report(bool passed) => new("t", [new VerificationResult("retry-check", passed,
        passed ? 0 : 1, "observed", new JsonObject(), new JsonObject(), true, "abcdef1", DateTimeOffset.UtcNow)], passed, null);

    private sealed class SynonymEmbeddings : INotebookEmbeddings
    {
        public string Identity => "synonyms-v1";
        public int Documents { get; private set; }
        public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct)
        {
            Documents += texts.Count(t => t.Contains("Terminate", StringComparison.Ordinal));
            return Task.FromResult<IReadOnlyList<float[]>>(texts.Select(t => t.Contains("Terminate") || t.Contains("stop worker") ? new float[] { 1, 0 } : [0, 1]).ToArray());
        }
    }
    private sealed class BrokenEmbeddings : INotebookEmbeddings
    {
        public string Identity => "broken";
        public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct) => throw new HttpRequestException();
    }
}
