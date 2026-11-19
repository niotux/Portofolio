using System.Diagnostics;
using PiSharp.Core.Compaction;
using PiSharp.Core.Messages;
using PiSharp.Core.Session;
using PiSharp.Tests.Agent;
using PiSharp.Tests.Tools;
using static PiSharp.Tests.Agent.Fakes;

namespace PiSharp.Tests.Compaction;

public class CompactionTemplateTests
{
    private static readonly string[] Headings =
    [
        "## Task", "## Done", "## Open tasks / commitments", "## Decisions", "## Failed attempts", "## Pointers", "## Next step",
    ];

    [Fact]
    public void BothSummaryPromptsUseTheFixedHeadingsInOrderAndTheRules()
    {
        foreach (var prompt in new[] { CompactionPrompts.Initial, CompactionPrompts.Update })
        {
            var positions = Headings.Select(h => prompt.IndexOf("\n" + h + "\n", StringComparison.Ordinal)).ToList();
            Assert.All(positions, p => Assert.True(p >= 0));
            Assert.Equal(positions.Order(), positions);
            Assert.Contains("VERBATIM", prompt);
            Assert.Contains("message id", prompt);
            Assert.Contains("Pointers, not content", prompt);
            Assert.Contains("tried X, failed because Y", prompt);
            Assert.Contains("about 2000 tokens", prompt);
            Assert.Contains("where partial work is written", prompt);
        }
        Assert.Contains("Never drop an open task", CompactionPrompts.Update);
    }

    [Fact]
    public async Task ASecondCompactionUpdatesThePreviousSummaryInsteadOfSummarizingIt()
    {
        var session = SessionManager.InMemory("/tmp");
        void Turn(string text)
        {
            session.AppendMessage(new UserMessage(text + " " + new string('x', 40)));
            session.AppendMessage(new AssistantMessage { Content = [new TextContent("ok " + text)], Model = "m", StopReason = StopReason.Stop });
        }
        Turn("one");
        Turn("two");
        Turn("three");
        var settings = new CompactionSettings(KeepRecentTokens: 8);
        var first = Compactor.Prepare(session.Entries, settings)!;
        Assert.Null(first.PreviousSummary);
        var stream = new ScriptedStream().Then(Text("## Task\nFIRST SUMMARY")).Then(Text("## Task\nFIRST SUMMARY")).Then(Text("## Task\nSECOND SUMMARY")).Then(Text("## Task\nSECOND SUMMARY"));
        var firstResult = await Compactor.CompactAsync(first, Model, stream.Stream);
        session.AppendCompaction(firstResult.Summary, firstResult.FirstKeptEntryId, firstResult.TokensBefore);
        session.AppendCompactionLog(new CompactionLogEntry { Id = "", Trigger = "between-turns", SaveBeforeCompact = "off" });
        Turn("four");
        Turn("five");

        var second = Compactor.Prepare(session.Entries, settings)!;
        Assert.StartsWith("## Task\nFIRST SUMMARY", second.PreviousSummary);
        await Compactor.CompactAsync(second, Model, stream.Stream);

        var prompt = Assert.Single(stream.Contexts.Select(c => Assert.IsType<UserMessage>(c.Messages[0]).Text), t => t.Contains("<previous-summary>", StringComparison.Ordinal));
        Assert.Contains("<previous-summary>\n## Task\nFIRST SUMMARY", prompt);
        Assert.Contains(CompactionPrompts.Update, prompt);
        // The old summary is the base to update, not part of the conversation being summarized.
        var conversation = prompt[..prompt.IndexOf("</conversation>", StringComparison.Ordinal)];
        Assert.DoesNotContain("FIRST SUMMARY", conversation);
        Assert.Contains("four", conversation);
    }

    [Fact]
    public void LiveFactsFormatTheTimeAndTheGitState()
    {
        var now = new DateTimeOffset(2026, 9, 18, 14, 5, 9, TimeSpan.FromHours(3));
        Assert.Equal("[live facts after compaction]\nUTC now: 2026-09-18T11:05:09Z", LiveFacts.Format(now, null));

        var clean = LiveFacts.Format(now, new GitFacts("agent-maker/x", "abc1234", [], 0));
        Assert.EndsWith("git: branch agent-maker/x @ abc1234\ngit status: clean", clean);

        var lines = Enumerable.Range(0, LiveFacts.MaxStatusLines).Select(i => $" M f{i}.cs").ToList();
        var dirty = LiveFacts.Format(now, new GitFacts(null, null, lines, LiveFacts.MaxStatusLines + 7));
        Assert.Contains("git: branch (detached) @ (no commits)\n", dirty);
        Assert.Contains($"git status --porcelain ({LiveFacts.MaxStatusLines + 7} entries):\n   M f0.cs\n", dirty);
        Assert.EndsWith("  ... and 7 more", dirty);
    }

    [Fact]
    public void ReadGitIsNullOutsideARepositoryAndNeverThrows()
    {
        using var tmp = new TempWorkspace();
        // A directory that does not exist, and one that is (normally) outside any work tree.
        Assert.Null(LiveFacts.ReadGit(Path.Combine(tmp.Path, "missing")));
        if (LiveFacts.ReadGit(tmp.Path) is { } git)
            Assert.NotNull(git.Status); // the temp dir happens to sit inside a work tree: still well-formed
        Assert.StartsWith("[live facts after compaction]\nUTC now: ", LiveFacts.Read(Path.Combine(tmp.Path, "missing")));
    }

    [Fact]
    public void ReadGitReportsBranchAndCappedStatusOfARepository()
    {
        using var tmp = new TempWorkspace();
        if (!Git(tmp.Path, "init -q -b topic")) return; // no git on this machine: nothing to read
        for (var i = 0; i < LiveFacts.MaxStatusLines + 3; i++) File.WriteAllText(Path.Combine(tmp.Path, $"f{i:D2}.txt"), "x");

        var git = LiveFacts.ReadGit(tmp.Path);

        Assert.NotNull(git);
        Assert.Equal("topic", git.Branch);
        Assert.Null(git.Head); // no commits yet
        Assert.Equal(LiveFacts.MaxStatusLines + 3, git.StatusTotal);
        Assert.Equal(LiveFacts.MaxStatusLines, git.Status.Count);
        Assert.Equal("?? f00.txt", git.Status[0]);
    }

    private static bool Git(string cwd, string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("git", args)
            {
                WorkingDirectory = cwd,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });
            if (p is null) return false;
            p.WaitForExit(10_000);
            return p.HasExited && p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
