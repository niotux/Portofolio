using System.Text.Json.Nodes;
using PiSharp.Core.Agent;
using PiSharp.Core.Harness;
using PiSharp.Core.Messages;
using PiSharp.Tests.Agent;

namespace PiSharp.Tests.Harness;

public class CubicleTracerTests
{
    private static readonly TimeSpan Long = TimeSpan.FromMinutes(10);

    private static IEnumerable<JsonObject> Events(StubRequest req) => req.Events.Select(n => n!.AsObject());

    private static string Type(JsonObject evt) => evt["type"]!.GetValue<string>();

    private static JsonObject Payload(JsonObject evt) => evt["payload"]!.AsObject();

    [Fact]
    public async Task FlushesWhenBatchSizeReached()
    {
        using var stub = new CubicleStub();
        using var client = new CubicleClient(stub.Url, "t");
        await using var tracer = new CubicleTracer(client, flushSize: 5, flushInterval: Long);
        tracer.OnEvent(new AgentStart());
        for (var i = 0; i < 5; i++) tracer.OnEvent(new MessageEnd(new UserMessage($"p{i}")));
        var requests = await stub.WaitForRequestsAsync(1);
        var events = Events(requests[0]).ToList();
        Assert.Equal(5, events.Count);
        Assert.Equal([1, 2, 3, 4, 5], events.Select(e => e["seq"]!.GetValue<long>()));
        Assert.All(events, e => Assert.Equal("prompt", Type(e)));
        Assert.All(events, e => Assert.Equal(tracer.RunId, e["run_id"]!.GetValue<string>()));
        Assert.Equal("p3", Payload(events[3])["text"]!.GetValue<string>());
        Assert.Equal("Bearer t", requests[0].Authorization);
    }

    [Fact]
    public async Task FlushesAfterIntervalWithPartialBatch()
    {
        using var stub = new CubicleStub();
        using var client = new CubicleClient(stub.Url, "t");
        await using var tracer = new CubicleTracer(client, flushSize: 50, flushInterval: TimeSpan.FromMilliseconds(100));
        tracer.OnEvent(new MessageEnd(new UserMessage("a")));
        tracer.OnEvent(new MessageEnd(new UserMessage("b")));
        var requests = await stub.WaitForRequestsAsync(1, TimeSpan.FromSeconds(5));
        Assert.Equal(2, requests[0].Events.Count);
    }

    [Fact]
    public async Task DisposeFlushesWhatIsQueued()
    {
        using var stub = new CubicleStub();
        using var client = new CubicleClient(stub.Url, "t");
        var tracer = new CubicleTracer(client, flushSize: 50, flushInterval: Long);
        tracer.OnEvent(new MessageEnd(new UserMessage("a")));
        await tracer.DisposeAsync();
        var req = Assert.Single(stub.Posts);
        Assert.Single(req.Events);
        Assert.Equal(1, tracer.SentBatches);
    }

    [Fact]
    public async Task Hh1Probe_DeadlineEventIsRecordedByTracer()
    {
        using var stub = new CubicleStub();
        using var client = new CubicleClient(stub.Url, "t");
        var tracer = new CubicleTracer(client, flushSize: 50, flushInterval: Long);
        tracer.OnEvent(new ToolExecutionDeadline("call-1", "ignored-token-probe", TimeSpan.FromMilliseconds(250)));
        await tracer.DisposeAsync();

        var requests = await stub.WaitForRequestsAsync(1);
        var evt = Assert.Single(Events(requests[0]));
        Assert.Equal("tool_deadline", Type(evt));
        Assert.Equal("ignored-token-probe", Payload(evt)["name"]!.GetValue<string>());
        Assert.Equal("call-1", Payload(evt)["tool_call_id"]!.GetValue<string>());
        Assert.Equal(250, Payload(evt)["deadline_ms"]!.GetValue<long>());
    }

