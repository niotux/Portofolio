using System.Net;
using System.Text.Json.Nodes;
using PiSharp.Core.Agent;
using PiSharp.Core.Factory;
using PiSharp.Core.Guard;
using PiSharp.Core.Messages;
using PiSharp.Core.Session;
using PiSharp.Tools;

namespace PiSharp.Tests.Tools;

// plan-pi-sharp-codex phase 6: turn diffs (item 19), the seat's telemetry (item 20) and session fork (item 21).
public sealed class TurnDiffTests
{
    private static ToolCallContent Call(string name, string json) => new("c-" + name, name, (JsonObject)JsonNode.Parse(json)!);

    [Fact]
    public void ATurnsDiffCoversEditWriteAndPatch_FromEachFilesContentBeforeTheTurn()
    {
        using var ws = new TempWorkspace();
        ws.File("src/a.cs", "one\ntwo\nthree\n");
        ws.File("src/gone.txt", "bye\n");
        var tracker = new TurnDiffTracker(ws.Path);
        IReadOnlyList<IAgentTool> tools = [new EditTool(ws.Path), new WriteTool(ws.Path)];
        tracker.Capture([Call("edit", """{"path":"src/a.cs","oldText":"two","newText":"2"}""")], tools);
        File.WriteAllText(Path.Combine(ws.Path, "src/a.cs"), "one\n2\nthree\n");
        tracker.Capture([
            Call("apply_patch", """{"input":"*** Begin Patch\n*** Add File: src/new.cs\n+fresh\n*** Delete File: src/gone.txt\n*** End Patch"}"""),
            Call("edit", """{"path":"src/a.cs","oldText":"one","newText":"1"}"""), // already captured: its baseline stays the first
            Call("read", """{"path":"src/a.cs"}"""),
        ], tools);
        File.WriteAllText(Path.Combine(ws.Path, "src/new.cs"), "fresh\n");
        File.Delete(Path.Combine(ws.Path, "src/gone.txt"));
        Assert.Equal(3, tracker.Tracked);

        var diff = tracker.Finish();
        Assert.NotNull(diff);
        Assert.Equal(["src/a.cs", "src/gone.txt", "src/new.cs"], diff.Files.Select(f => f.Path));
        Assert.Equal((1, 1), (diff.Files[0].Added, diff.Files[0].Removed));
        Assert.True(diff.Files[1].Deleted);
        Assert.True(diff.Files[2].Created);
        Assert.Contains("-two", diff.Diff);
        Assert.Contains("+2", diff.Diff);
        Assert.Contains("+fresh", diff.Diff);
        Assert.Equal("3 files (+2 -2): src/a.cs, src/gone.txt, src/new.cs", diff.Line());
        Assert.Equal(0, tracker.Tracked); // the next turn starts empty
        Assert.Null(tracker.Finish());
    }

    [Fact]
    public void AnUnchangedFileIsNoDiff_AndALongDiffIsCutAtItsCap()
    {
        using var ws = new TempWorkspace();
        ws.File("same.txt", "x\n");
        ws.File("big.txt", string.Concat(Enumerable.Range(0, 400).Select(i => $"line {i}\n")));
        ws.File("small.txt", "a\n");
        var tracker = new TurnDiffTracker(ws.Path);
        tracker.Capture([Call("write", """{"path":"same.txt","content":"x\n"}"""), Call("write", """{"path":"big.txt","content":""}"""),
            Call("write", """{"path":"small.txt","content":"b\n"}""")]);
        File.WriteAllText(Path.Combine(ws.Path, "big.txt"), string.Concat(Enumerable.Range(0, 400).Select(i => $"changed {i}\n")));
        File.WriteAllText(Path.Combine(ws.Path, "small.txt"), "b\n");

        var diff = tracker.Finish(maxDiffBytes: 2_000);
        Assert.NotNull(diff);
        Assert.Equal(["big.txt", "small.txt"], diff.Files.Select(f => f.Path)); // both listed with their counts
        Assert.Equal((400, 400), (diff.Files[0].Added, diff.Files[0].Removed));
        Assert.True(diff.Truncated);
        Assert.True(diff.Diff.Length <= 2_000);
        Assert.True(diff.ToJson()["truncated"]!.GetValue<bool>());
    }

