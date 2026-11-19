using System.Text.Json.Nodes;
using PiSharp.Core.Compaction;
using PiSharp.Core.Messages;
using PiSharp.Core.Session;
using PiSharp.Tests.Agent;
using static PiSharp.Tests.Agent.Fakes;

namespace PiSharp.Tests.Compaction;

public class CompactorTests
{
    private static SessionManager History(params string[] userTexts)
    {
        var session = SessionManager.InMemory("/tmp");
        foreach (var text in userTexts)
        {
            session.AppendMessage(new UserMessage(text));
            session.AppendMessage(new AssistantMessage
            {
                Content = [new TextContent("ok " + text)],
                Model = "m",
                StopReason = StopReason.Stop,
                Usage = new Usage(Input: 10, Output: 5, TotalTokens: 15),
            });
        }
        return session;
    }

    [Fact]
    public void KeepRecentFor_SizesTheTailSoTheContextLandsAtTheTarget()
    {
        var seat = new CompactionSettings(ReserveTokens: 8192, KeepRecentTokens: 20000, TriggerTokens: 50000, TargetTokens: 25000);

        // 25k total = 8k fixed + 3k summary allowance + 14k kept.
        Assert.Equal(14000, seat.KeepRecentFor(8000, Model));
        // A fixed part that eats the target still keeps a tail as large as the summary (no 4,000 in code since 2026-10-20).
        Assert.Equal(seat.SummaryAllowance, seat.KeepRecentFor(30000, Model));
        // The target, not KeepRecentTokens, sizes the tail: 180k down to 90k keeps ~70k beside 11k fixed and the 9k
        // summary allowance (it kept 20k, so seats landed near 30k, 2026-10-20).
        Assert.Equal(22000, seat.KeepRecentFor(0, Model));
        var tab = new CompactionSettings(ReserveTokens: 32000, KeepRecentTokens: 20000, TriggerTokens: 180000, TargetTokens: 90000);
        Assert.Equal(9000, tab.SummaryAllowance);
        Assert.Equal(70000, tab.KeepRecentFor(11000, Model));
        // Without a target the tail is KeepRecentTokens, as before.
        Assert.Equal(20000, new CompactionSettings(KeepRecentTokens: 20000).KeepRecentFor(8000, Model));
    }

    /// <summary>digital-scribe, 2026-10-20: "let's not hardcode anymore stuff in pi-sharp regarding token budgets". A
    /// settings.json without keepRecentTokens or reserveTokens (seat installers no longer write them) takes them from the
    /// target and the model, never 20,000/16,384 from code.</summary>
    [Fact]
    public void WithoutKeepOrReserveTheTailFollowsTheTargetAndTheReserveIsTheModelsOutput()
    {
        var model = Model with { ContextWindow = 200000, MaxOutputTokens = 32000 };
        var tab = new CompactionSettings(TriggerTokens: 180000, TargetTokens: 90000);
        Assert.Equal(32000, tab.ReserveFor(model));
        Assert.Equal(70000, tab.KeepRecentFor(11000, model));
        // A trigger without a target compacts down to half of it (2026-09-26: "it should always compact at 50%").
        var trigger = new CompactionSettings(TriggerTokens: 180000);
        Assert.Equal(90000, trigger.TargetFor(model));
        Assert.Equal(90000 - 11000 - 9000, trigger.KeepRecentFor(11000, model));
        // Nothing configured: half of what the window holds beside the model's output.
        Assert.Equal((200000 - 32000) / 2, CompactionSettings.Default.TargetFor(model));
        // A model with no output cap and no reserve in settings.json is an error, not a guess.
        var unset = new PiSharp.Core.Models.ModelConfig { Id = "bare", BaseUrl = "http://x/v1", ContextWindow = 100000 };
        Assert.Contains("maxOutputTokens", Assert.Throws<InvalidOperationException>(() => CompactionSettings.Default.ReserveFor(unset)).Message);
    }

    [Fact]
    public void ShouldCompact_WhenOverWindowMinusReserve()
    {
        var settings = new CompactionSettings(Enabled: true, ReserveTokens: 100, KeepRecentTokens: 50);
        var model = Model with { ContextWindow = 200 };
        Assert.False(Compactor.ShouldCompact(50, model, settings));
        Assert.True(Compactor.ShouldCompact(101, model, settings));
        Assert.False(Compactor.ShouldCompact(999, model, settings with { Enabled = false }));
    }

    [Fact]
    public void FindCutPoint_KeepsRecentTokensAtTurnBoundary()
    {
        var session = History("aaaa", "bbbb", "cccc", "dddd"); // 8 entries, user+assistant each
        var settingsKeep = 3; // tiny: walk back until budget hit
        var cut = Compactor.FindCutPoint(session.Entries, 0, session.Entries.Count, settingsKeep);
        Assert.True(cut.FirstKeptEntryIndex > 0);
        Assert.True(cut.FirstKeptEntryIndex < session.Entries.Count);
        var kept = SessionManager.EntryToMessages(session.Entries[cut.FirstKeptEntryIndex])[0];
        Assert.True(kept is UserMessage or AssistantMessage);
    }