    [Fact]
    public async Task DropsOldestWhenQueueIsFullAndReportsCount()
    {
        using var stub = new CubicleStub();
        var release = new TaskCompletionSource();
        var first = true;
        stub.Handler = async _ =>
        {
            if (first) { first = false; await release.Task; }
            return StubResponse.Accepted();
        };
        using var client = new CubicleClient(stub.Url, "t", timeout: TimeSpan.FromSeconds(30));
        var tracer = new CubicleTracer(client, capacity: 3, flushSize: 1, flushInterval: Long);
        tracer.OnEvent(new AgentStart());
        tracer.OnEvent(new MessageEnd(new UserMessage("blocker")));
        await stub.WaitForRequestsAsync(1); // pump is now stuck in the first POST
        for (var i = 0; i < 10; i++) tracer.OnEvent(new MessageEnd(new UserMessage($"q{i}")));
        Assert.Equal(7, tracer.DroppedEvents);
        tracer.OnEvent(new AgentEnd([]));
        release.SetResult();
        await tracer.DisposeAsync();

        var all = stub.Posts.SelectMany(Events).ToList();
        var runEnd = Assert.Single(all, e => Type(e) == "run_end");
        Assert.Equal(7, Payload(runEnd)["dropped"]!.GetValue<int>());
        Assert.Equal(8, tracer.DroppedEvents); // run_end pushed one more out
        var kept = all.Where(e => Type(e) == "prompt").Select(e => Payload(e)["text"]!.GetValue<string>()).ToList();
        Assert.Equal(["blocker", "q8", "q9"], kept); // oldest dropped, newest survive
    }

    [Fact]
    public async Task TruncatesEveryTextField()
    {
        using var stub = new CubicleStub();
        using var client = new CubicleClient(stub.Url, "t");
        var big = new string('x', CubicleTracer.MaxText + 3000);
        var tracer = new CubicleTracer(client, flushSize: 50, flushInterval: Long);
        tracer.OnEvent(new MessageEnd(new UserMessage(big)));
        tracer.OnEvent(new MessageEnd(Fakes.Text(big)));
        tracer.OnEvent(new ToolExecutionStart("c1", "read", Fakes.Json($$"""{"path":"{{big}}"}""")));
        tracer.OnEvent(new ToolExecutionEnd("c1", "read", new AgentToolResult([new TextContent(big)]), false));
        tracer.OnEvent(new ToolExecutionEnd("c2", "bash", new AgentToolResult([new TextContent(big)]), true));
        await tracer.DisposeAsync();

        var events = stub.Posts.SelectMany(Events).ToList();
        Assert.Equal(5, events.Count);
        Assert.Equal(CubicleTracer.MaxText, Payload(events[0])["text"]!.GetValue<string>().Length);
        Assert.Equal(CubicleTracer.MaxText, Payload(events[1])["text"]!.GetValue<string>().Length);
        Assert.Equal(CubicleTracer.MaxText, Payload(events[2])["args"]!["path"]!.GetValue<string>().Length);
        Assert.Equal(CubicleTracer.MaxText, Payload(events[3])["result"]!.GetValue<string>().Length);
        Assert.Equal(CubicleTracer.MaxText, Payload(events[4])["error"]!.GetValue<string>().Length);
        Assert.Null(Payload(events[4])["result"]);

        // wi-69: the full runtime-reported length is recorded before truncation, so a result the
        // tracer itself truncated is still measurable client-side (not a wire-byte measure).
        Assert.Equal(big.Length, Payload(events[3])["result_bytes"]!.GetValue<int>());
        Assert.True(Payload(events[3])["result_truncated"]!.GetValue<bool>());
        Assert.Equal(big.Length, Payload(events[4])["result_bytes"]!.GetValue<int>());
        Assert.True(Payload(events[4])["result_truncated"]!.GetValue<bool>());
    }

    [Fact]
    public async Task ToolEndReportsUntruncatedLength()
    {
        using var stub = new CubicleStub();
        using var client = new CubicleClient(stub.Url, "t");
        var tracer = new CubicleTracer(client, flushSize: 50, flushInterval: Long);
        tracer.OnEvent(new ToolExecutionEnd("c1", "read", new AgentToolResult([new TextContent("contents")]), false));
        await tracer.DisposeAsync();

        var end = Payload(Assert.Single(stub.Posts.SelectMany(Events), e => Type(e) == "tool_end"));
        Assert.Equal(8, end["result_bytes"]!.GetValue<int>());
        Assert.False(end["result_truncated"]!.GetValue<bool>());
    }