    [Fact]
    public async Task RunTelemetryRecordsRequestsToolsGuardDecisionsAndTheTurnDiff()
    {
        using var ws = new TempWorkspace();
        ws.File("f.txt", "old\n");
        var sent = new List<SeatEvent>();
        var sink = new SeatTelemetry((events, _) => { sent.AddRange(events); return Task.FromResult(true); });
        var run = new RunTelemetry(sink, new TurnDiffTracker(ws.Path));
        var reply = new AssistantMessage
        {
            Content = [new ToolCallContent("t1", "bash", new JsonObject { ["command"] = "ls" })], Model = "gpt-x", Provider = "openai-codex",
            StopReason = StopReason.ToolUse, Usage = new Usage(Input: 1000, Output: 50, CacheRead: 800),
        };
        run.Observe(new MessageStart(reply));
        run.Observe(new MessageEnd(reply));
        run.Observe(new ToolExecutionStart("t1", "bash", new JsonObject()));
        run.Observe(new ToolExecutionEnd("t1", "bash", AgentToolResult.Error("exit 2: no such file"), true));
        run.Guard(new ToolCallContent("t2", "bash", new JsonObject { ["command"] = "git push --force" }),
            new GuardVerdict(GuardAction.Ask, new GuardRule("force-push", "force", "A force push rewrites a branch.", action: GuardAction.Ask), "ask"), "denied");
        run.Diffs.Capture([Call("write", """{"path":"f.txt","content":"new\n"}""")]);
        File.WriteAllText(Path.Combine(ws.Path, "f.txt"), "new\n");
        var lines = new List<string>();
        Assert.NotNull(run.Finish(lines.Add));
        await sink.FlushAsync();

        Assert.Equal([SeatEvent.Request, SeatEvent.Tool, SeatEvent.Guard, SeatEvent.TurnDiff], sent.Select(e => e.Kind));
        var request = sent[0];
        Assert.Equal(("gpt-x", true, 1000L, 50L, 800L), (request.Name, request.Ok, request.TokensIn, request.TokensOut, request.TokensCached));
        Assert.Equal(1, request.Detail!["tool_calls"]!.GetValue<int>());
        Assert.Equal(("bash", false), (sent[1].Name, sent[1].Ok));
        Assert.Contains("no such file", sent[1].Detail!["error"]!.GetValue<string>());
        Assert.Equal(("force-push", false, "denied"), (sent[2].Name, sent[2].Ok, sent[2].Detail!["decision"]!.GetValue<string>()));
        Assert.Equal("f.txt", sent[3].Detail!["files"]![0]!["path"]!.GetValue<string>());
        Assert.Equal(["[turn] changed 1 file (+1 -1): f.txt"], lines);
        var json = request.ToJson();
        Assert.Equal("request", json["kind"]!.GetValue<string>());
        Assert.Equal(1000, json["tokens_in"]!.GetValue<long>());
    }

    [Fact]
    public async Task TelemetryKeepsWhatItCouldNotSend_UpToItsCap()
    {
        var up = false;
        var batches = new List<int>();
        var sink = new SeatTelemetry((events, _) =>
        {
            if (!up) throw new HttpRequestException("hub down");
            batches.Add(events.Count);
            return Task.FromResult(true);
        });
        for (var i = 0; i < SeatTelemetry.MaxQueued + 10; i++) sink.Record(new SeatEvent(DateTime.UtcNow, SeatEvent.Tool, "t" + i));
        Assert.Equal(SeatTelemetry.MaxQueued, sink.Queued);
        Assert.Equal(10, sink.Dropped);

        await sink.FlushAsync();
        Assert.Equal(SeatTelemetry.MaxQueued, sink.Queued); // the failed batch went back

        up = true;
        await sink.FlushAsync();
        Assert.Equal(0, sink.Queued);
        Assert.Equal(SeatTelemetry.MaxQueued, sink.Sent);
        Assert.All(batches, n => Assert.True(n <= SeatTelemetry.BatchSize));
    }

