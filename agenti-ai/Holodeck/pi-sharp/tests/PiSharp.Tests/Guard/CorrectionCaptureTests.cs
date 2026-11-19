using System.Text.Json.Nodes;
using PiSharp.Core.Factory;
using PiSharp.Core.Guard;
using PiSharp.Core.Notebook;
using PiSharp.Tests.Tools;

namespace PiSharp.Tests.Guard;

public class CorrectionCaptureTests
{
    private static FactoryInboxItem Item(long id, string author, string body, long? thread = 10,
        string reason = "reply", IReadOnlyList<string>? about = null) =>
        new(id, id, reason, new FactoryMessage(id, "board", thread, author, "note", body, [], "2026-09-29T00:00:00Z", About: about));

    private static void AddSource(NotebookStore store, FactoryInboxItem item) =>
        store.Put("chat-source-" + item.Message.Id, "source", item.Message.Body,
            "chat-" + item.Message.Id, "open", 0, operationId: "chat-source-" + item.Message.Id);

    [Fact]
    public void AuthorityReplyWithNegativeMarkerInPostedThreadIsCaptured()
    {
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Path, "seat1");
        var capture = new CorrectionCapture(store, tmp.Path);
        var item = Item(123, "team-lead-2", "Stop doing that; use the safe path.");
        AddSource(store, item);

        Assert.True(capture.Capture(item, true,
            new HashSet<string>(["team-lead-2"]), new HashSet<string>(), ["{\"name\":\"bash\"}"], "seat1"));
        var record = Assert.Single(store.List(kind: "correction"));
        Assert.Equal("chat-source-123", record["sourceId"]!.GetValue<string>());
        var payload = JsonNode.Parse(record["text"]!.GetValue<string>())!;
        Assert.Equal("authority-reply", payload["reason"]!.GetValue<string>());
        Assert.Equal("{\"name\":\"bash\"}", payload["toolCalls"]![0]!.GetValue<string>());
    }

    [Theory]
    [InlineData("team-lead-2", false, "Never do that again.")]
    [InlineData("unknown-actor", true, "Never do that again.")]
    [InlineData("team-lead-2", true, "Thanks for the update.")]
    public void NonAuthorityOffThreadOrNonCorrectionIsIgnored(string author, bool postedInThread, string body)
    {
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Path, "seat1");
        var capture = new CorrectionCapture(store, tmp.Path);
        var item = Item(124, author, body);
        AddSource(store, item);
        Assert.False(capture.Capture(item, postedInThread,
            new HashSet<string>(["team-lead-2"]), new HashSet<string>(), [], "seat1"));
        Assert.Empty(store.List(kind: "correction"));
    }

    [Fact]
    public void ReviewReopenOnOwnedItemCapturesAndDeduplicatesAcrossRestart()
    {
        using var tmp = new TempWorkspace();
        var item = Item(125, "team-lead-2", "review -> in_progress: please fix the failing check.", thread: null,
            reason: "mention", about: ["wi-9"]);
        var authorities = new HashSet<string>();
        var ownItems = new HashSet<string>(["wi-9"]);
        using (var store = new NotebookStore(tmp.Path, "seat1"))
        {
            AddSource(store, item);
            var capture = new CorrectionCapture(store, tmp.Path);
            Assert.True(capture.Capture(item, false, authorities, ownItems, Enumerable.Range(1, 12).Select(n => $"tool-{n}").ToArray(), "seat1"));
            Assert.Single(store.List(kind: "correction"));
        }
        using (var reopened = new NotebookStore(tmp.Path, "seat1"))
        {
            var capture = new CorrectionCapture(reopened, tmp.Path);
            Assert.False(capture.Capture(item, false, authorities, ownItems, Enumerable.Range(1, 12).Select(n => $"tool-{n}").ToArray(), "seat1"));
            var record = Assert.Single(reopened.List(kind: "correction"));
            var calls = JsonNode.Parse(record["text"]!.GetValue<string>())!["toolCalls"]!.AsArray();
            Assert.Equal(10, calls.Count);
            Assert.Equal("tool-3", calls[0]!.GetValue<string>());
            Assert.Equal("tool-12", calls[9]!.GetValue<string>());
        }
    }

    [Fact]
    public void ReviewReopenRequiresOwnershipAndAssignmentParserMatchesSeat()
    {
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Path, "seat1");
        var capture = new CorrectionCapture(store, tmp.Path);
        var item = Item(126, "team-lead-2", "review -> in_progress: fix", thread: null,
            reason: "mention", about: ["wi-other"]);
        AddSource(store, item);
        Assert.False(capture.Capture(item, false, new HashSet<string>(), new HashSet<string>(["wi-own"]), [], "seat1"));
        var noNote = Item(127, "team-lead-2", "review -> in_progress", thread: null,
            reason: "mention", about: ["wi-own"]);
        AddSource(store, noNote);
        Assert.False(capture.Capture(noNote, false, new HashSet<string>(), new HashSet<string>(["wi-own"]), [], "seat1"));
        Assert.Equal(("pi-sharp-guard-rules-2", "coder-06-pi5"),
            CorrectionCapture.ParseAssignment("ASSIGNED pi-sharp-guard-rules-2 -> @coder-06-pi5"));
        Assert.Null(CorrectionCapture.ParseAssignment("status update only"));
    }

    [Fact]
    public void ToolCallCredentialFieldsAreRedacted()
    {
        var call = new PiSharp.Core.Messages.ToolCallContent("id", "chat_post", JsonNode.Parse("""{"token":"secret-value","api_key":"api-private-value","body":"Bearer kbt_private"}""")!.AsObject());
        var safe = CorrectionCapture.SafeToolCall(call);
        Assert.DoesNotContain("secret-value", safe, StringComparison.Ordinal);
        Assert.DoesNotContain("api-private-value", safe, StringComparison.Ordinal);
        Assert.DoesNotContain("kbt_private", safe, StringComparison.Ordinal);
        Assert.Contains("[redacted]", safe, StringComparison.Ordinal);
    }
}
