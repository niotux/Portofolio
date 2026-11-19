using System.Text.Json.Nodes;
using PiSharp.Core.Agent;
using PiSharp.Core.Compaction;
using PiSharp.Core.Messages;
using PiSharp.Core.Notebook;
using PiSharp.Tests.Agent;
using PiSharp.Tests.Tools;

namespace PiSharp.Tests.Notebook;

public class OptimizationTests
{
    [Fact]
    public async Task BudgetSurvivesRestartAndIncludesSummaryCalls()
    {
        using var tmp = new TempWorkspace();
        var inner = new ScriptedStream().Then(Fakes.Text("summary") with { Usage = new Usage(Output: 10) });
        using (var store = new NotebookStore(tmp.Path, "seat"))
        {
            var stream = new TaskBudget(store, () => "task1", 1, 100, 10).Wrap(inner.Stream);
            await Compactor.GenerateSummaryAsync([new UserMessage("Work")], Fakes.Model, stream, 100, 20000);
        }
        using var resumed = new NotebookStore(tmp.Path, "seat");
        var budgeted = new TaskBudget(resumed, () => "task1", 1, 100, 10).Wrap(inner.Stream);
        var ctx = new AgentContext { SystemPrompt = "", Tools = [] };
        var result = await Fakes.Collect(AgentLoop.Run([new UserMessage("continue")], ctx, Fakes.Config(), budgeted));
        Assert.Equal("budget_exhausted", Assert.Single(result.OfType<AgentEnd>()).Outcome);
        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task LargeHistorySummariesUseBoundedRequests()
    {
        var inner = new ScriptedStream();
        for (var i = 0; i < 20; i++) inner.Then(Fakes.Text("Keep source-1 pending and source-2 cancelled"));
        // A 20k target: no request reads more than ~20k tokens (80,000 characters) of history.
        await Compactor.GenerateSummaryAsync([new UserMessage(new string('x', 320000))], Fakes.Model, inner.Stream, 4000, 20000);
        Assert.True(inner.Calls > 1);
        Assert.All(inner.Contexts, c => Assert.True(c.Messages.OfType<UserMessage>().Sum(m => m.Text.Length) <= 80000));
    }

    [Fact]
    public void ArtifactBudgetRetainsOriginalAndPreservesImageModality()
    {
        using var tmp = new TempWorkspace();
        var store = new ArtifactStore(tmp.Path);
        var raw = new AgentToolResult([new TextContent(new string('x', 25000)), new ImageContent("aGVsbG8=", "image/png")]);
        var bounded = store.Bound(raw);
        Assert.True(bounded.Text.Length < 8000);
        Assert.Single(bounded.Content.OfType<ImageContent>());
        var id = store.Save(raw.Content);
        Assert.Equal(25000, store.Read(id)[0]!["text"]!.GetValue<string>().Length);
        Assert.Throws<ArgumentException>(() => store.Read("../../other-agent"));
    }

    [Fact]
    public void ArtifactQuotaPrunesTheOldestHalfInsteadOfRefusing()
    {
        using var tmp = new TempWorkspace();
        using var notebook = new NotebookStore(Path.Combine(tmp.Path, "nb"), "seat");
        var dir = Path.Combine(tmp.Path, "artifacts");
        var filling = new ArtifactStore(dir) { Notebook = notebook };
        // Eight ~430-byte artifacts with distinct, explicit ages: 0 oldest .. 7 newest.
        var ids = Enumerable.Range(0, 8).Select(i => filling.Save([new TextContent($"{i}:" + new string('x', 400))])).ToList();
        for (var i = 0; i < ids.Count; i++)
            File.SetLastWriteTimeUtc(Path.Combine(dir, ids[i] + ".json"), new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc).AddMinutes(i));
        filling.MarkUploaded(ids[0]);

        // A quota with room for less than one more: the ninth crosses it, so the four oldest go, the four newest stay.
        var used = new DirectoryInfo(dir).EnumerateFiles().Sum(f => f.Length);
        var store = new ArtifactStore(dir, quotaBytes: used + 100) { Notebook = notebook };
        var ninth = store.Save([new TextContent("8:" + new string('y', 400))]);

        for (var i = 0; i < 4; i++) Assert.False(File.Exists(Path.Combine(dir, ids[i] + ".json")), $"artifact {i} should be pruned");
        Assert.False(File.Exists(Path.Combine(dir, ids[0] + ".json.committed")));
        for (var i = 4; i < 8; i++) Assert.Equal($"{i}:", store.Read(ids[i])[0]!["text"]!.GetValue<string>()[..2]);
        Assert.StartsWith("8:", store.Read(ninth)[0]!["text"]!.GetValue<string>());

        var log = File.ReadAllLines(Path.Combine(dir, ArtifactStore.PruneLogName));
        var entry = JsonNode.Parse(Assert.Single(log))!;
        Assert.Equal(4, entry["pruned"]!.GetValue<int>());
        Assert.Equal(4, entry["kept"]!.GetValue<int>());
        Assert.Equal([ids[0], ids[1], ids[2], ids[3]], entry["ids"]!.AsArray().Select(n => n!.GetValue<string>()));

        var pruned = notebook.Get("artifact-" + ids[0])!;
        Assert.Equal("superseded", pruned["status"]!.GetValue<string>());
        Assert.Contains("the KB copy remains", pruned["text"]!.GetValue<string>());
        Assert.Equal("open", notebook.Get("artifact-" + ids[5])!["status"]!.GetValue<string>());
    }

