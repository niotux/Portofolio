using System.Text.Json.Nodes;
using PiSharp.Cli;
using PiSharp.Core.Agent;
using PiSharp.Core.Config;
using PiSharp.Core.Messages;
using PiSharp.Core.Notebook;
using PiSharp.Core.Session;
using PiSharp.Tests.Agent;
using PiSharp.Tests.Tools;

namespace PiSharp.Tests.Notebook;

/// <summary>Phase 4: the notebook as working memory (write, search, batch, self-closing sources, a briefing sent
/// once per context, recall, import).</summary>
public class MemoryTests
{
    // These behavior fixtures model a full harness prompt; budget boundaries are tested separately.
    private static IReadOnlyList<Message> Prepare(NotebookStore store, IReadOnlyList<Message> messages,
        int? reciteEvery = null, int? stallTurns = null) => NotebookContext.Prepare(store, messages,
            NotebookContext.Default() with { BaseCharacters = 120000 }, reciteEvery, stallTurns);

    private static readonly DateTimeOffset T0 = new(2026, 9, 28, 8, 0, 0, TimeSpan.Zero);
    private static UserMessage User(string text, int minute) => new([new TextContent(text)], T0.AddMinutes(minute));
    private static string Text(AgentToolResult result) => string.Concat(result.Content.OfType<TextContent>().Select(t => t.Text));
    private static Task<AgentToolResult> Call(NotebookTool tool, JsonObject args) => tool.ExecuteAsync("t", args, null, default);
    private static int JournalRows(TempWorkspace tmp, string id) => File.ReadAllLines(Path.Combine(tmp.Path, "notebook.jsonl"))
        .Count(l => JsonNode.Parse(l)?["record"]?["id"]?.GetValue<string>() == id);

    // ------------------------------------------------------------------ write / get

    [Fact]
    public async Task WriteUpsertsWithoutARevisionLastWriterWinsAndKeepsHistory()
    {
        using var tmp = new TempWorkspace();
        using (var store = new NotebookStore(tmp.Path, "seat"))
        {
            var tool = new NotebookTool(store);
            var first = await Call(tool, new() { ["op"] = "write", ["key"] = "p4-design", ["title"] = "Phase 4 design", ["tags"] = new JsonArray("P4", "notebook"), ["text"] = "search in process" });
            Assert.False(first.IsError, Text(first));
            var second = await Call(tool, new() { ["op"] = "write", ["key"] = "p4-design", ["text"] = "search in process, BM25" });
            Assert.False(second.IsError, Text(second));
            var again = await Call(tool, new() { ["op"] = "write", ["key"] = "p4-design", ["text"] = "search in process, BM25" });
            Assert.Contains("\"unchanged\":true", Text(again));

            var record = store.Get("p4-design")!;
            Assert.Equal(2, record["revision"]!.GetValue<long>());
            Assert.Equal("search in process, BM25", record["text"]!.GetValue<string>());
            Assert.Equal("Phase 4 design", record["title"]!.GetValue<string>()); // omitted fields keep their values
            Assert.Equal("[\"p4\",\"notebook\"]", record["tags"]!.ToJsonString());
            Assert.Equal("note", record["kind"]!.GetValue<string>());

            var get = await Call(tool, new() { ["op"] = "get", ["key"] = "p4-design" });
            Assert.Contains("BM25", Text(get));
            Assert.Contains("Phase 4 design", Text(get));
        }
        Assert.Equal(2, JournalRows(tmp, "p4-design")); // both versions stay in the journal
        using var reopened = new NotebookStore(tmp.Path, "seat"); // title and tags are outside the payload hash
        Assert.Equal("Phase 4 design", reopened.Get("p4-design")!["title"]!.GetValue<string>());
        Assert.Equal(2, reopened.Pending().Count);
    }

