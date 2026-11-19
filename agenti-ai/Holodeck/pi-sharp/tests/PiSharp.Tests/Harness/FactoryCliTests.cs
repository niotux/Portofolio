// CliHost in attached mode against an in-memory AI Factory: no sockets, no server, no real token.
// The fake answers /whoami plus the chat MCP tools and records every request, so the tests assert the
// wire (tool names and arguments) as well as what the run did: which turns started, what got posted back,
// and what the model saw in each request.
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using PiSharp.Cli;
using PiSharp.Core.Agent;
using PiSharp.Core.Config;
using PiSharp.Core.Mcp;
using PiSharp.Core.Messages;
using PiSharp.Core.Models;
using PiSharp.Core.Session;
using PiSharp.Tests.Agent;
using PiSharp.Tests.Factory;
using PiSharp.Tests.Tools;

namespace PiSharp.Tests.Harness;

/// <summary>An in-memory AI Factory: the KB's <c>/whoami</c> plus <c>chat_read</c>, <c>chat_wait</c>,
/// <c>chat_post</c>, <c>chat_ack</c> and <c>chat_manage op=status</c>. The latch is the real one: an item the scripts delivered stays in every
/// digest - probe included - until <c>chat_ack</c> handles its message id, so a <c>chat_wait</c> that returned
/// items is followed by a <c>chat_read</c> returning the same ones, and that read does not advance the read
/// script. Acking is by the inbox row's message id (the top-level <c>messageId</c>), the id space
/// <c>chat_ack</c> really uses, never the inbox id.</summary>
internal sealed class FactoryServer
{
    private readonly object _gate = new();
    private readonly List<JsonObject> _items = [];
    private readonly HashSet<long> _handled = [];
    private bool _repeat;
    private int _whoAmI;
    private int _probes;
    private int _reads;
    private int _waits;
    private int _posts;
    private int _acks;

    /// <summary>Slug reported by /whoami.</summary>
    public string Slug { get; init; } = "coder-x";

    public HttpStatusCode WhoAmIStatus { get; init; } = HttpStatusCode.OK;

    /// <summary>The KB connection fails outright (no response at all).</summary>
    public bool WhoAmIThrows { get; init; }

    /// <summary>The egress probe (chat_read with mark_read:false) answers a JSON-RPC error frame.</summary>
    public bool ProbeFails { get; init; }

    /// <summary>Unread room counters ride along in every digest; they are addressed to nobody.</summary>
    public bool RoomTraffic { get; init; }

    /// <summary>Inbox item for the n-th consuming chat_read (1-based); null = empty inbox.</summary>
    public Func<int, JsonObject?>? OnRead { get; init; }

    /// <summary>Inbox item for the n-th chat_wait (1-based); null = timed out with an empty inbox.</summary>
    public Func<int, JsonObject?>? OnWait { get; init; }

    public int WhoAmICalls { get { lock (_gate) return _whoAmI; } }

    public int ProbeCalls { get { lock (_gate) return _probes; } }

    public int PostCalls { get { lock (_gate) return _posts; } }

    public int AckCalls { get { lock (_gate) return _acks; } }

    /// <summary>Message ids still latched: delivered and not yet acknowledged.</summary>
    public List<long> Unhandled
    {
        get { lock (_gate) return _items.Select(Key).OfType<long>().Where(id => !_handled.Contains(id)).ToList(); }
    }

    public FakeHandler Handler() => new(RespondAsync);

    private async Task<HttpResponseMessage> RespondAsync(HttpRequestMessage request)
    {
        if (request.RequestUri!.AbsolutePath.EndsWith("/whoami", StringComparison.Ordinal))
        {
            lock (_gate) _whoAmI++;
            if (WhoAmIThrows) throw new HttpRequestException("connection refused");
            return Respond(WhoAmIStatus, new JsonObject { ["slug"] = Slug, ["name"] = Slug }.ToJsonString());
        }

        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync();
        var envelope = (JsonObject)JsonNode.Parse(body)!;
        var tool = envelope["params"]!["name"]!.GetValue<string>();
        var arguments = (JsonObject)envelope["params"]!["arguments"]!;
        string? text;
        lock (_gate) text = ToolText(tool, arguments);
        return text is null
            ? Respond(HttpStatusCode.OK, """{"jsonrpc":"2.0","id":1,"error":{"code":-32000,"message":"chat is down"}}""")
            : Respond(HttpStatusCode.OK, Frame(text));
    }