    [Fact]
    public async Task ARunEndedOnAToolBatchLeavesARunEndTrace()
    {
        // Team watch 2026-10-03: without it pi4's last trace stayed tool_end and RESUME read the stopped seat as mid-turn.
        using var stub = new CubicleStub();
        using var client = new CubicleClient(stub.Url, "t");
        var tracer = new CubicleTracer(client, about: [], flushSize: 50, flushInterval: Long);
        var assistant = Fakes.Assistant(StopReason.ToolUse, Fakes.Call("c1", "kb_list"));
        var refused = new ToolResultMessage { ToolCallId = "c1", ToolName = "kb_list", Content = [new TextContent("repeated")], IsError = true };
        tracer.OnEvent(new AgentStart());
        tracer.OnEvent(new MessageEnd(assistant));
        tracer.OnEvent(new ToolExecutionEnd("c1", "kb_list", new AgentToolResult([new TextContent("repeated")]), true));
        tracer.OnEvent(new TurnEnd(assistant, [refused]));
        tracer.OnEvent(new TurnEnd(assistant, [refused], Final: true));
        await tracer.DisposeAsync();

        var events = stub.Posts.SelectMany(Events).ToList();
        Assert.Equal(["assistant", "tool_end", "run_end"], events.Select(Type));
        Assert.Equal("tool_batch", Payload(events[2])["reason"]!.GetValue<string>());
        Assert.Equal("kb_list", Payload(events[2])["tools"]![0]!.GetValue<string>());
    }

    [Fact]
    public async Task MapsEventsToTraceTypesAndPayloads()
    {
        using var stub = new CubicleStub();
        using var client = new CubicleClient(stub.Url, "t");
        var tracer = new CubicleTracer(client, about: ["wi-42"], flushSize: 50, flushInterval: Long);
        var assistant = Fakes.Assistant(StopReason.ToolUse, new ThinkingContent("hmm", null, false), new TextContent("Reading."), Fakes.Call("c1", "read")) with
        {
            Usage = new Usage(Input: 10, Output: 5, TotalTokens: 15),
        };
        tracer.OnEvent(new AgentStart());
        tracer.OnEvent(new MessageStart(new UserMessage("hi")));
        tracer.OnEvent(new MessageEnd(new UserMessage("hi")));
        tracer.OnEvent(new MessageUpdate(assistant, new TextDelta(0, "R", assistant)));
        tracer.OnEvent(new MessageEnd(assistant));
        tracer.OnEvent(new ToolExecutionStart("c1", "read", Fakes.Json("""{"path":"a.txt"}""")));
        tracer.OnEvent(new ToolExecutionUpdate("c1", "read", new JsonObject(), new AgentToolResult([new TextContent("partial")])));
        tracer.OnEvent(new ToolExecutionEnd("c1", "read", new AgentToolResult([new TextContent("contents")]), false));
        tracer.OnEvent(new MessageEnd(new ToolResultMessage { ToolCallId = "c1", ToolName = "read", Content = [new TextContent("contents")] }));
        var final = Fakes.Text("Done.") with { Usage = new Usage(Input: 20, Output: 2, TotalTokens: 22) };
        tracer.OnEvent(new MessageEnd(final));
        tracer.OnEvent(new AgentEnd([assistant, final]));
        await tracer.DisposeAsync();

        var events = stub.Posts.SelectMany(Events).ToList();
        Assert.Equal(["prompt", "assistant", "tool_start", "tool_end", "assistant", "run_end"], events.Select(Type));
        Assert.All(events, e => Assert.Equal("wi-42", e["about"]![0]!.GetValue<string>()));
        Assert.All(events, e => Assert.False(string.IsNullOrEmpty(e["at"]!.GetValue<string>())));

        var a = Payload(events[1]);
        Assert.Equal("Reading.", a["text"]!.GetValue<string>());
        Assert.Equal(3, a["thinking_chars"]!.GetValue<int>());
        Assert.Equal("read", a["tool_calls"]![0]!.GetValue<string>());
        Assert.Equal("toolUse", a["stop_reason"]!.GetValue<string>());
        Assert.Equal(10, a["usage"]!["input"]!.GetValue<int>());
        Assert.Equal("hmm", a["thinking"]!.GetValue<string>());
        Assert.Equal(assistant.Model, a["model"]!.GetValue<string>());
        Assert.Null(a["error"]);
        Assert.Null(Payload(events[3])["details"]);

        var start = Payload(events[2]);
        Assert.Equal("read", start["name"]!.GetValue<string>());
        Assert.Equal("a.txt", start["args"]!["path"]!.GetValue<string>());

        var end = Payload(events[3]);
        Assert.Equal("read", end["name"]!.GetValue<string>());
        Assert.False(end["is_error"]!.GetValue<bool>());
        Assert.Equal("contents", end["result"]!.GetValue<string>());
        Assert.True(end["duration_ms"]!.GetValue<long>() >= 0);

        var runEnd = Payload(events[5]);
        Assert.Equal("stop", runEnd["reason"]!.GetValue<string>());
        Assert.Equal(2, runEnd["turns"]!.GetValue<int>());
        Assert.Equal(30, runEnd["usage"]!["input"]!.GetValue<int>());
        Assert.Equal(0, runEnd["dropped"]!.GetValue<int>());
    }