    [Fact]
    public void ArtifactLargerThanTheWholeQuotaStillRefusesAfterPruning()
    {
        using var tmp = new TempWorkspace();
        var store = new ArtifactStore(tmp.Path, quotaBytes: 1000);
        store.Save([new TextContent("small")]);
        var error = Assert.Throws<IOException>(() => store.Save([new TextContent(new string('z', 2000))]));
        Assert.Contains("even after pruning", error.Message);
    }

    [Fact]
    public async Task ArtifactPagesFitTheWorkingContextAndReachTheEnd()
    {
        // 2026-10-29: a page was 8000 characters plus a trailing offset line, always over the 8000 a result keeps, so
        // the working context stored it again and cut the offset line off; two demo seats followed the new artifact
        // pointers for an hour. A page now fits, leads with where to read on, and the pages end.
        using var tmp = new TempWorkspace();
        var store = new ArtifactStore(tmp.Path);
        var original = string.Concat(Enumerable.Range(0, 3000).Select(i => $"line {i:D5}\n"));
        var id = store.Save([new TextContent(original)]);
        var tool = new ArtifactTool(store);
        var read = new System.Text.StringBuilder();
        var offset = 0;
        var calls = 0;
        while (offset < original.Length)
        {
            var page = (await tool.ExecuteAsync("c" + calls, new JsonObject { ["id"] = id, ["offset"] = offset }, null, default)).Text;
            Assert.True(page.Length <= ArtifactTool.PageCharacters, $"page of {page.Length} characters");
            Assert.StartsWith($"[artifact {id}; offset={offset}; next=", page);
            var body = page[(page.IndexOf('\n') + 1)..];
            read.Append(body);
            offset += body.Length;
            Assert.True(++calls < 10);
        }
        Assert.Equal(original, read.ToString());

        // The working context leaves a page as it is: nothing new is stored.
        var before = Directory.GetFiles(tmp.Path, "*.json").Length;
        var first = (await tool.ExecuteAsync("c", new JsonObject { ["id"] = id }, null, default)).Text;
        var history = store.PrepareMessages([new UserMessage("read it"), ToolResult("artifact", first)]);
        Assert.Equal(first, TextOf(history[1]));
        Assert.Equal(before, Directory.GetFiles(tmp.Path, "*.json").Length);
    }

    [Fact]
    public async Task AnArtifactPageCutByTheBudgetPointsBackIntoTheSameArtifact()
    {
        using var tmp = new TempWorkspace();
        var store = new ArtifactStore(tmp.Path);
        var original = new string('a', 20000);
        var id = store.Save([new TextContent(original)]);
        var page = (await new ArtifactTool(store).ExecuteAsync("c", new JsonObject { ["id"] = id, ["offset"] = 100 }, null, default)).Text;
        // The budget is spent newest first: two large results after it in the same batch use up its 16000 characters.
        var before = Directory.GetFiles(tmp.Path, "*.json").Length;
        var history = store.PrepareMessages([new UserMessage("go"), ToolResult("artifact", page),
            ToolResult("bash", new string('b', 7900)), ToolResult("bash", new string('c', 7900))]);
        var cut = TextOf(history[1]);
        Assert.True(cut.Length < page.Length);
        Assert.StartsWith($"[artifact {id}; offset=100; next=", cut);
        Assert.Contains($"read on with artifact {id} offset=", cut);
        Assert.DoesNotContain("Original content: artifact", cut);
        var resume = int.Parse(System.Text.RegularExpressions.Regex.Match(cut, @"read on with artifact \w+ offset=(\d+)").Groups[1].Value);
        var shown = cut[(cut.IndexOf('\n') + 1)..cut.LastIndexOf("\n[Cut by", StringComparison.Ordinal)];
        Assert.Equal(100 + shown.Length, resume);
        // The page was not stored again (the two bash results fit and stay whole).
        Assert.Equal(before, Directory.GetFiles(tmp.Path, "*.json").Length);
    }