    private string? ToolText(string tool, JsonObject arguments)
    {
        switch (tool)
        {
            case "chat_read" when arguments["mark_read"] is JsonValue flag && flag.TryGetValue<bool>(out var mark) && !mark:
                _probes++;
                // The probe marks nothing read and advances no script, but it sees whatever is still latched.
                return ProbeFails ? null : Digest(Latched(), timedOut: false, RoomTraffic);
            case "chat_read":
                if (_repeat)
                {
                    _repeat = false;
                    return Digest(Latched(), timedOut: false, RoomTraffic);
                }
                _reads++;
                Deliver(OnRead?.Invoke(_reads));
                return Digest(Latched(), timedOut: false, RoomTraffic);
            case "chat_wait":
                _waits++;
                Deliver(OnWait?.Invoke(_waits));
                var latched = Latched();
                _repeat = latched.Count > 0;
                return Digest(latched, timedOut: latched.Count == 0, RoomTraffic);
            case "chat_post":
                _posts++;
                return """{"id":9001,"room":"pi-sharp"}""";
            case "chat_ack":
                _acks++;
                return Acknowledge(arguments);
            case "chat_manage":
                return """{"ok":true}""";
            case "kb_list":
                // The seat holds no ready/in_progress work item, so a finished task turns its line idle.
                return """{"total":0,"offset":0,"count":0,"items":[]}""";
            default:
                return null;
        }
    }

    /// <summary>Adds an item the script produced; like the server's insert, an id already there is left alone
    /// (so a script that keeps returning the same item does not multiply it, and an acked one never comes back).</summary>
    private void Deliver(JsonObject? item)
    {
        if (item is null) return;
        var key = Key(item);
        if (key is long id && _items.Exists(i => Key(i) == id)) return;
        _items.Add(item);
    }

    private List<JsonObject> Latched() =>
        _items.Where(i => Key(i) is not long id || !_handled.Contains(id)).ToList();

    /// <summary>The id <c>chat_ack</c> works in: the inbox row's message id, falling back to the nested one.</summary>
    private static long? Key(JsonObject item)
    {
        if (item["messageId"] is JsonValue top && top.TryGetValue<long>(out var id)) return id;
        if (item["message"] is JsonObject m && m["id"] is JsonValue nested && nested.TryGetValue<long>(out var inner)) return inner;
        return null;
    }

    private string Acknowledge(JsonObject arguments)
    {
        var acknowledged = new JsonArray();
        var alreadySeen = new JsonArray();
        var unknown = new JsonArray();
        var ids = arguments["message_ids"]!.AsArray().Select(n => n!.GetValue<long>()).Distinct();
        foreach (var id in ids)
        {
            var bucket = !_items.Exists(i => Key(i) == id) ? unknown : _handled.Add(id) ? acknowledged : alreadySeen;
            bucket.Add((JsonNode)JsonValue.Create(id));
        }
        return new JsonObject
        {
            ["actor"] = Slug,
            ["acknowledged"] = acknowledged,
            ["alreadySeen"] = alreadySeen,
            ["unknown"] = unknown,
        }.ToJsonString();
    }

    private static string Digest(IReadOnlyList<JsonObject> items, bool timedOut, bool rooms)
    {
        var inbox = new JsonArray();
        // The same fixture object is served on every latched digest, and a JsonNode has only one parent.
        foreach (var item in items) inbox.Add(item.DeepClone());
        return new JsonObject
        {
            ["inbox"] = inbox,
            ["rooms"] = rooms
                ? new JsonArray(new JsonObject { ["room"] = "pi-sharp", ["unread"] = 4 })
                : new JsonArray(),
            ["reachable"] = new JsonArray(),
            ["timedOut"] = timedOut,
        }.ToJsonString();
    }

    private static string Frame(string toolText) => new JsonObject
    {
        ["jsonrpc"] = "2.0",
        ["id"] = 1,
        ["result"] = new JsonObject
        {
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = toolText }),
            ["isError"] = false,
        },
    }.ToJsonString();

    private static HttpResponseMessage Respond(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}

[Collection("cubicle-env")]
public class FactoryCliTests
{
    private const string Actor = "coder-x";

    // ------------------------------------------------------------------ attached runs