    [Fact]
    public async Task NewRunGetsNewIdAndSeqRestarts()
    {
        using var stub = new CubicleStub();
        using var client = new CubicleClient(stub.Url, "t");
        var tracer = new CubicleTracer(client, flushSize: 50, flushInterval: Long);
        tracer.OnEvent(new AgentStart());
        var run1 = tracer.RunId;
        tracer.OnEvent(new MessageEnd(new UserMessage("a")));
        tracer.OnEvent(new AgentEnd([]));
        tracer.OnEvent(new AgentStart());
        var run2 = tracer.RunId;
        tracer.OnEvent(new MessageEnd(new UserMessage("b")));
        await tracer.DisposeAsync();

        Assert.NotEqual(run1, run2);
        Assert.True(run1.Length <= CubicleTracer.MaxRunId);
        var events = stub.Posts.SelectMany(Events).ToList();
        Assert.Equal(3, events.Count);
        Assert.Equal(run1, events[0]["run_id"]!.GetValue<string>());
        Assert.Equal(run2, events[2]["run_id"]!.GetValue<string>());
        Assert.Equal(1, events[2]["seq"]!.GetValue<long>());
    }

    [Fact]
    public async Task UnavailableServerNeverThrowsAndCountsBatches()
    {
        using var stub = new CubicleStub();
        stub.Handler = _ => Task.FromResult(new StubResponse(404, "{}"));
        using var client = new CubicleClient(stub.Url, "t");
        var tracer = new CubicleTracer(client, flushSize: 2, flushInterval: Long);
        for (var i = 0; i < 4; i++) tracer.OnEvent(new MessageEnd(new UserMessage($"p{i}")));
        await tracer.DisposeAsync();
        Assert.Equal(2, tracer.FailedBatches);
        Assert.Equal(0, tracer.SentBatches);
        Assert.Equal(2, stub.Posts.Count());
    }

    [Fact]
    public async Task ClosedPortNeverThrows()
    {
        using var stub = new CubicleStub();
        var url = stub.Url;
        stub.Dispose();
        using var client = new CubicleClient(url, "t");
        var tracer = new CubicleTracer(client, flushSize: 1, flushInterval: Long);
        tracer.OnEvent(new MessageEnd(new UserMessage("a")));
        tracer.OnEvent(new AgentEnd([]));
        await tracer.DisposeAsync();
        Assert.Equal(2, tracer.FailedBatches);
        tracer.OnEvent(new MessageEnd(new UserMessage("after dispose"))); // ignored, no throw
    }

