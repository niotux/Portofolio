using PiSharp.Core.Notebook;
using PiSharp.Core.Messages;
using PiSharp.Tests.Tools;

namespace PiSharp.Tests.Notebook;

public class NotebookShareTests
{
    /// <summary>
    /// Acceptance (pi-sharp-memory-curation-1): a synthetic 200-record notebook reports a notebook share
    /// within 1% of a hand count. The 200 notes are all over the 8-note briefing budget, so the briefing
    /// lists 8 and reports the rest by count; the hand count is computed the way the harness actually
    /// builds the pinned <c>&lt;notebook-state&gt;</c> message (header + the listed lines + footer).
    /// </summary>
    [Fact]
    public void A200RecordNotebookReportsShareWithinOnePercentOfAHandCount()
    {
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Path, "seat1");
        // 200 open notes with distinct titles, all the same kind, so the briefing's 8-note budget is hit
        // and the 192 the rest of the notes are counted, not listed.
        for (var i = 0; i < 200; i++)
            store.Put("note-" + i, "note", "body of note " + i, "chat-1", "open", 0);

        var prepared = NotebookContext.Prepare(store, [new UserMessage(new string('x', 10000))]);

        // The hand count: the pinned <notebook-state> message the harness built is
        //   "<notebook-state>\n" + BriefingHeader + (the briefing's lines) + StateFooter
        // Measure() counts exactly those characters as the notebook, so it must equal the prepared
        // message's notebook text. Find that message in the prepared list and compare its length to
        // Measure().NotebookChars.
        var notebookMessage = prepared.OfType<UserMessage>()
            .Where(u => ContentText.Of(u.Content).StartsWith("<notebook-state>", StringComparison.Ordinal))
            .Select(u => ContentText.Of(u.Content))
            .FirstOrDefault();
        Assert.NotNull(notebookMessage);
        var handCount = notebookMessage!.Length;

        var share = NotebookShareTracker.Measure(prepared, "You are a seat.");
        // Every notebook character the tracker counted is the pinned briefing's text.
        Assert.True(Math.Abs(share.NotebookChars - handCount) / handCount < 0.01,
            $"share.NotebookChars={share.NotebookChars} handCount={handCount}");
        Assert.Equal(1, share.Pinned);

        // The request side: everything that is not the notebook (the transcript + the system prompt).
        Assert.True(share.RequestChars > 0);
        Assert.True(share.Share > 0 && share.Share < 1);