    [Fact]
    public async Task InboxQuestionIsAnsweredInItsThread()
    {
        var server = new FactoryServer
        {
            // The latch keeps handing the same item back until it is acked; the steering poll before turn 1 wins it.
            OnRead = _ => Item(2831, "what is the build status?", thread: 2830),
        };
        using var handler = server.Handler();
        using var tmp = new TempWorkspace();
        WriteModel(tmp);
        WriteFactorySettings(tmp);
        var stream = new ScriptedStream().Then(Fakes.Text("build is green"));
        var (code, stdout, stderr) = await Run(["-p", "--no-session", "ping"], tmp, handler, stream);

        Assert.Equal(0, code);
        Assert.Equal(
            ["factory: final-text posted to thread 2830, 0 secondary target(s) skipped (task for message 2831, acknowledged 2831)"],
            Lines(stderr));
        Assert.Contains("build is green", stdout, StringComparison.Ordinal);
        Assert.Equal(1, stream.Calls);
        var steered = stream.Contexts[0].Messages.OfType<UserMessage>().Last();
        Assert.Contains("what is the build status?", steered.Text, StringComparison.Ordinal);

        var post = Assert.Single(Calls(handler, "chat_post"));
        Assert.Equal("build is green", post.Arguments["body"]!.GetValue<string>());
        Assert.Equal(2830, post.Arguments["thread"]!.GetValue<long>());
        Assert.Equal("answer", post.Arguments["kind"]!.GetValue<string>());
        Assert.False(post.Arguments.ContainsKey("room"));
        Assert.False(post.Arguments.ContainsKey("to"));

        // Digests only ever used the two frozen shapes.
        foreach (var read in Calls(handler, "chat_read").Skip(1))
        {
            Assert.True(read.Arguments["mark_read"]!.GetValue<bool>());
            Assert.Equal("mark_read", Assert.Single(read.Arguments).Key);
        }
        foreach (var wait in Calls(handler, "chat_wait"))
        {
            Assert.Equal(30, wait.Arguments["timeout_seconds"]!.GetValue<int>());
            Assert.Equal("inbox", wait.Arguments["scope"]!.GetValue<string>());
            Assert.Equal(2, wait.Arguments.Count);
        }
        // rate-bands-d: after a successful turn the steering also reads its own rate band (kb_get on the actor slug);
        // the fake answers an error frame, so the check is a no-op here.
        Assert.All(Posts(handler), r => Assert.Contains(r.Tool, (string[])["chat_read", "chat_wait", "chat_post", "chat_ack", "chat_manage", "kb_list", "kb_get"]));

        // The reply is acked, so the item leaves the inbox instead of latching into the next process.
        Assert.Equal([2831L], Calls(handler, "chat_ack").SelectMany(AckIds).ToList());
        Assert.Empty(server.Unhandled);

        // wi-84: status is a field, not a message: working while the task is open, then idle once it has left
        // and the seat holds no ready/in_progress item.
        Assert.Equal(["working — message 2831 in pi-sharp", "idle — last: message 2831 in pi-sharp"],
            Calls(handler, "chat_manage").Select(s => s.Arguments["status"]!.GetValue<string>()));
        Assert.All(Calls(handler, "chat_manage"), s => Assert.Equal("status", s.Arguments["op"]!.GetValue<string>()));
        Assert.Equal(1, server.PostCalls);
    }

    [Fact]
    public async Task AFreshProcessAfterTheReplyPostsNothingMoreAndTheItemIsGone()
    {
        // One latched question, delivered by the very first consuming read and never re-delivered by the script:
        // only chat_ack can take it out of the inbox.
        var server = new FactoryServer { OnRead = n => n == 1 ? Item(2870, "did the ack land?", thread: 2869) : null };
        using var tmp = new TempWorkspace();
        WriteModel(tmp);
        WriteFactorySettings(tmp);

        using var first = server.Handler();
        var firstStream = new ScriptedStream().Then(Fakes.Text("it did"));
        var (firstCode, _, firstErr) = await Run(["-p", "--no-session", "ping"], tmp, first, firstStream);

        Assert.Equal(0, firstCode);
        Assert.Equal(
            ["factory: final-text posted to thread 2869, 0 secondary target(s) skipped (task for message 2870, acknowledged 2870)"],
            Lines(firstErr));
        var post = Assert.Single(Calls(first, "chat_post"));
        Assert.Equal(2869, post.Arguments["thread"]!.GetValue<long>());
        // The assertion a local-only dedupe would fail: the server's digest no longer carries the item.
        Assert.Empty(server.Unhandled);

        using var second = server.Handler();
        var secondStream = new ScriptedStream().Then(Fakes.Text("nothing to answer"));
        var (secondCode, secondOut, secondErr) = await Run(["-p", "--no-session", "ping"], tmp, second, secondStream);

        Assert.Equal(0, secondCode);
        Assert.Empty(Lines(secondErr));
        Assert.Contains("nothing to answer", secondOut, StringComparison.Ordinal);
        Assert.Equal(1, secondStream.Calls);
        Assert.Empty(Calls(second, "chat_post"));
        // Exactly one chat_post for message 2870 across both processes.
        Assert.Equal(1, server.PostCalls);
        Assert.Empty(server.Unhandled);
    }