    [Fact]
    public async Task TheHubSenderPostsTheEventsUnderTheSeatsToken()
    {
        string? body = null, auth = null, path = null;
        using var handler = new Recording(async request =>
        {
            body = await request.Content!.ReadAsStringAsync();
            auth = request.Headers.Authorization?.ToString();
            path = request.RequestUri!.AbsolutePath;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        await using var sink = SeatTelemetry.ForHub("http://127.0.0.1:18081/mcp", "seat-token", handler);
        sink.Record(new SeatEvent(new DateTime(2026, 10, 25, 18, 0, 0, DateTimeKind.Utc), SeatEvent.Tool, "bash", true, 120));
        await sink.FlushAsync();
        Assert.Equal("/seat/events", path);
        Assert.Equal("Bearer seat-token", auth);
        var events = JsonNode.Parse(body!)!["events"]!.AsArray();
        Assert.Equal("bash", Assert.Single(events)!["name"]!.GetValue<string>());
        Assert.Equal(120, events[0]!["duration_ms"]!.GetValue<int>());
    }

    /// <summary>A hub older than the seat refuses a batch holding a kind it does not know; the seat sends the rest again.</summary>
    [Fact]
    public async Task AnOlderHubStillGetsTheKindsItKnows()
    {
        var posts = new List<string>();
        using var handler = new Recording(async request =>
        {
            var body = await request.Content!.ReadAsStringAsync();
            posts.Add(body);
            return new HttpResponseMessage(body.Contains("\"compaction\"") ? HttpStatusCode.BadRequest : HttpStatusCode.OK);
        });
        await using var sink = SeatTelemetry.ForHub("http://127.0.0.1:18081/mcp", "seat-token", handler);
        sink.Record(new SeatEvent(DateTime.UtcNow, SeatEvent.Compaction, "between-turns", true));
        sink.Record(new SeatEvent(DateTime.UtcNow, SeatEvent.Tool, "bash", true, 120));
        await sink.FlushAsync();
        Assert.Equal(2, posts.Count);
        Assert.DoesNotContain("compaction", posts[1]);
        Assert.Contains("\"bash\"", posts[1]);
        Assert.Equal(0, sink.Queued);
    }

    [Fact]
    public void AForkIsACopyUnderANewIdThatNamesItsSource()
    {
        using var ws = new TempWorkspace();
        var source = SessionManager.Create(ws.Path, ws.Path);
        source.AppendMessage(new UserMessage("first"));
        source.AppendMessage(new AssistantMessage { Content = [new TextContent("answer")], Model = "m", StopReason = StopReason.Stop });
        var sourceFile = source.SessionFile!;
        var before = File.ReadAllText(sourceFile);

        var fork = SessionManager.Fork(sourceFile, ws.Path);
        Assert.NotEqual(source.SessionId, fork.SessionId);
        Assert.Equal(source.SessionId, fork.Header!.ParentSession);
        Assert.Equal(["first", "answer"], fork.BuildContextMessages().Select(m => m switch
        {
            UserMessage u => u.Text, AssistantMessage a => a.Text, _ => "",
        }));
        fork.AppendMessage(new UserMessage("second try"));

        Assert.Equal(before, File.ReadAllText(sourceFile)); // the original is untouched
        var reopened = SessionManager.Open(fork.SessionFile!);
        Assert.Equal(3, reopened.BuildContextMessages().Count);
        Assert.Equal(source.SessionId, reopened.Header!.ParentSession);
    }

    private sealed class Recording(Func<HttpRequestMessage, Task<HttpResponseMessage>> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => answer(request);
    }
}