    [Fact]
    public async Task WriteCannotOverwriteASourceOrRuntimeEvidence()
    {
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Path, "seat");
        store.Put("s1", "source", "exact words", "", "open", 0);
        var tool = new NotebookTool(store);
        Assert.True((await Call(tool, new() { ["op"] = "write", ["id"] = "s1", ["text"] = "rewritten" })).IsError);
        Assert.True((await Call(tool, new() { ["op"] = "write", ["id"] = "new", ["kind"] = "source", ["text"] = "x" })).IsError);
        Assert.True((await Call(tool, new() { ["op"] = "write", ["id"] = "job-1", ["text"] = "x" })).IsError);
        Assert.Equal("exact words", store.Get("s1")!["text"]!.GetValue<string>());
    }

    // ------------------------------------------------------------------ search

    [Fact]
    public async Task SearchRanksTitleAndTagsAboveTextAndReturnsShortSnippets()
    {
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Path, "seat");
        store.Write("in-text", new string('x', 400) + " the migration was additive " + new string('y', 400), "Unrelated heading");
        store.Write("in-title", "details elsewhere", "Migration plan", ["db"]);
        store.Write("in-tags", "other words", "Plan", ["migration"]);
        store.Write("no-match", "nothing here", "Nothing");
        var tool = new NotebookTool(store);
        var json = JsonNode.Parse(Text(await Call(tool, new() { ["op"] = "search", ["query"] = "migration" })))!;
        var ids = json["results"]!.AsArray().Select(r => r!["id"]!.GetValue<string>()).ToArray();
        Assert.Equal(3, ids.Length);
        Assert.Equal("in-text", ids[^1]);
        Assert.Equal("all", json["match"]!.GetValue<string>());
        var snippet = json["results"]!.AsArray().Single(r => r!["id"]!.GetValue<string>() == "in-text")!["snippet"]!.GetValue<string>();
        Assert.Contains("migration was additive", snippet);
        Assert.True(snippet.Length <= 170, snippet);

        var tagged = JsonNode.Parse(Text(await Call(tool, new() { ["op"] = "search", ["query"] = "migration", ["tags"] = new JsonArray("db") })))!;
        Assert.Equal("in-title", Assert.Single(tagged["results"]!.AsArray())!["id"]!.GetValue<string>());
    }

    [Fact]
    public void SearchSupportsPhrasesExclusionOrTaskFilterAndFallsBackToAnyWord()
    {
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Path, "seat");
        store.Write("a", "rate limit handover on the board", task: "chat-1");
        store.Write("b", "limit the rate of wakes", task: "chat-2");
        store.Write("c", "handover note for wi-12", task: "chat-1");
        string[] Ids(string q, string? task = null) => store.Search(q, null, task, 10, out _).Select(h => h.Record["id"]!.GetValue<string>()).ToArray();

        Assert.Equal(["a"], Ids("\"rate limit\""));
        Assert.Equal(["b"], Ids("rate -handover"));
        Assert.Equal(["a", "c"], Ids("handover", "chat-1").Order());
        Assert.Equal(new[] { "b", "c" }, Ids("wakes or wi").Order());
        Assert.Equal(["c"], Ids("handover wakes or wi")); // or binds tighter than and
        var hits = store.Search("handover zebra", null, null, 10, out var mode);
        Assert.Equal("any", mode);
        Assert.Equal(2, hits.Count);
    }

    [Fact]
    public async Task SearchReturnsTenByDefault()
    {
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Path, "seat");
        for (var i = 0; i < 30; i++) store.Write("n" + i, "common word " + i);
        var tool = new NotebookTool(store);
        var json = JsonNode.Parse(Text(await Call(tool, new() { ["op"] = "search", ["query"] = "common" })))!;
        Assert.Equal(10, json["results"]!.AsArray().Count);
    }

    // ------------------------------------------------------------------ batch, retries, hints

    [Fact]
    public async Task BatchReportsEachResultAndOneFailureDoesNotStopTheOthers()
    {
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Path, "seat");
        var tool = new NotebookTool(store);
        var result = await Call(tool, new()
        {
            ["op"] = "batch",
            ["ops"] = new JsonArray(
                new JsonObject { ["op"] = "put", ["id"] = "c1", ["kind"] = "commitment", ["text"] = "ship", ["expectedRevision"] = 0 },
                new JsonObject { ["op"] = "transition", ["id"] = "c1", ["status"] = "completed", ["expectedRevision"] = 0 },
                new JsonObject { ["op"] = "transition", ["id"] = "c1x", ["status"] = "completed" },
                new JsonObject { ["op"] = "write", ["id"] = "n1", ["title"] = "Found", ["text"] = "the cause" },
                new JsonObject { ["op"] = "get", ["id"] = "n1" }),
        });
        Assert.False(result.IsError, Text(result));
        var json = JsonNode.Parse(Text(result))!;
        Assert.Equal(3, json["applied"]!.GetValue<int>());
        Assert.Equal(2, json["failed"]!.GetValue<int>());
        var rows = json["results"]!.AsArray();
        Assert.Equal(new[] { true, true, false, true, false }, rows.Select(r => r!["ok"]!.GetValue<bool>()));
        Assert.Contains("stale", rows[1]!["result"]!["note"]!.GetValue<string>());
        Assert.Contains("Closest IDs: c1", rows[2]!["error"]!.GetValue<string>());
        Assert.Equal("completed", store.Get("c1")!["status"]!.GetValue<string>());
        Assert.Equal("the cause", store.Get("n1")!["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task AStalePutThatOnlyChangesStatusIsReappliedAndAChangedOneStillConflicts()
    {
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Path, "seat");
        store.Put("c", "commitment", "verify", "t", "open", 0);
        store.Put("c", "commitment", "verify", "t", "blocked", 1);
        var tool = new NotebookTool(store);
        var ok = await Call(tool, new() { ["op"] = "put", ["id"] = "c", ["kind"] = "commitment", ["text"] = "verify", ["task"] = "t", ["status"] = "completed", ["expectedRevision"] = 1 });
        Assert.False(ok.IsError, Text(ok));
        Assert.Equal(3, store.Get("c")!["revision"]!.GetValue<long>());
        var changed = await Call(tool, new() { ["op"] = "put", ["id"] = "c", ["kind"] = "commitment", ["text"] = "other", ["task"] = "t", ["expectedRevision"] = 1 });
        Assert.True(changed.IsError);
        Assert.Contains("\"revision\":3", Text(changed));
    }

    [Fact]
    public async Task TransitionOfAMissingRecordNamesTheClosestKeys()
    {
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Path, "seat");
        store.Write("wi-12-findings", "x");
        var result = await Call(new NotebookTool(store), new() { ["op"] = "transition", ["id"] = "wi-12-finding", ["status"] = "completed" });
        Assert.True(result.IsError);
        Assert.Contains("Closest IDs: wi-12-findings", Text(result));
    }

    // ------------------------------------------------------------------ sources close themselves

    [Fact]
    public void AnAckCompletesTheSourcesOfThatMessageByIdTaskAndPromptCopy()
    {
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Path, "seat");
        store.Put("chat-source-7", "source", "[inbox from @a in board, message 7, mention] do it", "chat-7", "open", 0);
        store.Put("source-copy", "source", "[inbox from @a in board, message 7, mention] do it", "", "open", 0);
        store.Put("amend", "source", "later amendment", "chat-7", "open", 0);
        store.Put("other", "source", "[inbox from @a in board, message 70, mention] other", "chat-70", "open", 0);
        var done = NotebookSources.CompleteForMessages(store, [7]);
        Assert.Equal(["amend", "chat-source-7", "source-copy"], done.Order());
        Assert.Equal("open", store.Get("other")!["status"]!.GetValue<string>());
    }

    [Fact]
    public void StartupExpiresOpenSourcesOlderThan48Hours()
    {
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Path, "seat");
        store.Put("old", "source", "old", "", "open", 0);
        store.Put("old-blocked", "source", "waits", "", "blocked", 0);
        store.Put("commit", "commitment", "keep", "", "open", 0);
        Assert.Empty(NotebookSources.ExpireStale(store, DateTimeOffset.UtcNow.AddHours(47)));
        Assert.Equal(["old"], NotebookSources.ExpireStale(store, DateTimeOffset.UtcNow.AddHours(49)));
        Assert.Equal("cancelled", store.Get("old")!["status"]!.GetValue<string>());
        Assert.Equal("blocked", store.Get("old-blocked")!["status"]!.GetValue<string>());
        Assert.Equal("open", store.Get("commit")!["status"]!.GetValue<string>());
    }

    [Fact]
    public void AnAnsweredPromptIsCapturedAsCompleted()
    {
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Path, "seat");
        var first = User("first question", 1);
        var second = User("second question", 3);
        NotebookContext.Capture(store, [first]);
        Assert.Equal("open", store.Get(NotebookContext.SourceKey(first))!["status"]!.GetValue<string>());
        NotebookContext.Capture(store, [first, Fakes.Text("answer"), second, Fakes.ToolCalls()]);
        Assert.Equal("completed", store.Get(NotebookContext.SourceKey(first))!["status"]!.GetValue<string>());
        Assert.Equal("open", store.Get(NotebookContext.SourceKey(second))!["status"]!.GetValue<string>()); // a tool call is not an answer
    }

    // ------------------------------------------------------------------ <notebook-state>

    private static int Count(IReadOnlyList<Message> prepared, string tag) =>
        prepared.OfType<UserMessage>().Count(m => m.Text.StartsWith(tag, StringComparison.Ordinal));

    [Fact]
    public void AnUnchangedStateIsNotResentAndStaysAtItsPosition()
    {
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Path, "seat");
        store.Put("c1", "commitment", "ship it", "t", "open", 0);
        var messages = new List<Message> { User("go", 1) };
        var first = Prepare(store, messages);
        var state = Assert.IsType<UserMessage>(first[^1]);
        Assert.StartsWith("<notebook-state>", state.Text);

        messages.Add(Fakes.ToolCalls()); // a tool round, no new prompt: the notebook did not change
        var second = Prepare(store, messages);
        Assert.Equal(first, second.Take(first.Count)); // the same prefix, instance for instance
        Assert.Equal(messages.Count + 1, second.Count); // and nothing new
        Assert.Equal(1, Count(second, "<notebook-state"));
    }

    [Fact]
    public void AChangeSendsOnlyTheChangedLinesAndACompactionSendsTheFullStateAgain()
    {
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Path, "seat");
        store.Put("c1", "commitment", "ship it", "t", "open", 0);
        store.Put("c2", "commitment", "test it", "t", "open", 0);
        var messages = new List<Message> { User("go", 1) };
        var first = Prepare(store, messages);
        store.Transition("c1", "completed");
        store.Put("c3", "check", "check the deploy", "t", "open", 0);
        messages.Add(Fakes.Text("working"));
        var second = Prepare(store, messages);
        Assert.Equal(first, second.Take(first.Count));
        var update = Assert.IsType<UserMessage>(second[^1]).Text;
        Assert.StartsWith("<notebook-state-update>", update);
        Assert.Contains("c3 r1 [check/open] check the deploy", update);
        Assert.Contains("No longer open (completed, cancelled or superseded): c1", update);
        Assert.DoesNotContain("test it", update);

        var compacted = new List<Message> { new CompactionSummaryMessage("summary", 1000), User("continue", 5) };
        var third = Prepare(store, compacted);
        Assert.Equal(1, Count(third, "<notebook-state>"));
        Assert.Equal(0, Count(third, "<notebook-state-update>"));
        Assert.Contains("test it", third.OfType<UserMessage>().Single(m => m.Text.StartsWith("<notebook-state>")).Text);
    }

    [Fact]
    public void ManyUpdatesArePinnedAgainAsOneFullState()
    {
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Path, "seat");
        var messages = new List<Message> { User("go", 0) };
        Prepare(store, messages);
        for (var i = 1; i <= NotebookContext.MaxUpdates + 1; i++)
        {
            store.Put("c" + i, "commitment", "step " + i, "t", "open", 0);
            messages.Add(Fakes.Text("ok " + i));
            var prepared = Prepare(store, messages);
            if (i <= NotebookContext.MaxUpdates) Assert.Equal(i, Count(prepared, "<notebook-state-update>"));
            else
            {
                Assert.Equal(0, Count(prepared, "<notebook-state-update>"));
                Assert.Equal(1, Count(prepared, "<notebook-state>"));
                Assert.StartsWith("<notebook-state>", Assert.IsType<UserMessage>(prepared[^1]).Text);
            }
        }
    }

    [Fact]
    public void CommitmentsChecksAndNotesComeBeforeSourcesAndOnlyTheNewestSourcesAreListed()
    {
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Path, "seat");
        for (var i = 0; i < 20; i++) store.Put("source-" + i.ToString("D2"), "source", "said " + i, "", "open", 0);
        store.Put("commit", "commitment", "ship", "t", "open", 0);
        store.Put("check", "check", "verify", "t", "open", 0);
        store.Write("note", "long body", "Why the cache breaks");
        var briefing = store.Briefing();
        Assert.True(briefing.IndexOf("commit r1", StringComparison.Ordinal) < briefing.IndexOf("note r1", StringComparison.Ordinal));
        Assert.True(briefing.IndexOf("check r1", StringComparison.Ordinal) < briefing.IndexOf("note r1", StringComparison.Ordinal));
        Assert.True(briefing.IndexOf("note r1 [note] Why the cache breaks", StringComparison.Ordinal) < briefing.IndexOf("source-19 r1", StringComparison.Ordinal));
        Assert.Equal(NotebookStore.BriefingSources, System.Text.RegularExpressions.Regex.Matches(briefing, @"source-\d\d r1").Count);
        Assert.Contains("+10 older open records not listed", briefing);
        Assert.DoesNotContain("pending sync", briefing); // content only: sync state must not change the briefing
    }

    [Fact]
    public void UnderPressureSourcesGiveWayBeforeNotes()
    {
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Path, "seat");
        store.Put("source-a", "source", "said", "", "open", 0);
        store.Write("note-a", "body", "A note");
        var briefing = store.Briefing(NotebookStore.BriefingHeader.Length + 120 + 110 + 30);
        Assert.Contains("note-a r1", briefing);
        Assert.DoesNotContain("source-a r1", briefing);
    }

    // ------------------------------------------------------------------ recall

    [Fact]
    public void ANewInboxTaskRecallsTheRelevantNotesOnce()
    {
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Path, "seat");
        store.Write("cache-finding", "The prompt cache broke because the briefing moved each request.", "Prompt cache breaks", ["cache"]);
        store.Write("deploy-notes", "publish.sh verifies sha256 on the target", "Deploy path");
        for (var i = 0; i < 10; i++) store.Write("filler-" + i, "unrelated text about gardens " + i + " " + new string('z', 900), "Garden " + i);
        // A large enough request that the combined budget (15%) leaves room for the recall block (2,000 chars
        // default) on top of the pinned briefing (12,000 chars default).
        var context = new string('a', 100000);
        var messages = new List<Message> { User(context, 0), User("[inbox from @peer in pi-sharp, message 9001, mention] why does the prompt cache break?", 1) };
        var first = Prepare(store, messages);
        var recalled = first.OfType<UserMessage>().Single(m => m.Text.StartsWith("<recalled-notes>")).Text;
        Assert.Contains("cache-finding", recalled);
        Assert.DoesNotContain("deploy-notes", recalled);
        Assert.True(recalled.Length <= NotebookContext.RecallCharacters, recalled.Length.ToString());
        Assert.Same(messages[1], first[1]);
        Assert.StartsWith("<recalled-notes>", Assert.IsType<UserMessage>(first[2]).Text); // right after the task

        messages.Add(Fakes.ToolCalls());
        var second = Prepare(store, messages);
        Assert.Equal(1, Count(second, "<recalled-notes>")); // kept in place, not repeated
        Assert.Equal(first, second.Take(first.Count));
    }

    [Fact]
    public void ACompactionRecallsNotesRightAfterTheSummary()
    {
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Path, "seat");
        store.Write("schema-decision", "Title and tags ride in the jsonb body; no server migration.", "Notebook schema", kind: "decision");
        var context = new string('a', 100000);
        var messages = new List<Message> { new CompactionSummaryMessage("We were changing the notebook schema for title and tags.", 5000), User(context, 0), User("continue", 2) };
        var prepared = Prepare(store, messages);
        Assert.IsType<CompactionSummaryMessage>(prepared[0]);
        var recalled = Assert.IsType<UserMessage>(prepared[1]).Text;
        Assert.StartsWith("<recalled-notes>", recalled);
        Assert.Contains("schema-decision [decision] Notebook schema", recalled);
    }

    [Fact]
    public void RecallIsCappedAtFiveNotesAndTwoThousandCharacters()
    {
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Path, "seat");
        for (var i = 0; i < 12; i++) store.Write("router-" + i, "router config " + new string('r', 3000), "Router note " + i, ["router"]);
        var block = NotebookContext.RecalledNotes(store, "router config")!;
        Assert.True(block.Length <= NotebookContext.RecallCharacters);
        Assert.InRange(block.Split('\n').Count(l => l.StartsWith("- router-")), 1, NotebookContext.RecallCount);
    }

    // ------------------------------------------------------------------ prompts

    [Fact]
    public void TheSavePromptNamesTheOpAndAsksForConclusions()
    {
        Assert.Contains("notebook op=write id=", CompactionHooks.DefaultSavePrompt);
        Assert.Contains("Conclusions, not transcripts", CompactionHooks.DefaultSavePrompt);
    }

    [Fact]
    public void SeatsAreToldTheNotebookIsTheirWorkingMemory()
    {
        const string guideline = "keep working memory in the notebook (op=write), not in .md files; repo files are for code and docs the product needs";
        Assert.Contains(guideline, SystemPrompt.Build(new SystemPromptOptions { NotebookMemory = true }), StringComparison.OrdinalIgnoreCase);
        Assert.Contains(guideline, SystemPrompt.Build(new SystemPromptOptions { CustomPrompt = "seat", SelectedTools = ["notebook"] }), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(guideline, SystemPrompt.Build(new SystemPromptOptions()), StringComparison.OrdinalIgnoreCase);
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Path, "seat");
        Assert.Contains(guideline, new NotebookTool(store).Description, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------ import

    [Fact]
    public void ImportIsIdempotentAndKeysByRelativePath()
    {
        using var tmp = new TempWorkspace();
        var repo = Path.Combine(tmp.Path, "repo");
        Directory.CreateDirectory(Path.Combine(repo, "work", "p3"));
        File.WriteAllText(Path.Combine(repo, "work", "plan.md"), "# The plan\n\nstep one");
        File.WriteAllText(Path.Combine(repo, "work", "p3", "notes with space.md"), "no heading");
        File.WriteAllText(Path.Combine(repo, "work", "ignore.txt"), "not markdown");
        using var store = new NotebookStore(Path.Combine(tmp.Path, "nb"), "seat");

        var first = NotebookImport.Run(store, Path.Combine(repo, "work"), repo);
        Assert.Equal(2, first.Imported);
        var plan = store.Get("file:work:plan.md")!;
        Assert.Equal("The plan", plan["title"]!.GetValue<string>());
        Assert.Equal("[\"imported\"]", plan["tags"]!.ToJsonString());
        Assert.Equal("note", plan["kind"]!.GetValue<string>());
        Assert.Equal("notes with space.md", store.Get("file:work:p3:notes_with_space.md")!["title"]!.GetValue<string>());

        var pending = store.PendingCount;
        var second = NotebookImport.Run(store, Path.Combine(repo, "work"), repo);
        Assert.Equal((0, 0, 2), (second.Imported, second.Updated, second.Unchanged));
        Assert.Equal(pending, store.PendingCount);

        File.AppendAllText(Path.Combine(repo, "work", "plan.md"), "\nstep two");
        Assert.Equal(1, NotebookImport.Run(store, Path.Combine(repo, "work"), repo).Updated);
        Assert.Equal(2, store.Get("file:work:plan.md")!["revision"]!.GetValue<long>());
        Assert.Contains(store.Search("step two", null, null, 10, out _), h => h.Record["id"]!.GetValue<string>() == "file:work:plan.md");
    }

    [Fact]
    public void ImportKeysStayValidForLongPaths()
    {
        var key = NotebookImport.Key("work/" + new string('a', 300) + ".md");
        Assert.True(key.Length <= 160);
        Assert.NotEqual(key, NotebookImport.Key("work/" + new string('a', 301) + ".md"));
    }

    [Fact]
    public void TheImportCommandWritesAsTheJournalOwnerAndRefusesARunningSeat()
    {
        using var tmp = new TempWorkspace();
        var notebookDir = Path.Combine(tmp.Path, "nb");
        Directory.CreateDirectory(Path.Combine(tmp.Path, "work"));
        File.WriteAllText(Path.Combine(tmp.Path, "work", "a.md"), "# A\nbody");
        using (var seat = new NotebookStore(notebookDir, "coder-06-pi3"))
        {
            seat.Write("existing", "x");
            var busy = new StringWriter();
            Assert.Equal(1, NotebookCommand.Run(["notebook", "import", "work", "--notebook", notebookDir], new StringWriter(), busy, tmp.Path));
            Assert.Contains("stop the seat", busy.ToString());
        }
        var output = new StringWriter();
        var error = new StringWriter();
        Assert.Equal(0, NotebookCommand.Run(["notebook", "import", "work", "--notebook", notebookDir], output, error, tmp.Path));
        Assert.Contains("1 new", output.ToString());
        Assert.Equal(0, NotebookCommand.Run(["notebook", "import", "work", "--notebook", notebookDir], output, error, tmp.Path));
        Assert.Contains("0 new, 0 updated, 1 unchanged", output.ToString());
        using var reopened = new NotebookStore(notebookDir, "coder-06-pi3");
        Assert.Equal("A", reopened.Get("file:work:a.md")!["title"]!.GetValue<string>());
        Assert.Equal(2, NotebookCommand.Run(["notebook"], new StringWriter(), new StringWriter(), tmp.Path));
    }
}