    [Fact]
    public async Task RoomTrafficAddressedToNobodyNeverStartsATurn()
    {
        var server = new FactoryServer { RoomTraffic = true };
        using var handler = server.Handler();
        using var tmp = new TempWorkspace();
        WriteModel(tmp);
        WriteFactorySettings(tmp);
        var stream = new ScriptedStream().Then(Fakes.Text("only the prompt"));
        var (code, stdout, stderr) = await Run(["-p", "--no-session", "ping"], tmp, handler, stream);

        Assert.Equal(0, code);
        Assert.Empty(Lines(stderr));
        Assert.Contains("only the prompt", stdout, StringComparison.Ordinal);
        Assert.Equal(1, stream.Calls);
        Assert.Empty(Calls(handler, "chat_post"));
        Assert.Equal(0, server.PostCalls);
    }

    [Fact]
    public async Task DroppedInboxItemsAreReportedOnStderr()
    {
        // The item's two ids disagree, so the parser refuses it - and says so instead of dropping it silently.
        var ambiguous = Item(2860, "which id is this?", thread: 2859);
        ambiguous["messageId"] = 9999;
        var server = new FactoryServer { OnRead = n => n == 1 ? ambiguous.DeepClone().AsObject() : null };
        using var handler = server.Handler();
        using var tmp = new TempWorkspace();
        WriteModel(tmp);
        WriteFactorySettings(tmp);
        var stream = new ScriptedStream().Then(Fakes.Text("only the prompt"));
        var (code, stdout, stderr) = await Run(["-p", "--no-session", "ping"], tmp, handler, stream);

        Assert.Equal(0, code);
        Assert.Contains("factory: dropped inbox item inboxId=", stderr, StringComparison.Ordinal);
        Assert.Contains("messageId=9999", stderr, StringComparison.Ordinal);
        // Refused, not delivered: no extra turn and nothing posted back.
        Assert.Equal(1, stream.Calls);
        Assert.Contains("only the prompt", stdout, StringComparison.Ordinal);
        Assert.Equal(0, server.PostCalls);
    }