    private static ToolResultMessage ToolResult(string tool, string text) =>
        new() { ToolCallId = "t-" + tool + text.Length, ToolName = tool, Content = [new TextContent(text)] };

    private static string TextOf(Message message) =>
        string.Concat(((ToolResultMessage)message).Content.OfType<TextContent>().Select(t => t.Text));

    [Fact]
    public void ImagesLeaveByAgeSoANewImageNeverRewritesDeepHistory()
    {
        using var tmp = new TempWorkspace();
        var store = new ArtifactStore(tmp.Path);
        UserMessage Shot(string name) => new([new TextContent(name), new ImageContent("aGVsbG8=", "image/png")]);
        List<Message> history = [Shot("a"), Fakes.Text("saw a"), Fakes.Text("more"), Fakes.Text("more"), Shot("b"), Fakes.Text("saw b")];
        var before = store.PrepareMessages(history);
        // "a" is three assistant turns old: dropped. "b" is one turn old: kept.
        Assert.Empty(((UserMessage)before[0]).Content.OfType<ImageContent>());
        Assert.Single(((UserMessage)before[4]).Content.OfType<ImageContent>());

        // Two more images arrive and take the two places; only the recent "b" may change, nothing before it.
        history.AddRange([Shot("c"), Shot("d")]);
        var after = store.PrepareMessages(history);
        for (var i = 0; i < 4; i++) Assert.Equal(Text(before[i]), Text(after[i]));
        static string Text(Message m) => m is UserMessage u ? string.Concat(u.Content.OfType<TextContent>().Select(t => t.Text)) + u.Content.OfType<ImageContent>().Count() : m.ToString()!;
    }

    [Fact]
    public async Task DiscoveryCannotActivateExcludedToolsAndOrderingStaysStable()
    {
        var permitted = new DelegateTool { Name = "kb__search", Description = "Search company knowledge" };
        IReadOnlyList<IAgentTool> tools = [permitted];
        var discovery = new ToolDiscovery(() => tools);
        tools = [permitted, discovery];
        Assert.Single(ToolCatalog.Present(tools, Fakes.Model));
        await discovery.ExecuteAsync("s", new JsonObject { ["op"] = "search", ["query"] = "knowledge" }, null, default);
        Assert.Single(ToolCatalog.Present(tools, Fakes.Model));
        discovery.NoteUse("kb__search");
        discovery.Rebase();
        Assert.Equal(new[] { "kb__search", "toolkit" }, ToolCatalog.Present(tools, Fakes.Model).Select(t => t.Name));
        tools = [discovery]; // disconnected/removed by the live registry
        var gone = await discovery.ExecuteAsync("s2", new JsonObject { ["op"] = "search", ["query"] = "knowledge" }, null, default);
        Assert.Contains("No tool matches", string.Concat(gone.Content.OfType<TextContent>().Select(t => t.Text)));
        Assert.Single(ToolCatalog.Present(tools, Fakes.Model));
    }

    [Fact]
    public void MandatoryReferencesAreNeverSilentlyLost()
    {
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Path, "seat");
        for (var i = 0; i < 30; i++) store.Put("commitment-" + i, "commitment", new string('x', 3000), "task", "open", 0);
        var briefing = store.Briefing();
        for (var i = 0; i < 30; i++) Assert.Contains("commitment-" + i + " r1", briefing);
        var tight = store.Briefing(400);
        Assert.True(tight.Length <= 400);
        Assert.Matches(@"\+\d+ older open records not listed", tight);
    }

    [Fact]
    public void OverBudgetOldSourcesGiveWayBeforeCommitments()
    {
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Path, "seat");
        for (var i = 0; i < 300; i++) store.Put("source-" + i.ToString("D3"), "source", "Service start; reply NO_REPLY", "", "open", 0);
        store.Put("commitment-keep", "commitment", "ship the fix", "task", "open", 0);
        var briefing = store.Briefing();
        Assert.True(briefing.Length <= 12000);
        Assert.Contains("commitment-keep r1", briefing);
        Assert.Contains("source-299 r1", briefing);
        Assert.DoesNotContain("source-000 r1", briefing);
        Assert.Contains("older open records not listed", briefing);
    }
}