    [Fact]
    public async Task AssistantTurnEmitsACacheMissLineWhenACallbackIsWired()
    {
        const string line = "cache_miss seat=coder-06-pi6 host=.101 missed=0 idle=0 model_changed=false reported=true";
        using var stub = new CubicleStub();
        using var client = new CubicleClient(stub.Url, "t");
        await using var tracer = new CubicleTracer(client, flushSize: 50, flushInterval: Long, cacheMissLine: () => line);
        tracer.OnEvent(new AgentStart());
        tracer.OnEvent(new MessageEnd(new AssistantMessage { Content = [new TextContent("hi")], Model = "m", StopReason = StopReason.Stop }));
        await tracer.DisposeAsync();
        var req = Assert.Single(stub.Posts);
        var events = Events(req).ToList();
        var miss = Assert.Single(events, e => Type(e) == "cache_miss");
        Assert.Equal(line, Payload(miss)["line"]!.GetValue<string>());
    }

    [Fact]
    public async Task AssistantTurnEmitsNoCacheMissLineWithoutACallback()
    {
        using var stub = new CubicleStub();
        using var client = new CubicleClient(stub.Url, "t");
        await using var tracer = new CubicleTracer(client, flushSize: 50, flushInterval: Long);
        tracer.OnEvent(new AgentStart());
        tracer.OnEvent(new MessageEnd(new AssistantMessage { Content = [new TextContent("hi")], Model = "m", StopReason = StopReason.Stop }));
        await tracer.DisposeAsync();
        var req = Assert.Single(stub.Posts);
        Assert.All(Events(req), e => Assert.NotEqual("cache_miss", Type(e)));
    }

    /// <summary>The web cubicle view renders edits from tool_end.details and shows the model's thinking.</summary>
    [Fact]
    public async Task ForwardsDetailsThinkingErrorsAndTurnStarts()
    {
        using var stub = new CubicleStub();
        using var client = new CubicleClient(stub.Url, "t");
        var tracer = new CubicleTracer(client, flushSize: 50, flushInterval: Long);
        var details = Fakes.Json("""{"diff":"-1 old\n+1 new","patch":"@@ -1 +1 @@","firstChangedLine":1}""");
        var failed = Fakes.Text("") with { StopReason = StopReason.Error, ErrorMessage = "upstream 500", ResponseModel = "served-model" };
        var redacted = Fakes.Assistant(StopReason.Stop, new ThinkingContent("secret", null, true), new TextContent("ok"));
        tracer.OnEvent(new TurnStart());
        tracer.OnEvent(new ToolExecutionEnd("c1", "edit", new AgentToolResult([new TextContent("Edited a.cs")], details), false));
        tracer.OnEvent(new ToolExecutionEnd("c2", "notebook", new AgentToolResult([new TextContent("big")], Fakes.Json($$"""{"rows":"{{new string('r', CubicleTracer.MaxText)}}","more":"{{new string('m', CubicleTracer.MaxText)}}","extra":"{{new string('e', CubicleTracer.MaxText)}}","last":"{{new string('l', CubicleTracer.MaxText)}}"}""")), false));
        tracer.OnEvent(new MessageEnd(failed));
        tracer.OnEvent(new MessageEnd(redacted));
        await tracer.DisposeAsync();

        var events = stub.Posts.SelectMany(Events).ToList();
        Assert.Equal(["turn_start", "tool_end", "tool_end", "assistant", "assistant"], events.Select(Type));
        Assert.Empty(Payload(events[0]));
        var edit = Payload(events[1])["details"]!;
        Assert.Equal("-1 old\n+1 new", edit["diff"]!.GetValue<string>());
        Assert.Equal(1, edit["firstChangedLine"]!.GetValue<int>());
        Assert.True(Payload(events[2])["details"]!["omitted_chars"]!.GetValue<int>() > CubicleTracer.MaxStructured);
        Assert.Equal("upstream 500", Payload(events[3])["error"]!.GetValue<string>());
        Assert.Equal("served-model", Payload(events[3])["model"]!.GetValue<string>());
        Assert.Null(Payload(events[4])["thinking"]); // redacted thinking is counted, never sent
        Assert.Equal(6, Payload(events[4])["thinking_chars"]!.GetValue<int>());
    }