    [Fact]
    public async Task EachInboxTaskStartsFromAnEmptyContextButMidTaskArrivalsDoNot()
    {
        var aDelivered = false;
        var midDelivered = false;
        var server = new FactoryServer
        {
            OnWait = n => n switch
            {
                1 => Mark(Item(101, "task A: check the branch", thread: 201), () => aDelivered = true),
                2 => Item(103, "task B: read the log", thread: 203),
                3 => Item(104, "task C: summarise the phase", thread: 204),
                _ => null,
            },
            // One mid-task arrival, on the first steering poll after task A started.
            OnRead = _ =>
            {
                if (!aDelivered || midDelivered) return null;
                midDelivered = true;
                // reason mention: only dm and mention join a task now, a reply item would be ignored and acked.
                return Item(102, "and also mention the tag", thread: 201);
            },
        };
        using var handler = server.Handler();
        using var tmp = new TempWorkspace();
        WriteModel(tmp);
        WriteFactorySettings(tmp);
        var stream = new ScriptedStream()
            .Then(Fakes.Text("ack"))
            .Then(Fakes.ToolCalls(Fakes.Call("c1", "test_tool")))
            .Then(Fakes.Text("A done"))
            .Then(Fakes.Text("B done"))
            .Then(Fakes.Text("C done"));
        var (code, _, stderr) = await Run(
            ["-p", "--no-session", "ping"], tmp, handler, stream, [new DelegateTool()]);

        Assert.Equal(0, code);
        Assert.Equal(
            [
                "factory: final-text posted to thread 201, 0 secondary target(s) skipped (task for message 101, acknowledged 101, 102)",
                "factory: final-text posted to thread 203, 0 secondary target(s) skipped (task for message 103, acknowledged 103)",
                "factory: final-text posted to thread 204, 0 secondary target(s) skipped (task for message 104, acknowledged 104)",
            ],
            Lines(stderr));
        Assert.Equal(5, stream.Calls);

        // Mid-task arrival: task A's second request still carries its first message and the tool round trip.
        var midTask = stream.Contexts[2];
        Assert.Contains(midTask.Messages, m => m is UserMessage u && u.Text.Contains("task A", StringComparison.Ordinal));
        Assert.Contains(midTask.Messages, m => m is UserMessage u && u.Text.Contains("and also mention the tag", StringComparison.Ordinal));
        Assert.Contains(midTask.Messages, m => m is ToolResultMessage);

        // Third inbox task: system prompt and tools kept, everything else dropped.
        var third = stream.Contexts[4];
        Assert.NotEmpty(third.SystemPrompt);
        Assert.Contains(third.Tools, t => t.Name == "test_tool");
        var only = Assert.IsType<UserMessage>(Assert.Single(third.Messages));
        Assert.Contains("task C: summarise the phase", only.Text, StringComparison.Ordinal);
        Assert.Contains("message 104", only.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("task A", only.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(third.Messages, m => m is UserMessage u && u.Text.Contains("task B", StringComparison.Ordinal));

        var threads = Calls(handler, "chat_post").Select(p => p.Arguments["thread"]!.GetValue<long>()).ToList();
        Assert.Equal([201, 203, 204], threads);
    }

    // ------------------------------------------------------------------ preflight ladder

    [Fact]
    public async Task SlugMismatchRefusesToRun()
    {
        var server = new FactoryServer { Slug = "someone-else" };
        using var handler = server.Handler();
        using var tmp = new TempWorkspace();
        WriteModel(tmp);
        WriteFactorySettings(tmp);
        var stream = new ScriptedStream().Then(Fakes.Text("never"));
        var (code, _, stderr) = await Run(["-p", "--no-session", "ping"], tmp, handler, stream);

        Assert.Equal(3, code);
        Assert.Contains("@someone-else", stderr, StringComparison.Ordinal);
        Assert.Contains("@coder-x", stderr, StringComparison.Ordinal);
        Assert.Equal(0, stream.Calls);
        Assert.Empty(Posts(handler));
        Assert.Equal(0, server.PostCalls);
    }

    [Fact]
    public async Task UnreachableKnowledgeBaseFallsBackToALocalRun()
    {
        var server = new FactoryServer { WhoAmIThrows = true };
        using var handler = server.Handler();
        using var tmp = new TempWorkspace();
        WriteModel(tmp);
        WriteFactorySettings(tmp);
        var stream = new ScriptedStream().Then(Fakes.Text("local answer"));
        var (code, stdout, stderr) = await Run(["-p", "--no-session", "ping"], tmp, handler, stream);

        Assert.Equal(0, code);
        Assert.Contains("local answer", stdout, StringComparison.Ordinal);
        Assert.Equal(1, stream.Calls);
        var line = Assert.Single(Lines(stderr));
        Assert.StartsWith("factory:", line, StringComparison.Ordinal);
        Assert.Contains("http://kb.test/whoami", line, StringComparison.Ordinal);
        Assert.Empty(Posts(handler));
    }

    [Fact]
    public async Task RejectedTokenFallsBackToALocalRun()
    {
        var server = new FactoryServer { WhoAmIStatus = HttpStatusCode.Unauthorized };
        using var handler = server.Handler();
        using var tmp = new TempWorkspace();
        WriteModel(tmp);
        WriteFactorySettings(tmp);
        var stream = new ScriptedStream().Then(Fakes.Text("local answer"));
        var (code, stdout, stderr) = await Run(["-p", "--no-session", "ping"], tmp, handler, stream);

        Assert.Equal(0, code);
        Assert.Contains("local answer", stdout, StringComparison.Ordinal);
        Assert.Equal(1, stream.Calls);
        var line = Assert.Single(Lines(stderr));
        Assert.StartsWith("factory:", line, StringComparison.Ordinal);
        Assert.Contains("rejected", line, StringComparison.Ordinal);
        Assert.Contains("http://kb.test/whoami", line, StringComparison.Ordinal);
        Assert.DoesNotContain("factory-token", stderr, StringComparison.Ordinal);
        Assert.Empty(Posts(handler));
    }

    [Fact]
    public async Task FailedEgressProbeFallsBackToALocalRun()
    {
        var server = new FactoryServer { ProbeFails = true };
        using var handler = server.Handler();
        using var tmp = new TempWorkspace();
        WriteModel(tmp);
        WriteFactorySettings(tmp);
        var stream = new ScriptedStream().Then(Fakes.Text("local answer"));
        var (code, stdout, stderr) = await Run(["-p", "--no-session", "ping"], tmp, handler, stream);

        Assert.Equal(0, code);
        Assert.Contains("local answer", stdout, StringComparison.Ordinal);
        Assert.Equal(1, stream.Calls);
        var line = Assert.Single(Lines(stderr));
        Assert.StartsWith("factory:", line, StringComparison.Ordinal);
        Assert.Contains("chat unreachable, running locally", line, StringComparison.Ordinal);
        Assert.Equal(1, server.ProbeCalls);
        Assert.Empty(Calls(handler, "chat_post"));
    }

    [Fact]
    public async Task EgressProbeIsTheFirstChatCallAndMarksNothingRead()
    {
        var server = new FactoryServer();
        using var handler = server.Handler();
        using var tmp = new TempWorkspace();
        WriteModel(tmp);
        WriteFactorySettings(tmp);
        var (code, _, stderr) = await Run(["-p", "--no-session", "ping"], tmp, handler);

        Assert.Equal(0, code);
        Assert.Empty(Lines(stderr));
        var probe = Posts(handler)[0];
        Assert.Equal("chat_read", probe.Tool);
        Assert.False(probe.Arguments["mark_read"]!.GetValue<bool>());
        Assert.Equal("mark_read", Assert.Single(probe.Arguments).Key);
        Assert.Equal(1, server.WhoAmICalls);
        Assert.Equal(1, server.ProbeCalls);
    }

    [Fact]
    public async Task WithoutAFactoryBlockNothingIsVerified()
    {
        var server = new FactoryServer();
        using var handler = server.Handler();
        using var tmp = new TempWorkspace();
        WriteModel(tmp);
        var stream = new ScriptedStream().Then(Fakes.Text("local answer"));
        var (code, stdout, stderr) = await Run(["-p", "--no-session", "ping"], tmp, handler, stream);

        Assert.Equal(0, code);
        Assert.Contains("local answer", stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("factory:", stderr, StringComparison.Ordinal);
        Assert.Equal(0, server.WhoAmICalls);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void McpStatusShowsTheVerifiedActor()
    {
        var session = SessionManager.InMemory("/tmp");
        var context = new AgentContext();
        var state = new CliState
        {
            Cwd = "/tmp",
            Model = Fakes.Model,
            Thinking = ThinkingLevel.Off,
            Models = [Fakes.Model],
            Skills = [],
            Templates = [],
            Mcp = new McpManager(),
            Agent = new AgentSession(session, context),
            Context = context,
            Settings = new Settings(),
            Stream = new ScriptedStream().Then(Fakes.Text("ok")).Stream,
            FactoryActor = Actor,
        };

        var message = Assert.IsType<SlashMessage>(SlashCommands.Handle("/mcp", state));
        Assert.Contains("factory  @coder-x verified", message.Text, StringComparison.Ordinal);

        var local = Assert.IsType<SlashMessage>(SlashCommands.Handle("/mcp", state with { FactoryActor = null }));
        Assert.DoesNotContain("factory", local.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(400, "Failed to load image: image file is truncated (0 bytes not processed)", true)]
    [InlineData(400, "invalid_image: could not decode image", true)]
    [InlineData(400, "invalid request parameter", false)]
    [InlineData(500, "Failed to load image", false)]
    public async Task DeterministicImage400DoesNotResumeUnchanged(int status, string error, bool deterministic)
    {
        var server = new FactoryServer { OnRead = n => n == 1 ? Item(2990, "inspect image", thread: 2989) : null };
        using var handler = server.Handler();
        using var tmp = new TempWorkspace();
        WriteModel(tmp);
        WriteFactorySettings(tmp);
        var image = new ImageContent(Convert.ToBase64String([1, 2, 3]), "image/unknown-format");
        var tool = new DelegateTool { Name = "shot", Execute = (_, _, _, _) => Task.FromResult(new AgentToolResult([image])) };
        var stream = new ScriptedStream().Then(Fakes.ToolCalls(Fakes.Call("uncaught-image-ref", "shot")))
            .Then(Fakes.Error(error) with { ProviderHttpStatus = status, ProviderRequestFailed = true })
            .Then(Fakes.Text("recovered"));
        var stderr = new StringWriter();
        var host = new CliHost
        {
            Out = new StringWriter(), Err = stderr, In = new StringReader(""), ConfigDir = tmp.Path,
            Color = false, HookCancelKey = false, FactoryHandler = handler,
            StreamFactory = _ => stream.Stream, ToolsFactory = (_, _) => [tool],
            ModelErrorBackoff = TimeSpan.Zero,
        };
        var code = await host.RunAsync(["-p", "inspect"]);
        Assert.Equal(deterministic ? 1 : 0, code);
        Assert.Equal(deterministic ? 2 : 3, stream.Calls);
        if (deterministic)
        {
            Assert.Contains("not retrying", stderr.ToString());
            Assert.Contains("uncaught-image-ref", stderr.ToString());
            Assert.DoesNotContain("resuming (", stderr.ToString());
            Assert.NotEmpty(Directory.GetFiles(Path.Combine(tmp.Path, "sessions"), "*.jsonl", SearchOption.AllDirectories));
        }
    }

    // plan-pi-sharp-codex item 1: until 2026-10-25 only the reviewer's sub-run had a guard; a seat's own calls ran unchecked.
    [Fact]
    public async Task AnAttachedSeatsGuardRefusesAForbiddenCommandWithItsAlternative()
    {
        var server = new FactoryServer { OnRead = n => n == 1 ? Item(2991, "push it", thread: 2989) : null };
        using var handler = server.Handler();
        using var tmp = new TempWorkspace();
        WriteModel(tmp);
        WriteFactorySettings(tmp);
        var ran = false;
        var tool = new DelegateTool
        {
            Name = "bash",
            Parameters = JsonNode.Parse("""{"type":"object","properties":{"command":{"type":"string"}},"required":["command"]}""")!.AsObject(),
            Execute = (_, _, _, _) => { ran = true; return Task.FromResult(AgentToolResult.FromText("pushed")); },
        };
        var stream = new ScriptedStream()
            .Then(Fakes.ToolCalls(Fakes.Call("c1", "bash", new JsonObject { ["command"] = "git push origin main" })))
            .Then(Fakes.Text("done"));
        var stderr = new StringWriter();
        var host = new CliHost
        {
            Out = new StringWriter(), Err = stderr, In = new StringReader(""), ConfigDir = tmp.Path,
            Color = false, HookCancelKey = false, FactoryHandler = handler,
            StreamFactory = _ => stream.Stream, ToolsFactory = (_, _) => [tool],
        };
        await host.RunAsync(["-p", "push"]);
        Assert.False(ran);
        Assert.Contains("[guard] enforce:", stderr.ToString());
        var refusal = stream.Contexts.Last().Messages.OfType<ToolResultMessage>().Single();
        Assert.Contains("[push-to-master]", ContentText.Of(refusal.Content));
        Assert.Contains("Instead: push your own branch", ContentText.Of(refusal.Content));
    }

    // ------------------------------------------------------------------ helpers    // ------------------------------------------------------------------ helpers

    /// <summary>plan-pi-sharp-codex item 3: on an attached seat, by default, a command does not see the seat's token or any
    /// other secret-looking variable.</summary>
    [Fact]
    public async Task AnAttachedSeatsCommandsDoNotSeeItsSecretsByDefault()
    {
        if (!OperatingSystem.IsLinux()) return;
        var server = new FactoryServer { OnRead = n => n == 1 ? Item(2992, "check the env", thread: 2990) : null };
        using var handler = server.Handler();
        using var tmp = new TempWorkspace();
        WriteModel(tmp);
        WriteFactorySettings(tmp);
        var names = new[] { "PI_FACTORY_TOKEN", "HUB_API_KEY", "DB_PASSWORD" };
        foreach (var name in names) Environment.SetEnvironmentVariable(name, "s3cret-" + name);
        try
        {
            var stream = new ScriptedStream()
                .Then(Fakes.ToolCalls(Fakes.Call("c1", "bash", new JsonObject { ["command"] = "echo token=${PI_FACTORY_TOKEN:-absent} key=${HUB_API_KEY:-absent} pw=${DB_PASSWORD:-absent} home=${HOME:+set}" })))
                .Then(Fakes.Text("done"));
            var host = new CliHost
            {
                Out = new StringWriter(), Err = new StringWriter(), In = new StringReader(""), ConfigDir = tmp.Path,
                Color = false, HookCancelKey = false, FactoryHandler = handler,
                StreamFactory = _ => stream.Stream, ToolsFactory = (_, _) => [new PiSharp.Tools.BashTool(tmp.Path)],
            };
            await host.RunAsync(["-p", "check"]);
            var output = ContentText.Of(stream.Contexts.Last().Messages.OfType<ToolResultMessage>().Single().Content);
            Assert.Contains("token=absent key=absent pw=absent home=set", output);
            Assert.DoesNotContain("s3cret", output);
        }
        finally
        {
            foreach (var name in names) Environment.SetEnvironmentVariable(name, null);
        }
    }

    private static JsonObject Mark(JsonObject item, Action mark)
    {
        mark();
        return item;
    }

    private static JsonObject Item(
        long messageId,
        string body,
        long? thread = null,
        string reason = "mention",
        string kind = "question",
        string room = "pi-sharp",
        string author = "digital-scribe")
    {
        var message = new JsonObject
        {
            ["id"] = messageId,
            ["room"] = room,
            ["author"] = author,
            ["kind"] = kind,
            ["body"] = body,
            ["mentions"] = new JsonArray(Actor),
            ["about"] = new JsonArray(),
            ["at"] = "2026-09-14T16:41:00Z",
            ["replies"] = 0,
        };
        if (thread is not null) message["thread"] = thread.Value;
        return new JsonObject
        {
            ["inboxId"] = messageId,
            ["messageId"] = messageId,
            ["reason"] = reason,
            ["message"] = message,
        };
    }

    private static List<FakeRequest> Posts(FakeHandler handler)
    {
        lock (handler.Requests) return handler.Requests.Where(r => r.Method == "POST").ToList();
    }

    private static List<FakeRequest> Calls(FakeHandler handler, string tool) =>
        Posts(handler).Where(r => r.Tool == tool).ToList();

    private static List<long> AckIds(FakeRequest request) =>
        request.Arguments["message_ids"]!.AsArray().Select(n => n!.GetValue<long>()).ToList();

    /// <summary>The run's log lines, less the start-up facts every attached seat prints (its guard and its commands'
    /// environment), which these tests do not look at.</summary>
    private static string[] Lines(string stderr) =>
        [.. stderr.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Where(l => !l.StartsWith("[guard] ", StringComparison.Ordinal) && !l.StartsWith("[shell] ", StringComparison.Ordinal))];

    private static void WriteModel(TempWorkspace tmp)
    {
        File.WriteAllText(Path.Combine(tmp.Path, "models.json"), """
            {"models":[{"id":"test-model","baseUrl":"http://localhost:9/v1","apiKey":"dummy","contextWindow":32768,"maxOutputTokens":8192}]}
            """);
    }

    /// <summary>A factory block with the token in settings (fake literal) so the tests never touch the
    /// environment. idleExitSeconds bounds the attached run so the process exits on its own.</summary>
    private static void WriteFactorySettings(TempWorkspace tmp, string actor = Actor, int idleExitSeconds = 1)
    {
        File.WriteAllText(Path.Combine(tmp.Path, "settings.json"), new JsonObject
        {
            ["factory"] = new JsonObject
            {
                ["kbUrl"] = "http://kb.test",
                ["chatUrl"] = "http://chat.test",
                ["actor"] = actor,
                ["token"] = "factory-token",
                ["idleExitSeconds"] = idleExitSeconds,
            },
        }.ToJsonString());
    }

    private static async Task<(int Code, string Stdout, string Stderr)> Run(
        string[] argv,
        TempWorkspace tmp,
        HttpMessageHandler? factoryHandler = null,
        ScriptedStream? stream = null,
        IReadOnlyList<IAgentTool>? tools = null)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var host = new CliHost
        {
            Out = stdout,
            Err = stderr,
            In = new StringReader(""),
            Color = false,
            ConfigDir = tmp.Path,
            HookCancelKey = false,
            StreamFactory = _ => (stream ?? new ScriptedStream().Then(Fakes.Text("ok"))).Stream,
            ToolsFactory = (_, _) => tools ?? [],
            FactoryHandler = factoryHandler,
        };
        var code = await host.RunAsync(argv);
        return (code, stdout.ToString(), stderr.ToString());
    }
}