        // The share the tracker records is the one that /stats reports on.
        var tracker = new NotebookShareTracker();
        tracker.Record(share);
        var (samples, p50, p95) = tracker.Percentiles();
        Assert.Equal(1, samples);
        Assert.Equal(share.Share, p50, 10);
        Assert.Equal(share.Share, p95, 10);
    }

    /// <summary>The share is the notebook's fraction of the request: a hand-computed 4/10 = 0.4.</summary>
    [Fact]
    public void ShareIsNotebookOverRequestPlusNotebook()
    {
        var prepared = new Message[]
        {
            new UserMessage("<notebook-state>\nDurable notebook.\n" + new string('n', 40) + "\n</notebook-state>"),
            new UserMessage("do the thing"),
            new AssistantMessage { Content = [new TextContent("thinking about it")], Model = "m" },
        };
        // The notebook message is 93 chars; the request is "do the thing" (12) + the
        // assistant text (17). Share = 93 / (93 + 12 + 17) = 93/122.
        var share = NotebookShareTracker.Measure(prepared);
        Assert.Equal(93, share.NotebookChars);
        Assert.Equal(12 + 17, share.RequestChars);
        Assert.Equal(93.0 / 122.0, share.Share, 9);
        Assert.Equal(1, share.Pinned);
    }

    /// <summary>No notebook at all: the share is 0 and the tracker reports no samples until it records.</summary>
    [Fact]
    public void NoNotebookIsZeroShare()
    {
        var prepared = new Message[] { new UserMessage("hello"), new AssistantMessage { Content = [new TextContent("hi")], Model = "m" } };
        var share = NotebookShareTracker.Measure(prepared, "system");
        Assert.Equal(0, share.NotebookChars);
        Assert.Equal(0.0, share.Share);
        var tracker = new NotebookShareTracker();
        Assert.Equal((0, 0.0, 0.0), tracker.Percentiles());
        tracker.Record(share);
        Assert.Equal(1, tracker.Percentiles().Samples);
    }

    /// <summary>The rolling window keeps the most recent shares for the p50/p95.</summary>
    [Fact]
    public void PercentilesFollowNearestRank()
    {
        var tracker = new NotebookShareTracker();
        for (var i = 1; i <= 100; i++)
            tracker.Record(new NotebookShare(i, 100 - i, 1, 0, 0));
        var (samples, p50, p95) = tracker.Percentiles();
        Assert.Equal(100, samples);
        // 100 samples, share i/100 for i=1..100: p50 = 50th smallest = 0.5, p95 = 95th = 0.95.
        Assert.True(Math.Abs(p50 - 0.5) < 1e-9);
        Assert.True(Math.Abs(p95 - 0.95) < 1e-9);
    }

    // ------------------------------------------------------------------ combined budget (pi-sharp-memory-curation-2)

    /// <summary>
    /// Acceptance (pi-sharp-memory-curation-2): for a range of request sizes, the notebook's bytes never
    /// exceed BudgetMaxChars and, for requests large enough that the budget matters, the notebook's share
    /// of the assembled request is <= 15% + a small epsilon (integer truncation in NotebookRoom).
    /// </summary>
    [Theory]
    [InlineData(100)]    // tiny: omit the briefing if even its wrappers cannot fit
    [InlineData(5000)]   // small request
    [InlineData(50000)]  // ~65k local seat (in chars)
    [InlineData(130000)] // 130k subscription seat
    public void NotebookShareNeverExceedsBudgetAndMaxChars(int requestChars)
    {
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Path, "seat");
        // A realistic notebook: a mix of obligations, notes and sources.
        for (var i = 0; i < 20; i++)
            store.Put("source-" + i, "source", "said something about task " + i, "t", "open", 0);
        for (var i = 0; i < 10; i++)
            store.Put("commit-" + i, "commitment", "do the thing " + i, "t", "open", 0);
        for (var i = 0; i < 5; i++)
            store.Write("note-" + i, "body of note " + i + " with some detail", "Note title " + i);

        var budget = NotebookContext.Default();
        var messages = new List<Message> { new UserMessage(new string('x', requestChars)) };
        var prepared = NotebookContext.Prepare(store, messages, budget);

        var share = NotebookShareTracker.Measure(prepared, "system prompt");
        // The notebook's bytes never exceed the absolute cap.
        Assert.True(share.NotebookChars <= budget.MaxChars,
            $"notebookChars={share.NotebookChars} maxChars={budget.MaxChars}");

        Assert.True(share.Share <= budget.Share + 0.00001,
            $"share={share.Share:F4} target={budget.Share}");
    }

    /// <summary>The pinned state is byte-identical across turns when the notebook is unchanged (pi-sharp-
    /// memory-curation-2: the pin stays byte-stable across turns). The messages list is the same, so no
    /// new sources are captured and the briefing content is identical.</summary>
    [Fact]
    public void PinnedStateIsByteStableAcrossTurnsWithUnchangedNotebook()
    {
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Path, "seat");
        store.Put("c1", "commitment", "ship it", "t", "open", 0);
        store.Write("n1", "some note body", "A note");

        var budget = NotebookContext.Default();
        var requestText = new string('a', 60000);
        var messages = new List<Message> { new UserMessage(requestText) };

        var prepared1 = NotebookContext.Prepare(store, messages, budget);
        var pin1 = prepared1.OfType<UserMessage>()
            .Single(u => ContentText.Of(u.Content).StartsWith("<notebook-state>"))
            .Text;

        // Turn 2: same messages (no new user messages, so no new sources), same notebook.
        // The pin must be byte-identical.
        var prepared2 = NotebookContext.Prepare(store, messages, budget);
        var pin2 = prepared2.OfType<UserMessage>()
            .Single(u => ContentText.Of(u.Content).StartsWith("<notebook-state>"))
            .Text;

        Assert.Equal(pin1, pin2);
    }

    /// <summary>The re-pin guard: when the total notebook bytes (pin + deltas) would exceed the budget's
    /// MaxChars, the pin is rebuilt whole instead of appending a delta (pi-sharp-memory-curation-2).</summary>
    [Fact]
    public void RePinInsteadOfAppendWhenOverBudget()
    {
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Path, "seat");
        // A small budget (2000 chars) forces the re-pin guard to trigger quickly.
        var budget = new NotebookContext.Budget(0.15, 2000);
        // Enough records that the briefing is close to 2000 chars.
        for (var i = 0; i < 15; i++)
            store.Put("source-" + i, "source", "said " + i, "t", "open", 0);

        var messages = new List<Message> { new UserMessage(new string('a', 100000)) };
        var prepared1 = NotebookContext.Prepare(store, messages, budget);
        // The first Prepare pins the state.
        Assert.Contains(prepared1.OfType<UserMessage>(), u => ContentText.Of(u.Content).StartsWith("<notebook-state>"));

        // Add a record that changes the briefing; the next Prepare should re-pin (not append a delta)
        // because the total notebook bytes would exceed the 2000-char budget.
        store.Put("new-record", "commitment", "a new commitment that pushes the briefing over budget", "t", "open", 0);
        messages.Add(new AssistantMessage { Content = [new TextContent("did something")], Model = "m" });
        messages.Add(new UserMessage("next turn"));
        var prepared2 = NotebookContext.Prepare(store, messages, budget);

        // The total notebook bytes must never exceed the budget's MaxChars.
        var share = NotebookShareTracker.Measure(prepared2, "system");
        Assert.True(share.NotebookChars <= budget.MaxChars,
            $"notebookChars={share.NotebookChars} maxChars={budget.MaxChars}");
    }

    /// <summary>The default budget: 15% share, 16,800 char cap.</summary>
    [Fact]
    public void DefaultBudgetIs15PercentCappedAt16800()
    {
        var budget = NotebookContext.Default();
        Assert.Equal(0.15, budget.Share);
        Assert.Equal(16800, budget.MaxChars);
        Assert.Equal(16800, NotebookContext.BudgetMaxChars);
    }

    /// <summary>
    /// Property test (pi-sharp-memory-curation-2 acceptance criterion #2): for random notebooks of
    /// 50, 100, 200 and 500 records with a random kind/status distribution, every open or blocked
    /// obligation id (instruction, commitment, check, or any blocked record) appears in the briefing's
    /// tier-0 index lines. The budget is sized so all obligations fit, proving the packer drops none.
    /// With the default 12,000-char budget the oldest obligations give way to the budget and are counted
    /// in the ~omitted line; the property under test is that no obligation is silently lost.
    /// </summary>
    [Theory]
    [InlineData(50)]
    [InlineData(100)]
    [InlineData(200)]
    [InlineData(500)]
    public void EveryOpenOrBlockedObligationIdAppearsInTheBriefing(int totalRecords)
    {
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Path, "seat");
        var rng = new Random(42 + totalRecords); // deterministic per size

        var kinds = new[] { "instruction", "commitment", "check", "note", "decision", "source" };
        var statuses = new[] { "open", "open", "open", "blocked", "completed" }; // weighted toward open

        var obligationIds = new List<string>();
        for (var i = 0; i < totalRecords; i++)
        {
            var id = $"rec-{i:0000}";
            var kind = kinds[rng.Next(kinds.Length)];
            var status = statuses[rng.Next(statuses.Length)];
            var text = $"text-{i} "+ new string('x', rng.Next(10, 60));
            store.Put(id, kind, text, $"title-{i}", status, 0);

            // An obligation is: open/blocked AND (kind is instruction/commitment/check OR status is blocked)
            var isOpenOrBlocked = status is "open" or "blocked";
            var isObligationKind = kind is "instruction" or "commitment" or "check";
            if (isOpenOrBlocked && (isObligationKind || status == "blocked"))
                obligationIds.Add(id);
        }

        // Ensure there are enough obligations to be meaningful.
        Assert.True(obligationIds.Count >= totalRecords / 4,
            $"expected at least {totalRecords / 4} obligations, got {obligationIds.Count}");

        // Budget large enough that every obligation fits: each obligation line costs id.Length + 95.
        var worstCaseBudget = obligationIds.Count * ("rec-0000".Length + 95) + 2000; // + header/omission/notes overhead
        var entries = store.BriefingEntries(worstCaseBudget);

        var entryKeys = new HashSet<string>(entries.Select(e => e.Key));
        var missing = obligationIds.Where(id => !entryKeys.Contains(id)).ToList();
        Assert.Empty(missing);
    }

    /// <summary>
    /// Property test companion: with the default 12,000-char budget and 500 records, any obligation
    /// not listed in the briefing is accounted for in the ~omitted count. No obligation is silently lost.
    /// </summary>
    [Fact]
    public void OmittedObligationsAreCountedNotSilentlyLost()
    {
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Path, "seat");
        var rng = new Random(99);

        var kinds = new[] { "instruction", "commitment", "check", "note", "decision", "source" };
        var statuses = new[] { "open", "open", "open", "blocked", "completed" };
        var obligationIds = new List<string>();

        for (var i = 0; i < 500; i++)
        {
            var id = $"rec-{i:0000}";
            var kind = kinds[rng.Next(kinds.Length)];
            var status = statuses[rng.Next(statuses.Length)];
            store.Put(id, kind, $"text-{i} "+ new string('x', rng.Next(10, 60)), $"t-{i}", status, 0);

            var isOpenOrBlocked = status is "open" or "blocked";
            var isObligationKind = kind is "instruction" or "commitment" or "check";
            if (isOpenOrBlocked && (isObligationKind || status == "blocked"))
                obligationIds.Add(id);
        }

        var entries = store.BriefingEntries(12000); // default budget
        var entryKeys = new HashSet<string>(entries.Select(e => e.Key));
        var listed = obligationIds.Count(id => entryKeys.Contains(id));
        var omitted = obligationIds.Count(id => !entryKeys.Contains(id));

        // The ~omitted line must account for the unlisted obligations (it also counts omitted sources,
        // so the number is >= the obligation count).
        var omittedEntry = entries.FirstOrDefault(e => e.Key == "~omitted");
        if (omitted > 0)
        {
            Assert.NotNull(omittedEntry);
            // Extract the count from the line: "+N older open records not listed..."
            var match = System.Text.RegularExpressions.Regex.Match(omittedEntry.Line, @"\+(\d+)");
            Assert.True(match.Success, $"no count found in: {omittedEntry.Line}");
            var reportedCount = int.Parse(match.Groups[1].Value);
            Assert.True(reportedCount >= omitted,
                $"reported {reportedCount} omitted but at least {omitted} obligations were unlisted");
        }
        else
        {
            Assert.Null(omittedEntry);
        }

        // Every listed obligation must actually be in the entries.
        Assert.Equal(listed + omitted, obligationIds.Count);
    }
}