    [Fact]
    public void FindCutPoint_NeverCutsAtToolResult()
    {
        var session = SessionManager.InMemory("/tmp");
        session.AppendMessage(new UserMessage("do it"));
        session.AppendMessage(new AssistantMessage
        {
            Content = [new ToolCallContent("c1", "read", new JsonObject { ["path"] = "a.txt" })],
            Model = "m",
            StopReason = StopReason.ToolUse,
        });
        session.AppendMessage(new ToolResultMessage
        {
            ToolCallId = "c1",
            ToolName = "read",
            Content = [new TextContent("file")],
        });
        var cut = Compactor.FindCutPoint(session.Entries, 0, session.Entries.Count, keepRecentTokens: 1);
        var kept = session.Entries[cut.FirstKeptEntryIndex];
        Assert.False(kept is SessionMessageEntry { Message: ToolResultMessage });
    }

    [Fact]
    public void Prepare_ReturnsNullWhenNothingToSummarize()
    {
        var session = SessionManager.InMemory("/tmp");
        session.AppendMessage(new UserMessage("only"));
        var prep = Compactor.Prepare(session.Entries, new CompactionSettings(KeepRecentTokens: 1_000_000));
        Assert.Null(prep);
    }

    [Fact]
    public void Prepare_SplitsHistoryFromKeptTail()
    {
        var session = History("one-message-here", "two-message-here", "three-message-here", "four-message-here");
        var prep = Compactor.Prepare(session.Entries, new CompactionSettings(KeepRecentTokens: 8));
        Assert.NotNull(prep);
        Assert.NotEmpty(prep!.FirstKeptEntryId);
        Assert.True(prep.MessagesToSummarize.Count > 0);
        Assert.DoesNotContain(prep.MessagesToSummarize, m => ReferenceEquals(m, ((SessionMessageEntry)session.Entries.First(e => e.Id == prep.FirstKeptEntryId)).Message));
    }

    [Fact]
    public void FileOps_ReadVsModified()
    {
        var ops = new FileOperations();
        FileOpTracker.ExtractFromMessage(new AssistantMessage
        {
            Content =
            [
                new ToolCallContent("1", "read", new JsonObject { ["path"] = "a.txt" }),
                new ToolCallContent("2", "read", new JsonObject { ["path"] = "b.txt" }),
                new ToolCallContent("3", "edit", new JsonObject { ["path"] = "b.txt" }),
                new ToolCallContent("4", "write", new JsonObject { ["path"] = "c.txt" }),
            ],
            Model = "m",
        }, ops);
        var (read, modified) = FileOpTracker.ComputeFileLists(ops);
        Assert.Equal(["a.txt"], read);
        Assert.Equal(["b.txt", "c.txt"], modified);
        var formatted = FileOpTracker.FormatFileOperations(read, modified);
        Assert.Contains("<read-files>", formatted);
        Assert.Contains("a.txt", formatted);
        Assert.Contains("<modified-files>", formatted);
    }

    [Fact]
    public void FileOps_ApplyPatchNamesItsFilesInThePatch()
    {
        var ops = new FileOperations();
        FileOpTracker.ExtractFromMessage(new AssistantMessage
        {
            Content =
            [
                new ToolCallContent("1", "read", new JsonObject { ["path"] = "a.txt" }),
                new ToolCallContent("2", "apply_patch", new JsonObject
                {
                    ["input"] = "*** Begin Patch\r\n*** Update File: a.txt\r\n*** Move to: moved.txt\r\n@@\r\n-x\r\n+y\r\n" +
                                "*** Add File: new.txt\r\n+hi\r\n*** Delete File: old.txt\r\n*** End Patch",
                }),
            ],
            Model = "m",
        }, ops);
        var (read, modified) = FileOpTracker.ComputeFileLists(ops);
        Assert.Empty(read);
        Assert.Equal(["a.txt", "moved.txt", "new.txt", "old.txt"], modified);
    }

    [Fact]
    public void SerializeConversation_TruncatesToolResults()
    {
        var huge = new string('x', Compactor.ToolResultMaxChars + 50);
        var text = Compactor.SerializeConversation(
        [
            new UserMessage("hello"),
            new AssistantMessage { Content = [new TextContent("hi"), new ThinkingContent("hmm")], Model = "m" },
            new ToolResultMessage { ToolCallId = "1", ToolName = "read", Content = [new TextContent(huge)] },
        ]);
        Assert.Contains("[User]: hello", text);
        Assert.Contains("[Assistant thinking]: hmm", text);
        Assert.Contains("[Assistant]: hi", text);
        Assert.Contains("more characters truncated", text);
        Assert.DoesNotContain(huge, text);
    }