    [Fact]
    public void StructuredCutsStringsAndArraysAndKeepsOtherValues()
    {
        var node = Fakes.Json($$"""{"s":"{{new string('x', CubicleTracer.MaxText + 5)}}","n":3,"b":true,"z":null,"a":[{{string.Join(',', Enumerable.Range(0, CubicleTracer.MaxArrayItems + 50))}}]}""");
        var cut = CubicleTracer.Structured(node)!;
        Assert.Equal(CubicleTracer.MaxText, cut["s"]!.GetValue<string>().Length);
        Assert.Equal(3, cut["n"]!.GetValue<int>());
        Assert.True(cut["b"]!.GetValue<bool>());
        Assert.Null(cut["z"]);
        Assert.Equal(CubicleTracer.MaxArrayItems, cut["a"]!.AsArray().Count);
        Assert.Equal(CubicleTracer.MaxText + 5, node["s"]!.GetValue<string>().Length); // the tool's own value is untouched
    }

    [Fact]
    public async Task EffortDeltaPerTaskCountsTurnsToolsAndTokensAndNamesTheWorkItem()
    {
        using var stub = new CubicleStub();
        using var client = new CubicleClient(stub.Url, "t");
        string? task = "chat-1";
        string? bound = "wi-7";
        var tracer = new CubicleTracer(client, flushSize: 50, flushInterval: Long,
            taskKey: () => task, workItem: () => bound,
            context: () => new ContextGauge(1200, 100000, 80000));
        var call = Fakes.Assistant(StopReason.ToolUse, Fakes.Call("c1", "bash")) with
        {
            Usage = new Usage(Input: 100, Output: 10, CacheRead: 900, TotalTokens: 1010),
        };
        var done = Fakes.Text("Done.") with { Usage = new Usage(Input: 50, Output: 5, CacheRead: 1000, TotalTokens: 1055) };
        tracer.OnEvent(new AgentStart());
        tracer.OnEvent(new MessageEnd(call));
        tracer.OnEvent(new ToolExecutionStart("c1", "bash", Fakes.Json("""{"command":"ls"}""")));
        tracer.OnEvent(new ToolExecutionEnd("c1", "bash", new AgentToolResult([new TextContent("x")]), true));
        tracer.OnEvent(new MessageEnd(done));
        // The next message takes the seat inside the same run: the first task's counts close before its prompt.
        task = "chat-2";
        bound = null;
        tracer.OnEvent(new MessageEnd(new UserMessage("next")));
        tracer.OnEvent(new MessageEnd(done));
        tracer.OnEvent(new AgentEnd([call, done, done]));
        await tracer.DisposeAsync();

        var events = stub.Posts.SelectMany(Events).ToList();
        var efforts = events.Where(e => Type(e) == "effort").ToList();
        Assert.Equal(2, efforts.Count);
        var first = Payload(efforts[0]);
        Assert.Equal("chat-1", first["task"]!.GetValue<string>());
        Assert.Equal("wi-7", first["work_item"]!.GetValue<string>());
        Assert.Equal("task_switch", first["reason"]!.GetValue<string>());
        Assert.Equal(2, first["turns"]!.GetValue<int>());
        Assert.Equal(1, first["tool_calls"]!.GetValue<int>());
        Assert.Equal(1, first["tool_errors"]!.GetValue<int>());
        Assert.Equal(150, first["usage"]!["input"]!.GetValue<int>());
        Assert.Equal(1900, first["usage"]!["cache_read"]!.GetValue<int>());
        Assert.NotNull(first["model"]);
        Assert.Equal(["wi-7"], efforts[0]["about"]!.AsArray().Select(a => a!.GetValue<string>()));

        var second = Payload(efforts[1]);
        Assert.Equal("chat-2", second["task"]!.GetValue<string>());
        Assert.Null(second["work_item"]);
        Assert.Equal("run_end", second["reason"]!.GetValue<string>());
        Assert.Equal(1, second["turns"]!.GetValue<int>());
        Assert.Null(efforts[1]["about"]);

        var assistant = Payload(events.First(e => Type(e) == "assistant"));
        Assert.Equal(1200, assistant["context"]!["tokens"]!.GetValue<int>());
        Assert.Equal(100000, assistant["context"]!["window"]!.GetValue<int>());
        Assert.Equal(80000, assistant["context"]!["compact_at"]!.GetValue<int>());
        // Events of a bound task carry its work item, so the cubicle can be read per work item.
        Assert.Contains("wi-7", events.First(e => Type(e) == "tool_end")["about"]!.AsArray().Select(a => a!.GetValue<string>()));
    }