    [Fact]
    public async Task CompactAsync_AppendsFileListsToSummary()
    {
        var session = History(new string('a', 40), new string('b', 40), new string('c', 40), new string('d', 40));
        session.AppendMessage(new UserMessage("read it"));
        session.AppendMessage(new AssistantMessage
        {
            Content = [new ToolCallContent("1", "read", new JsonObject { ["path"] = "src/A.cs" })],
            Model = "m",
            StopReason = StopReason.ToolUse,
        });
        session.AppendMessage(new ToolResultMessage { ToolCallId = "1", ToolName = "read", Content = [new TextContent("code")] });

        var prep = Compactor.Prepare(session.Entries, new CompactionSettings(KeepRecentTokens: 5));
        Assert.NotNull(prep);
        var stream = new ScriptedStream()
            .Then(Text("## Goal\nShip it"))
            .Then(Text("## Original Request\nread it"));
        var result = await Compactor.CompactAsync(prep!, Model, stream.Stream);
        Assert.Contains("## Goal", result.Summary);
        Assert.Equal(prep.FirstKeptEntryId, result.FirstKeptEntryId);
        Assert.True(stream.Calls >= 1);
    }

    /// <summary>digital-scribe, 2026-10-20: no token budgets hard-coded. A summary request reads up to the compaction
    /// target, as far as the model's window holds it beside the summary; a fixed 20,000-token cap chunked this history.</summary>
    [Fact]
    public async Task GenerateSummary_ReadsUpToTheTargetInOneRequest()
    {
        var stream = new ScriptedStream();
        for (var i = 0; i < 20; i++) stream.Then(Text("## Goal\nShip it"));
        var model = Model with { ContextWindow = 212000, MaxOutputTokens = 32000 };
        await Compactor.GenerateSummaryAsync([new UserMessage(new string('x', 200000))], model, stream.Stream, reserveTokens: 32000, targetTokens: 90000);
        Assert.Equal(1, stream.Calls);

        // A small window still bounds it: 32k less the summary's output leaves less than the history.
        var small = new ScriptedStream();
        for (var i = 0; i < 20; i++) small.Then(Text("## Goal\nShip it"));
        await Compactor.GenerateSummaryAsync([new UserMessage(new string('x', 200000))], Model, small.Stream, reserveTokens: 8000, targetTokens: 90000);
        Assert.True(small.Calls > 1);
        Assert.All(small.Contexts, c => Assert.True(TokenEstimate.EstimateTextTokens(c.SystemPrompt + ((UserMessage)c.Messages[0]).Text) + 6400 <= Model.ContextWindow));
    }

    [Fact]
    public async Task GenerateSummary_FailsOnLengthStop()
    {
        var stream = new ScriptedStream()
            .Then(Assistant(StopReason.Length, new TextContent("partial")))
            .Then(Assistant(StopReason.Length, new TextContent("still partial")));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Compactor.GenerateSummaryAsync([new UserMessage("x")], Model, stream.Stream, reserveTokens: 1000, targetTokens: 20000));
        Assert.Equal(2, stream.Calls);
    }

    [Fact]
    public async Task GenerateSummary_RetriesOnceWithTwiceTheCapAfterALengthStop()
    {
        var stream = new ScriptedStream()
            .Then(Assistant(StopReason.Length, new TextContent("partial")))
            .Then(Text("## Goal\nShip it"));
        var result = await Compactor.GenerateSummaryAsync([new UserMessage("x")], Model, stream.Stream, reserveTokens: 1000, targetTokens: 20000);
        Assert.Contains("## Goal", result.Text);
        Assert.Equal(stream.Options[0].MaxTokens * 2, stream.Options[1].MaxTokens);
    }

    [Theory]
    [InlineData(null, CompactionSettings.SummaryAllowanceTokens)]
    [InlineData(10000, CompactionSettings.SummaryAllowanceTokens)]
    [InlineData(65000, 6500)]
    [InlineData(500000, CompactionSettings.MaxSummaryAllowanceTokens)]
    public void SummaryAllowance_ScalesWithTheTarget(int? target, int expected) =>
        Assert.Equal(expected, new CompactionSettings(TargetTokens: target).SummaryAllowance);

    [Fact]
    public async Task GenerateSummary_FailsOnToolCall()
    {
        var stream = new ScriptedStream().Then(ToolCalls(Call("1", "read")));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Compactor.GenerateSummaryAsync([new UserMessage("x")], Model, stream.Stream, reserveTokens: 1000, targetTokens: 20000));
    }

    [Fact]
    public void GetSummarizationFailure_RejectsErrorAndLength()
    {
        Assert.Contains("failed", Compactor.GetSummarizationFailure(Error("boom"), "Summarization"));
        Assert.Contains("token cap", Compactor.GetSummarizationFailure(Assistant(StopReason.Length, new TextContent("x")), "Summarization"));
        Assert.Null(Compactor.GetSummarizationFailure(Text("ok"), "Summarization"));
    }
}