    [Fact]
    public async Task UnboundTaskTakesTheWorkItemFromItsKbTransitionCall()
    {
        using var stub = new CubicleStub();
        using var client = new CubicleClient(stub.Url, "t");
        var tracer = new CubicleTracer(client, flushSize: 50, flushInterval: Long, taskKey: () => "chat-9", workItem: () => null);
        tracer.OnEvent(new AgentStart());
        tracer.OnEvent(new MessageEnd(Fakes.Assistant(StopReason.ToolUse, Fakes.Call("c1", "kb__kb_transition"))));
        tracer.OnEvent(new ToolExecutionStart("c1", "kb__kb_transition", Fakes.Json("""{"work_item":"wi-31","to_status":"review"}""")));
        tracer.OnEvent(new ToolExecutionEnd("c1", "kb__kb_transition", new AgentToolResult([new TextContent("ok")]), false));
        tracer.OnEvent(new AgentEnd([]));
        await tracer.DisposeAsync();

        var effort = Payload(Assert.Single(stub.Posts.SelectMany(Events), e => Type(e) == "effort"));
        Assert.Equal("wi-31", effort["work_item"]!.GetValue<string>());
        Assert.Equal(1, effort["tool_calls"]!.GetValue<int>());
    }

    [Fact]
    public async Task AttachedRunClosesTheCountsAtEveryFinalTurn_WithoutAnAgentEnd()
    {
        using var stub = new CubicleStub();
        using var client = new CubicleClient(stub.Url, "t");
        var tracer = new CubicleTracer(client, flushSize: 50, flushInterval: Long, taskKey: () => "chat-5", workItem: () => "wi-5");
        var reply = Fakes.Text("Done.") with { Usage = new Usage(Input: 40, Output: 4, TotalTokens: 44) };
        tracer.OnEvent(new AgentStart());
        tracer.OnEvent(new TurnStart());
        tracer.OnEvent(new MessageEnd(reply));
        tracer.OnEvent(new TurnEnd(reply, [], Final: true));
        // A follow-up on the same task in the same open run adds a second delta; nothing is counted twice.
        tracer.OnEvent(new TurnStart());
        tracer.OnEvent(new MessageEnd(reply));
        tracer.OnEvent(new TurnEnd(reply, [], Final: true));
        await tracer.DisposeAsync();

        var efforts = stub.Posts.SelectMany(Events).Where(e => Type(e) == "effort").Select(Payload).ToList();
        Assert.Equal(2, efforts.Count);
        Assert.All(efforts, e =>
        {
            Assert.Equal("turn_end", e["reason"]!.GetValue<string>());
            Assert.Equal("wi-5", e["work_item"]!.GetValue<string>());
            Assert.Equal(1, e["turns"]!.GetValue<int>());
            Assert.Equal(40, e["usage"]!["input"]!.GetValue<int>());
        });
    }

    [Fact]
    public async Task NoTaskKeyMeansNoEffortEvents()
    {
        using var stub = new CubicleStub();
        using var client = new CubicleClient(stub.Url, "t");
        var tracer = new CubicleTracer(client, flushSize: 50, flushInterval: Long);
        tracer.OnEvent(new AgentStart());
        tracer.OnEvent(new MessageEnd(Fakes.Text("hi")));
        tracer.OnEvent(new AgentEnd([]));
        await tracer.DisposeAsync();

        Assert.DoesNotContain(stub.Posts.SelectMany(Events), e => Type(e) == "effort");
    }
}
