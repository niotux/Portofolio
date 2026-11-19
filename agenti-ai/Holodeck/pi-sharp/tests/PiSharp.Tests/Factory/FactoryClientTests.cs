// FactoryClient against an in-memory HttpMessageHandler: no sockets, no server, no real token.
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using PiSharp.Core.Factory;

namespace PiSharp.Tests.Factory;

/// <summary>One recorded outgoing request, flattened so assertions read like the wire.</summary>
internal sealed record FakeRequest(string Method, Uri Uri, string? Authorization, string Accept, string? ProtocolVersion, string Body)
{
    public JsonObject Envelope => (JsonObject)JsonNode.Parse(Body)!;

    public string Tool => Envelope["params"]!["name"]!.GetValue<string>();

    public JsonObject Arguments => (JsonObject)Envelope["params"]!["arguments"]!;
}

/// <summary>Answers from a supplied responder and records what was sent. The responder's task is awaited under the
/// caller's token so a never-completing one still honours the client's per-request deadline.</summary>
internal sealed class FakeHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _responder;

    public FakeHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) => _responder = responder;

    public List<FakeRequest> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        var recorded = new FakeRequest(
            request.Method.Method,
            request.RequestUri!,
            request.Headers.Authorization?.ToString(),
            request.Headers.Accept.ToString(),
            request.Headers.TryGetValues("MCP-Protocol-Version", out var v) ? string.Join(",", v) : null,
            body);
        lock (Requests) Requests.Add(recorded);
        return await _responder(request).WaitAsync(cancellationToken);
    }
}

public class FactoryClientTests
{
    [Fact]
    public async Task FullSourceReadUsesExactRoomCursorWithoutAcknowledging()
    {
        var body = new string('x', 400) + " Keep the pending commitment";
        using var handler = Responding("application/json", HttpStatusCode.OK, RpcOk(new JsonObject
        { ["messages"] = new JsonArray(new JsonObject { ["id"] = 55, ["body"] = body }) }.ToJsonString()));
        using var client = Client(handler);
        var source = new FactoryMessage(55, "board", 40, "operator", "note", "preview", [], "", true);
        var full = await client.ReadFullMessageAsync(source, default);
        Assert.Equal(body, full!.Body); Assert.False(full.HeadOnly);
        var args = Assert.Single(handler.Requests).Arguments;
        Assert.Equal("board", args["room"]!.GetValue<string>());
        Assert.Equal(54L, args["since"]!.GetValue<long>());
        Assert.Equal(1, args["limit"]!.GetValue<int>());
        Assert.False(args["mark_read"]!.GetValue<bool>());
    }

    [Fact]
    public async Task MalformedAcceptanceMetadataDoesNotCrashBudgetDiscovery()
    {
        using var handler = Responding("application/json", HttpStatusCode.OK,
            RpcOk("""{"kind":"work_item","attrs":{"type":"epic","acceptance_criteria":[{"id":"missing-text"}]}}"""));
        using var client = Client(handler);
        Assert.Null(await client.KbHandInAsync("epic"));
    }

    private const string KbUrl = "http://kb.test";
    private const string ChatUrl = "http://chat.test";
    private const string Token = "factory-token";

    private static FactoryClient Client(FakeHandler handler, TimeSpan? timeout = null, TimeSpan? noteDeadline = null) =>
        new(KbUrl, ChatUrl, Token, handler, timeout, noteDeadline);

    private static FakeHandler Responding(string mediaType, HttpStatusCode status, string body) =>
        new(_ => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, mediaType),
        }));

    private static FakeHandler Json(HttpStatusCode status, string body) => Responding("application/json", status, body);

    private static string RpcOk(string toolText, bool isError = false) =>
        new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = 1,
            ["result"] = new JsonObject
            {
                ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = toolText }),
                ["isError"] = isError,
            },
        }.ToJsonString();

    private static JsonObject InboxItem(long inboxId = 123, long messageId = 2965, string reason = "mention", long? thread = 2940)
    {
        var message = new JsonObject
        {
            ["id"] = messageId,
            ["room"] = "ops",
            ["author"] = "claude-agent-maker-1",
            ["kind"] = "question",
            ["body"] = "ping @coder-06-pi1",
            ["mentions"] = new JsonArray("coder-06-pi1"),
            ["about"] = new JsonArray(),
            ["at"] = "2026-09-14T16:41:00Z",
            ["resolvedBy"] = null,
            ["replies"] = 0,
        };
        if (thread is not null) message["thread"] = thread.Value;
        return new JsonObject
        {
            ["inboxId"] = inboxId,
            ["messageId"] = messageId,
            ["reason"] = reason,
            ["message"] = message,
        };
    }

    private static string DigestText(JsonObject? item, bool? timedOut = null)
    {
        var obj = new JsonObject
        {
            ["inbox"] = item is null ? new JsonArray() : new JsonArray(item),
            ["rooms"] = new JsonArray(),
            ["reachable"] = new JsonArray(),
        };
        if (timedOut is not null) obj["timedOut"] = timedOut.Value;
        return obj.ToJsonString();
    }

    // ---------------------------------------------------------------- whoami

    [Fact]
    public async Task WhoAmIParsesSlugAndName()
    {
        using var handler = Json(HttpStatusCode.OK, """{"slug":"coder-06-pi1","name":"Coder 6","writableKinds":["work_item"]}""");
        using var client = Client(handler);
        var who = await client.WhoAmIAsync();
        Assert.Equal(FactoryFailure.None, who.Failure);
        Assert.NotNull(who.Identity);
        Assert.Equal("coder-06-pi1", who.Identity.Slug);
        Assert.Equal("Coder 6", who.Identity.Name);
        var req = Assert.Single(handler.Requests);
        Assert.Equal("GET", req.Method);
        Assert.Equal("http://kb.test/whoami", req.Uri.ToString());
        Assert.Equal("Bearer factory-token", req.Authorization);
        Assert.Equal(new Uri("http://kb.test/whoami"), client.WhoAmIEndpoint);
        Assert.Equal(new Uri("http://chat.test/mcp"), client.ChatEndpoint);
    }

    [Fact]
    public async Task WhoAmIUnauthorizedOn401()
    {
        using var handler = Json(HttpStatusCode.Unauthorized, """{"error":"no"}""");
        using var client = Client(handler);
        var who = await client.WhoAmIAsync();
        Assert.Equal(FactoryFailure.Unauthorized, who.Failure);
        Assert.Null(who.Identity);
    }

    [Fact]
    public async Task WhoAmIUnreachableOnConnectionException()
    {
        using var handler = new FakeHandler(_ => throw new HttpRequestException("connection refused"));
        using var client = Client(handler);
        var who = await client.WhoAmIAsync();
        Assert.Equal(FactoryFailure.Unreachable, who.Failure);
        Assert.Null(who.Identity);
    }

    [Fact]
    public async Task WhoAmIUnreachableOnServerError()
    {
        using var handler = Json(HttpStatusCode.InternalServerError, "boom");
        using var client = Client(handler);
        Assert.Equal(FactoryFailure.Unreachable, (await client.WhoAmIAsync()).Failure);
    }

    [Fact]
    public async Task WhoAmIBadResponseOnMalformedBody()
    {
        using var handler = Json(HttpStatusCode.OK, "not json at all");
        using var client = Client(handler);
        var who = await client.WhoAmIAsync();
        Assert.Equal(FactoryFailure.BadResponse, who.Failure);
        Assert.Null(who.Identity);
    }

    // ---------------------------------------------------------------- chat_read

    [Fact]
    public async Task ReadInboxCallsChatReadAndParsesItem()
    {
        using var handler = Json(HttpStatusCode.OK, RpcOk(DigestText(InboxItem())));
        using var client = Client(handler);
        var digest = await client.ReadInboxAsync(0, markRead: false);

        Assert.NotNull(digest);
        Assert.False(digest.TimedOut);
        var item = Assert.Single(digest.Inbox);
        Assert.Equal(123, item.InboxId);
        Assert.Equal(2965, item.MessageId);
        Assert.Equal("mention", item.Reason);
        Assert.Equal(2965, item.Message.Id);
        Assert.Equal("ops", item.Message.Room);
        Assert.Equal(2940, item.Message.Thread);
        Assert.Equal("claude-agent-maker-1", item.Message.Author);
        Assert.Equal("question", item.Message.Kind);
        Assert.Equal("ping @coder-06-pi1", item.Message.Body);
        Assert.Equal("coder-06-pi1", Assert.Single(item.Message.Mentions));
        Assert.Equal("2026-09-14T16:41:00Z", item.Message.At);

        var req = Assert.Single(handler.Requests);
        Assert.Equal("POST", req.Method);
        Assert.Equal("http://chat.test/mcp", req.Uri.ToString());
        Assert.Equal("Bearer factory-token", req.Authorization);
        Assert.Equal("application/json, text/event-stream", req.Accept);
        Assert.Equal("2025-06-18", req.ProtocolVersion);
        Assert.Equal("2.0", req.Envelope["jsonrpc"]!.GetValue<string>());
        Assert.Equal("tools/call", req.Envelope["method"]!.GetValue<string>());
        Assert.Equal(1, req.Envelope["id"]!.GetValue<long>());
        Assert.Equal("chat_read", req.Tool);
        Assert.False(req.Arguments["mark_read"]!.GetValue<bool>());
        Assert.False(req.Arguments.ContainsKey("room"));
        Assert.False(req.Arguments.ContainsKey("thread"));
        Assert.False(req.Arguments.ContainsKey("since"));
    }

    [Fact]
    public async Task ReadInboxLeavesThreadNullWhenAbsent()
    {
        using var handler = Json(HttpStatusCode.OK, RpcOk(DigestText(InboxItem(reason: "dm", thread: null))));
        using var client = Client(handler);
        var digest = await client.ReadInboxAsync(0, markRead: true);
        var item = Assert.Single(digest!.Inbox);
        Assert.Null(item.Message.Thread);
        Assert.Equal("dm", item.Reason);
        Assert.True(Assert.Single(handler.Requests).Arguments["mark_read"]!.GetValue<bool>());
    }

    [Fact]
    public async Task ReadInboxIncrementsJsonRpcIds()
    {
        using var handler = Json(HttpStatusCode.OK, RpcOk(DigestText(null)));
        using var client = Client(handler);
        await client.ReadInboxAsync(0, markRead: false);
        await client.ReadInboxAsync(0, markRead: false);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(1, handler.Requests[0].Envelope["id"]!.GetValue<long>());
        Assert.Equal(2, handler.Requests[1].Envelope["id"]!.GetValue<long>());
    }

    // ---------------------------------------------------------------- chat_wait

    [Fact]
    public async Task ReadInboxWaitsThenReadsWhenItemsArrived()
    {
        var bodies = new Queue<string>(
        [
            RpcOk(DigestText(InboxItem(), timedOut: false)),
            RpcOk(DigestText(InboxItem(inboxId: 124))),
        ]);
        using var handler = new FakeHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(bodies.Dequeue(), Encoding.UTF8, "application/json"),
        }));
        using var client = Client(handler);
        var digest = await client.ReadInboxAsync(30, markRead: true);

        Assert.NotNull(digest);
        Assert.False(digest.TimedOut);
        Assert.Equal(124, Assert.Single(digest.Inbox).InboxId);

        Assert.Equal(2, handler.Requests.Count);
        var wait = handler.Requests[0];
        Assert.Equal("chat_wait", wait.Tool);
        Assert.Equal(30, wait.Arguments["timeout_seconds"]!.GetValue<int>());
        Assert.Equal("inbox", wait.Arguments["scope"]!.GetValue<string>());
        Assert.False(wait.Arguments.ContainsKey("mark_read"));
        var read = handler.Requests[1];
        Assert.Equal("chat_read", read.Tool);
        Assert.True(read.Arguments["mark_read"]!.GetValue<bool>());
        Assert.False(read.Arguments.ContainsKey("room"));
    }

    [Fact]
    public async Task ReadInboxDoesNotFollowUpWhenWaitTimedOut()
    {
        using var handler = Json(HttpStatusCode.OK, RpcOk(DigestText(null, timedOut: true)));
        using var client = Client(handler);
        var digest = await client.ReadInboxAsync(30, markRead: true);
        Assert.NotNull(digest);
        Assert.True(digest.TimedOut);
        Assert.Empty(digest.Inbox);
        Assert.Equal("chat_wait", Assert.Single(handler.Requests).Tool);
    }

    [Fact]
    public async Task ReadInboxClampsWaitSeconds()
    {
        using var handler = Json(HttpStatusCode.OK, RpcOk(DigestText(null, timedOut: true)));
        using var client = Client(handler);
        await client.ReadInboxAsync(5000, markRead: false);
        Assert.Equal(FactoryClient.MaxWaitSeconds, Assert.Single(handler.Requests).Arguments["timeout_seconds"]!.GetValue<int>());
    }

    // ---------------------------------------------------------------- transport shapes

    [Fact]
    public async Task ReadInboxParsesSseBodyAndTakesLastEvent()
    {
        var stale = RpcOk(DigestText(InboxItem(inboxId: 1, messageId: 1)));
        var fresh = RpcOk(DigestText(InboxItem(inboxId: 9, messageId: 2965)));
        var sse = "data: " + stale + "\n\ndata: " + fresh + "\n\n";
        using var handler = Responding("text/event-stream", HttpStatusCode.OK, sse);
        using var client = Client(handler);
        var digest = await client.ReadInboxAsync(0, markRead: false);
        Assert.Equal(9, Assert.Single(digest!.Inbox).InboxId);
    }

    [Fact]
    public async Task ReadInboxNullWhenToolReportsIsError()
    {
        using var handler = Json(HttpStatusCode.OK, RpcOk("Unauthenticated request.", isError: true));
        using var client = Client(handler);
        Assert.Null(await client.ReadInboxAsync(0, markRead: false));
    }

    [Fact]
    public async Task ReadInboxNullOnJsonRpcErrorFrame()
    {
        using var handler = Json(HttpStatusCode.OK, """{"jsonrpc":"2.0","id":1,"error":{"code":-32601,"message":"no such tool"}}""");
        using var client = Client(handler);
        Assert.Null(await client.ReadInboxAsync(0, markRead: false));
    }

    [Fact]
    public async Task ReadInboxNullOnHttp500()
    {
        using var handler = Json(HttpStatusCode.InternalServerError, "nope");
        using var client = Client(handler);
        Assert.Null(await client.ReadInboxAsync(0, markRead: false));
    }

    [Fact]
    public async Task ReadInboxNullWhenHandlerThrows()
    {
        using var handler = new FakeHandler(_ => throw new HttpRequestException("socket closed"));
        using var client = Client(handler);
        Assert.Null(await client.ReadInboxAsync(0, markRead: false));
    }

    [Fact]
    public async Task ReadInboxNullWhenResponseNeverArrives()
    {
        var never = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new FakeHandler(_ => never.Task);
        using var client = Client(handler, TimeSpan.FromMilliseconds(200));
        var stopwatch = Stopwatch.StartNew();
        Assert.Null(await client.ReadInboxAsync(0, markRead: false));
        stopwatch.Stop();
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(3), $"deadline not honoured: {stopwatch.Elapsed}");
        never.TrySetCanceled();
    }

    // ---------------------------------------------------------------- chat_post

    [Fact]
    public async Task PostSendsBodyAndThreadOnly()
    {
        using var handler = Json(HttpStatusCode.OK, RpcOk("""{"id":2966,"room":"ops"}"""));
        using var client = Client(handler);
        Assert.True(await client.PostAsync("on it", thread: 2940));
        var req = Assert.Single(handler.Requests);
        Assert.Equal("chat_post", req.Tool);
        Assert.Equal("on it", req.Arguments["body"]!.GetValue<string>());
        Assert.Equal(2940, req.Arguments["thread"]!.GetValue<long>());
        Assert.False(req.Arguments.ContainsKey("to"));
        Assert.False(req.Arguments.ContainsKey("kind"));
        Assert.False(req.Arguments.ContainsKey("room"));
    }

    [Fact]
    public async Task PostSendsDirectMessageWithKind()
    {
        using var handler = Json(HttpStatusCode.OK, RpcOk("""{"id":2967}"""));
        using var client = Client(handler);
        Assert.True(await client.PostAsync("done", to: "coder-06-pi2", kind: "answer"));
        var req = Assert.Single(handler.Requests);
        Assert.Equal("coder-06-pi2", req.Arguments["to"]!.GetValue<string>());
        Assert.Equal("answer", req.Arguments["kind"]!.GetValue<string>());
        Assert.False(req.Arguments.ContainsKey("thread"));
        Assert.False(req.Arguments.ContainsKey("room"));
    }

    [Fact]
    public async Task SetStatusSendsChatManageOpStatus()
    {
        using var handler = Json(HttpStatusCode.OK, RpcOk("""{"ok":true}"""));
        using var client = Client(handler);
        Assert.True(await client.SetStatusAsync("idle — last: message 43 in ops"));
        var req = Assert.Single(handler.Requests);
        Assert.Equal("chat_manage", req.Tool);
        Assert.Equal("status", req.Arguments["op"]!.GetValue<string>());
        Assert.Equal("idle — last: message 43 in ops", req.Arguments["status"]!.GetValue<string>());
        Assert.False(req.Arguments.ContainsKey("body"));
        Assert.False(req.Arguments.ContainsKey("room"));
    }

    [Fact]
    public async Task SetStatusNullClearsTheLine()
    {
        using var handler = Json(HttpStatusCode.OK, RpcOk("""{"ok":true}"""));
        using var client = Client(handler);
        Assert.True(await client.SetStatusAsync(null));
        var req = Assert.Single(handler.Requests);
        Assert.Equal("status", req.Arguments["op"]!.GetValue<string>());
        Assert.False(req.Arguments.ContainsKey("status"));
    }

    [Fact]
    public async Task SetStatusFalseWhenToolReportsIsError()
    {
        using var handler = Json(HttpStatusCode.OK, RpcOk("unknown op", isError: true));
        using var client = Client(handler);
        Assert.False(await client.SetStatusAsync("idle"));
    }

    [Fact]
    public async Task PostFalseWhenToolReportsIsError()
    {
        using var handler = Json(HttpStatusCode.OK, RpcOk("Target exactly one of room, thread or to.", isError: true));
        using var client = Client(handler);
        Assert.False(await client.PostAsync("oops"));
    }

    [Fact]
    public async Task PostFalseWhenHandlerThrows()
    {
        using var handler = new FakeHandler(_ => throw new HttpRequestException("down"));
        using var client = Client(handler);
        Assert.False(await client.PostAsync("hello", thread: 1));
    }

    // ------------------------------------------- chat_post: post detail (refused-reply slice a)

    [Fact]
    public async Task PostWithDetailSurfacesRefusalStatusAndBodyOn400()
    {
        using var handler = Json(HttpStatusCode.BadRequest, "625280 is not in this conversation");
        using var client = Client(handler);
        var result = await client.PostWithDetailAsync("hello", thread: 1);
        Assert.Equal(FactoryPostOutcome.Refusal, result.Outcome);
        Assert.Equal(400, result.StatusCode);
        Assert.Equal("625280 is not in this conversation", result.Body);
        Assert.NotNull(result.Error);
        Assert.False(result.Ok);
        var req = Assert.Single(handler.Requests);
        Assert.Equal("chat_post", req.Tool);
        Assert.Equal("hello", req.Arguments["body"]!.GetValue<string>());
    }

    [Fact]
    public async Task PostWithDetailSurfacesRefusalStatusAndBodyOn409()
    {
        using var handler = Json(HttpStatusCode.Conflict, "stale inbox row");
        using var client = Client(handler);
        var result = await client.PostWithDetailAsync("again", to: "team-lead-2");
        Assert.Equal(FactoryPostOutcome.Refusal, result.Outcome);
        Assert.Equal(409, result.StatusCode);
        Assert.Equal("stale inbox row", result.Body);
        Assert.False(result.Ok);
    }

    [Fact]
    public async Task PostWithDetailKeeps500Transient()
    {
        using var handler = Json(HttpStatusCode.InternalServerError, "hub restarting");
        using var client = Client(handler);
        var result = await client.PostWithDetailAsync("hello", thread: 1);
        Assert.Equal(FactoryPostOutcome.Transient, result.Outcome);
        Assert.Equal(500, result.StatusCode);
        Assert.Equal("hub restarting", result.Body);
        Assert.False(result.Ok);
    }

    [Fact]
    public async Task PostWithDetailKeeps429Transient()
    {
        using var handler = Json((HttpStatusCode)429, "slow down");
        using var client = Client(handler);
        var result = await client.PostWithDetailAsync("hello", thread: 1);
        Assert.Equal(FactoryPostOutcome.Transient, result.Outcome);
        Assert.Equal(429, result.StatusCode);
        Assert.Equal("slow down", result.Body);
        Assert.False(result.Ok);
    }

    [Fact]
    public async Task PostWithDetailReportsTimeoutAsTransient()
    {
        var never = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new FakeHandler(_ => never.Task);
        using var client = Client(handler, TimeSpan.FromMilliseconds(200));
        var result = await client.PostWithDetailAsync("hello", thread: 1);
        Assert.Equal(FactoryPostOutcome.Transient, result.Outcome);
        Assert.Null(result.StatusCode);
        Assert.NotNull(result.Error);
        Assert.False(result.Ok);
        never.TrySetCanceled();
    }

    [Fact]
    public async Task PostWithDetailSuccessIsUnchanged()
    {
        using var handler = Json(HttpStatusCode.OK, RpcOk("""{"id":2966,"room":"ops"}"""));
        using var client = Client(handler);
        var result = await client.PostWithDetailAsync("on it", thread: 2940);
        Assert.Equal(FactoryPostOutcome.Success, result.Outcome);
        Assert.True(result.Ok);
        Assert.Equal(200, result.StatusCode);
        Assert.Null(result.Body);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task PostAsyncStillFalseWhenServerRefuses()
    {
        using var handler = Json(HttpStatusCode.BadRequest, "625280 is not in this conversation");
        using var client = Client(handler);
        Assert.False(await client.PostAsync("hello", thread: 1));
    }

    [Fact]
    public async Task PostWithDetailToolIsErrorIsRefusalCarryingToolText()
    {
        using var handler = Json(HttpStatusCode.OK, RpcOk("Target exactly one of room, thread or to.", isError: true));
        using var client = Client(handler);
        var result = await client.PostWithDetailAsync("oops");
        Assert.Equal(FactoryPostOutcome.Refusal, result.Outcome);
        Assert.Equal(200, result.StatusCode);
        Assert.Equal("Target exactly one of room, thread or to.", result.Body);
        Assert.False(result.Ok);
    }

    // ---------------------------------------------------------------- chat_ack

    private static string AckText(IEnumerable<long> acknowledged, IEnumerable<long> alreadySeen, IEnumerable<long> unknown)
    {
        static JsonArray Ids(IEnumerable<long> ids)
        {
            var arr = new JsonArray();
            foreach (var id in ids) arr.Add((JsonNode)JsonValue.Create(id));
            return arr;
        }
        return new JsonObject
        {
            ["actor"] = "coder-06-pi1",
            ["acknowledged"] = Ids(acknowledged),
            ["alreadySeen"] = Ids(alreadySeen),
            ["unknown"] = Ids(unknown),
        }.ToJsonString();
    }

    /// <summary>Answers each chat_ack like the server: ids in <paramref name="inbox"/> are acknowledged the first
    /// time and already seen afterwards, anything else is unknown.</summary>
    private static FakeHandler AckServer(IEnumerable<long> inbox)
    {
        var unhandled = new HashSet<long>(inbox);
        var handled = new HashSet<long>();
        return new FakeHandler(async request =>
        {
            var body = await request.Content!.ReadAsStringAsync();
            var ids = ((JsonArray)JsonNode.Parse(body)!["params"]!["arguments"]!["message_ids"]!).Select(n => n!.GetValue<long>()).ToList();
            var acknowledged = new List<long>();
            var alreadySeen = new List<long>();
            var unknown = new List<long>();
            lock (unhandled)
            {
                foreach (var id in ids)
                {
                    if (unhandled.Remove(id)) { handled.Add(id); acknowledged.Add(id); }
                    else if (handled.Contains(id)) alreadySeen.Add(id);
                    else unknown.Add(id);
                }
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(RpcOk(AckText(acknowledged, alreadySeen, unknown)), Encoding.UTF8, "application/json"),
            };
        });
    }

    [Fact]
    public async Task AckSendsChatAckWithMessageIdsAndParsesAllThreeLists()
    {
        using var handler = AckServer([2965, 2966]);
        using var client = Client(handler);

        var first = await client.AckAsync([2965, 123]);
        Assert.NotNull(first);
        Assert.Equal([2965L], first.Acknowledged);
        Assert.Empty(first.AlreadySeen);
        Assert.Equal([123L], first.Unknown);

        var second = await client.AckAsync([2965, 2966]);
        Assert.NotNull(second);
        Assert.Equal([2966L], second.Acknowledged);
        Assert.Equal([2965L], second.AlreadySeen);
        Assert.Empty(second.Unknown);

        var req = handler.Requests[0];
        Assert.Equal("POST", req.Method);
        Assert.Equal("http://chat.test/mcp", req.Uri.ToString());
        Assert.Equal("Bearer factory-token", req.Authorization);
        Assert.Equal("2025-06-18", req.ProtocolVersion);
        Assert.Equal("tools/call", req.Envelope["method"]!.GetValue<string>());
        Assert.Equal("chat_ack", req.Tool);
        Assert.Equal("message_ids", Assert.Single(req.Arguments).Key);
        Assert.Equal([2965L, 123L], req.Arguments["message_ids"]!.AsArray().Select(n => n!.GetValue<long>()));
    }

    [Fact]
    public async Task AckOfTheNestedIdNeverSendsTheInboxId()
    {
        // The item's inboxId (123) and nested message id (2965) live in different sequences.
        using var digestHandler = Json(HttpStatusCode.OK, RpcOk(DigestText(InboxItem(inboxId: 123, messageId: 2965))));
        using var digestClient = Client(digestHandler);
        var item = Assert.Single((await digestClient.ReadInboxAsync(0, markRead: true))!.Inbox);

        using var handler = AckServer([2965]);
        using var client = Client(handler);
        var receipt = await client.AckAsync([item.Message.Id]);

        Assert.Equal([2965L], receipt!.Acknowledged);
        Assert.Equal([2965L], Assert.Single(handler.Requests).Arguments["message_ids"]!.AsArray().Select(n => n!.GetValue<long>()));
    }

    [Fact]
    public async Task AckChunksMoreThanOneHundredIds()
    {
        var ids = Enumerable.Range(1, 250).Select(i => (long)i).ToList();
        using var handler = AckServer(ids);
        using var client = Client(handler);

        var receipt = await client.AckAsync(ids);

        Assert.NotNull(receipt);
        Assert.Equal(ids, receipt.Acknowledged.Order());
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal([100, 100, 50], handler.Requests.Select(r => r.Arguments["message_ids"]!.AsArray().Count));
        Assert.All(handler.Requests, r => Assert.Equal("chat_ack", r.Tool));
    }

    [Fact]
    public async Task AckOfNothingMakesNoCall()
    {
        using var handler = AckServer([]);
        using var client = Client(handler);

        var receipt = await client.AckAsync([]);

        Assert.NotNull(receipt);
        Assert.Empty(receipt.Acknowledged);
        Assert.Empty(receipt.AlreadySeen);
        Assert.Empty(receipt.Unknown);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task AckNeverSendsNonPositiveIdsAndDedupes()
    {
        using var handler = AckServer([7]);
        using var client = Client(handler);

        var receipt = await client.AckAsync([0, -3, 7, 7]);

        Assert.NotNull(receipt);
        Assert.Equal([7L], receipt.Acknowledged);
        Assert.Equal([0L, -3L], receipt.Unknown);
        Assert.Equal([7L], Assert.Single(handler.Requests).Arguments["message_ids"]!.AsArray().Select(n => n!.GetValue<long>()));

        using var none = AckServer([]);
        using var noneClient = Client(none);
        Assert.Equal([0L], (await noneClient.AckAsync([0]))!.Unknown);
        Assert.Empty(none.Requests);
    }

    [Fact]
    public async Task AckNullWhenToolReportsIsError()
    {
        using var handler = Json(HttpStatusCode.OK, RpcOk("Supply at most 100 positive message ids.", isError: true));
        using var client = Client(handler);
        Assert.Null(await client.AckAsync([1]));
    }

    [Fact]
    public async Task AckNullOnJsonRpcErrorFrame()
    {
        using var handler = Json(HttpStatusCode.OK, """{"jsonrpc":"2.0","id":1,"error":{"code":-32601,"message":"no such tool"}}""");
        using var client = Client(handler);
        Assert.Null(await client.AckAsync([1]));
    }

    [Fact]
    public async Task AckNullOnHttp500AndOnThrow()
    {
        using var failing = Json(HttpStatusCode.InternalServerError, "nope");
        using var failingClient = Client(failing);
        Assert.Null(await failingClient.AckAsync([1]));

        using var throwing = new FakeHandler(_ => throw new HttpRequestException("socket closed"));
        using var throwingClient = Client(throwing);
        Assert.Null(await throwingClient.AckAsync([1]));
    }

    [Fact]
    public async Task AckNullWhenAnyChunkFails()
    {
        var calls = 0;
        using var handler = new FakeHandler(_ => Task.FromResult(Interlocked.Increment(ref calls) == 1
            ? new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(RpcOk(AckText(Enumerable.Range(1, 100).Select(i => (long)i), [], [])), Encoding.UTF8, "application/json"),
            }
            : new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent("down") }));
        using var client = Client(handler);

        Assert.Null(await client.AckAsync(Enumerable.Range(1, 150).Select(i => (long)i).ToList()));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public void ParseAckEdgeCases()
    {
        Assert.Null(FactoryClient.ParseAck("nonsense"));
        Assert.Null(FactoryClient.ParseAck("[1,2]"));
        Assert.Null(FactoryClient.ParseAck("""{"actor":"coder-06-pi1"}"""));
        Assert.Null(FactoryClient.ParseAck("""{"acknowledged":[1],"alreadySeen":[]}"""));
        Assert.Null(FactoryClient.ParseAck("""{"acknowledged":1,"alreadySeen":[],"unknown":[]}"""));
        // A digest payload is not a receipt.
        Assert.Null(FactoryClient.ParseAck(DigestText(InboxItem())));

        var receipt = FactoryClient.ParseAck("""{"actor":"coder-06-pi1","acknowledged":[1,"2",null,{}],"alreadySeen":[3],"unknown":[]}""");
        Assert.NotNull(receipt);
        Assert.Equal([1L, 2L], receipt.Acknowledged);
        Assert.Equal([3L], receipt.AlreadySeen);
        Assert.Empty(receipt.Unknown);

        var empty = FactoryClient.ParseAck("""{"actor":"coder-06-pi1","acknowledged":[],"alreadySeen":[],"unknown":[]}""");
        Assert.NotNull(empty);
        Assert.Empty(empty.Acknowledged);
    }

    // ---------------------------------------------------------------- parsers

    [Fact]
    public void ParseIdentityRejectsBadInput()
    {
        Assert.Null(FactoryClient.ParseIdentity("["));
        Assert.Null(FactoryClient.ParseIdentity("[1,2]"));
        Assert.Null(FactoryClient.ParseIdentity("""{"name":"no slug"}"""));
        var identity = FactoryClient.ParseIdentity("""{"slug":"architect-1"}""");
        Assert.NotNull(identity);
        Assert.Equal("architect-1", identity.Name);
    }

    [Fact]
    public void ParseDigestRejectsBadInputAndSkipsBrokenItems()
    {
        Assert.Null(FactoryClient.ParseDigest("nonsense"));
        Assert.Null(FactoryClient.ParseDigest("[1,2]"));
        var digest = FactoryClient.ParseDigest("""{"inbox":[{"inboxId":1},{"inboxId":2,"messageId":7,"message":{"room":"ops"}}]}""");
        Assert.NotNull(digest);
        Assert.False(digest.TimedOut);
        var item = Assert.Single(digest.Inbox);
        Assert.Equal(7, item.Message.Id);
        Assert.Equal("note", item.Message.Kind);
        Assert.Empty(item.Message.Mentions);
    }

    [Fact]
    public void ParseDigestDropsItemWhoseMessageIdDisagreesWithNestedId()
    {
        var digest = FactoryClient.ParseDigest("""
            {"inbox":[
              {"inboxId":1,"messageId":7,"reason":"dm","message":{"id":9,"room":"ops","author":"a","body":"ambiguous"}},
              {"inboxId":2,"messageId":8,"reason":"dm","message":{"id":8,"room":"ops","author":"b","body":"fine"}}
            ]}
            """);
        Assert.NotNull(digest);
        var item = Assert.Single(digest.Inbox);
        Assert.Equal(2, item.InboxId);
        Assert.Equal(8, item.MessageId);
        Assert.Equal(8, item.Message.Id);
        Assert.Equal("fine", item.Message.Body);

        // The drop is stated, not silent: the ids it carried plus both numbers in the reason.
        var dropped = Assert.Single(digest.Dropped);
        Assert.Equal<long?>(1, dropped.InboxId);
        Assert.Equal<long?>(7, dropped.MessageId);
        Assert.Contains("7", dropped.Reason, StringComparison.Ordinal);
        Assert.Contains("9", dropped.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseDigestReadsAHeadsOnlyRow()
    {
        // The live chat server's digest (wi-52): every field inline, a one-line head, no nested message object.
        var digest = FactoryClient.ParseDigest("""
            {"inbox":[{"inboxId":4699,"messageId":19150,"reason":"mention","room":"pi-sharp","author":"agent-maker-2",
              "kind":"question","head":"**Defect: a lost provider response parks a seat forever.**","at":"2026-09-15T23:31:47.8Z",
              "replies":1,"compactionCarried":false}]}
            """);
        Assert.NotNull(digest);
        Assert.Empty(digest.Dropped);
        var item = Assert.Single(digest.Inbox);
        Assert.Equal(4699, item.InboxId);
        Assert.Equal(19150, item.MessageId);
        Assert.Equal("mention", item.Reason);
        Assert.Equal(19150, item.Message.Id);
        Assert.Equal("pi-sharp", item.Message.Room);
        Assert.Equal("agent-maker-2", item.Message.Author);
        Assert.Equal("question", item.Message.Kind);
        Assert.Equal("**Defect: a lost provider response parks a seat forever.**", item.Message.Body);
        // The body is the head, and the item says so: the agent must fetch the message before answering it.
        Assert.True(item.Message.HeadOnly);
        Assert.Null(item.Message.Thread);
    }

    [Fact]
    public void ParseDigestReadsTheThreadOfAHeadsOnlyRowAndThePointerNamesIt()
    {
        // A heads-only row for a reply: the server carries the thread root's id inline. The fetch pointer is
        // that thread; a seat that fetched by the message id would land in the root's own thread only by luck.
        var digest = FactoryClient.ParseDigest("""
            {"inbox":[{"inboxId":10085,"messageId":98907,"reason":"mention","room":"ops","author":"agent-maker-2",
              "kind":"question","thread":98887,"head":"post-deploy live check, which row woke you?","at":"2026-09-20T11:29:00Z"}]}
            """);
        var item = Assert.Single(digest!.Inbox);
        Assert.True(item.Message.HeadOnly);
        Assert.Equal(98887, item.Message.Thread);
        Assert.Equal("thread=98887", item.Message.ReadPointer);
    }

    [Fact]
    public void AHeadsOnlyRowWithoutAThreadIdPointsAtItsOwnMessageId()
    {
        var digest = FactoryClient.ParseDigest("""
            {"inbox":[{"inboxId":10086,"messageId":99028,"reason":"mention","room":"ops","author":"coder-06-pi5",
              "kind":"note","head":"pi5 reported the wrong pointer here","at":"2026-09-20T11:30:00Z"}]}
            """);
        var item = Assert.Single(digest!.Inbox);
        Assert.Null(item.Message.Thread);
        Assert.Equal("id=99028", item.Message.ReadPointer);
    }

    [Fact]
    public void ParseDigestDropsAHeadsOnlyRowWithNoHead()
    {
        var digest = FactoryClient.ParseDigest("""
            {"inbox":[{"inboxId":5,"messageId":11,"reason":"dm","room":"ops","author":"a"}]}
            """);
        Assert.NotNull(digest);
        Assert.Empty(digest.Inbox);
        var dropped = Assert.Single(digest.Dropped);
        Assert.Equal<long?>(5, dropped.InboxId);
        Assert.Equal<long?>(11, dropped.MessageId);
        Assert.Equal("no message object", dropped.Reason);
    }

    [Fact]
    public void ParseDigestRecordsAnItemWithNoIdAsDropped()
    {
        var digest = FactoryClient.ParseDigest("""
            {"inbox":[{"inboxId":4,"reason":"dm","message":{"room":"ops","author":"a","body":"who am I?"}}]}
            """);
        Assert.NotNull(digest);
        Assert.Empty(digest.Inbox);
        var dropped = Assert.Single(digest.Dropped);
        Assert.Equal<long?>(4, dropped.InboxId);
        Assert.Null(dropped.MessageId);
        Assert.Equal("no message id", dropped.Reason);
    }

    // ---------------------------------------------------------------- KB note-write failure detail

    [Fact]
    public async Task KbWriteNoteNon2xxFailureSurfacesHttpStatus()
    {
        using var handler = Json(HttpStatusCode.ServiceUnavailable, "");
        using var client = Client(handler);
        var result = await client.KbWriteNoteWithDetailAsync("wi-1", "Handover: rate limit", "detail", default);
        Assert.False(result.Ok);
        Assert.StartsWith("HTTP 503", result.Error!);
        var req = Assert.Single(handler.Requests);
        Assert.Equal("kb_write", req.Tool);
    }

    [Fact]
    public async Task KbWriteNoteTransportFailureSurfacesExceptionMessage()
    {
        using var handler = new FakeHandler(_ => Task.FromException<HttpResponseMessage>(new HttpRequestException("Connection refused (192.168.8.81:8443)")));
        using var client = Client(handler);
        var result = await client.KbWriteNoteWithDetailAsync("wi-1", "t", "b", default);
        Assert.False(result.Ok);
        Assert.Equal("Connection refused (192.168.8.81:8443)", result.Error);
    }

    [Fact]
    public async Task KbWriteNoteIsErrorFrameSurfacesToolText()
    {
        using var handler = Json(HttpStatusCode.OK, RpcOk("kb_write failed: disk full", isError: true));
        using var client = Client(handler);
        var result = await client.KbWriteNoteWithDetailAsync("wi-1", "t", "b", default);
        Assert.False(result.Ok);
        Assert.Contains("isError", result.Error!);
        Assert.Contains("disk full", result.Error);
    }

    [Fact]
    public async Task KbWriteNoteSuccessLeavesErrorDetailNull()
    {
        using var handler = Json(HttpStatusCode.OK, RpcOk("stored"));
        using var client = Client(handler);
        var result = await client.KbWriteNoteWithDetailAsync("wi-1", "t", "b", default);
        Assert.True(result.Ok);
        Assert.Null(result.Error);
    }

    // ---------------------------------------------------------------- note-write deadline

    [Fact]
    public async Task KbWriteNoteDelayedBeyondBaseDeadlineSucceedsWithinNoteDeadline()
    {
        // Base timeout 1s, note deadline 3s; responder sleeps 2s then succeeds.
        using var handler = new FakeHandler(async _ =>
        {
            await Task.Delay(2000);
            return new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(RpcOk("stored"), Encoding.UTF8, "application/json") };
        });
        using var client = Client(handler, timeout: TimeSpan.FromSeconds(1), noteDeadline: TimeSpan.FromSeconds(3));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await client.KbWriteNoteWithDetailAsync("wi-1", "t", "b", default);
        sw.Stop();
        Assert.True(result.Ok);
        Assert.Null(result.Error);
        // Should have waited ~2s, not been killed at 1s (base) or 3s (note deadline).
        Assert.InRange(sw.ElapsedMilliseconds, 1500, 4000);
    }

    [Fact]
    public async Task KbWriteNoteExceedingNoteDeadlineFailsWithDiagnostic()
    {
        // Base timeout 5s, note deadline 1s; responder sleeps 2s (beyond note deadline).
        using var handler = new FakeHandler(async _ =>
        {
            await Task.Delay(2000);
            return new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(RpcOk("stored"), Encoding.UTF8, "application/json") };
        });
        using var client = Client(handler, timeout: TimeSpan.FromSeconds(5), noteDeadline: TimeSpan.FromSeconds(1));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await client.KbWriteNoteWithDetailAsync("wi-1", "t", "b", default);
        sw.Stop();
        Assert.False(result.Ok);
        Assert.NotNull(result.Error);
        // Should have been cancelled at ~1s (note deadline), not 5s (base).
        Assert.InRange(sw.ElapsedMilliseconds, 500, 3000);
    }

    [Fact]
    public async Task SeatStandingTextComesFromTheKbCommonFirstThenEachRoleJobDescriptionAndStanding()
    {
        using var handler = new FakeHandler(async request =>
        {
            var args = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!["params"]!["arguments"]!;
            var slug = args["slug"]!.GetValue<string>();
            var key = args["doc"]?.GetValue<string>();
            string text = (slug, key) switch
            {
                ("coder-06-pi3", null) => """{"slug":"coder-06-pi3","links":[{"rel":"has_role","slug":"coder"},{"rel":"<-assigned_to","slug":"wi-1"}]}""",
                ("pi-sharp", "seat-standing") => """{"docs":[{"key":"seat-standing","body":"COMMON"}]}""",
                ("coder", "job-description") => """{"docs":[{"key":"job-description","body":"JD"}]}""",
                ("coder", "seat-standing") => """{"docs":[{"key":"seat-standing","body":"CODER"}]}""",
                _ => "{}",
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(RpcOk(text), Encoding.UTF8, "application/json") };
        });
        using var client = Client(handler);
        var text = await client.KbSeatStandingTextAsync("coder-06-pi3", default);
        Assert.Equal("COMMON\n\n# Your role in the KB: `coder` (you are actor coder-06-pi3)\n\nJD\n\nCODER", text);
    }

    [Theory]
    [InlineData(false, "COMMON")]
    [InlineData(true, null)]
    public async Task SeatStandingTextReadWholeIsNullWhenADocumentGotNoAnswerButNotWhenTheRoleHasNone(bool whole, string? expected)
    {
        // coder has no seat-standing here (the KB's refusal is an answer); the second read finds the KB gone for one document.
        var gone = false;
        using var handler = new FakeHandler(async request =>
        {
            var args = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!["params"]!["arguments"]!;
            var slug = args["slug"]!.GetValue<string>();
            var key = args["doc"]?.GetValue<string>();
            if (gone && (slug, key) is ("coder", "job-description")) throw new HttpRequestException("connection refused");
            var (text, isError) = (slug, key) switch
            {
                ("coder-06-pi3", null) => ("""{"slug":"coder-06-pi3","links":[{"rel":"has_role","slug":"coder"}]}""", false),
                ("pi-sharp", "seat-standing") => ("""{"docs":[{"key":"seat-standing","body":"COMMON"}]}""", false),
                ("coder", "job-description") => ("""{"docs":[{"key":"job-description","body":"JD"}]}""", false),
                _ => ("no document seat-standing on coder", true),
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(RpcOk(text, isError), Encoding.UTF8, "application/json") };
        });
        using var client = Client(handler);
        Assert.Equal("COMMON\n\n# Your role in the KB: `coder` (you are actor coder-06-pi3)\n\nJD", await client.KbSeatStandingTextAsync("coder-06-pi3", default, whole));
        gone = true;
        Assert.Equal(expected, await client.KbSeatStandingTextAsync("coder-06-pi3", default, whole));
    }

    [Fact]
    public async Task OtherKbCallsStillUseBaseDeadlineNotNoteDeadline()
    {
        // Base timeout 1s, note deadline 3s; kb_get responder sleeps 2s.
        // KbGet must fail at ~1s even though note deadline is 3s.
        using var handler = new FakeHandler(async _ =>
        {
            await Task.Delay(2000);
            return new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(RpcOk("{\"slug\":\"wi-1\",\"kind\":\"work_item\"}"), Encoding.UTF8, "application/json") };
        });
        using var client = Client(handler, timeout: TimeSpan.FromSeconds(1), noteDeadline: TimeSpan.FromSeconds(3));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var entity = await client.KbEntityAsync("wi-1", default);
        sw.Stop();
        Assert.Null(entity);
        // Should have been cancelled at ~1s (base), not 3s (note deadline).
        Assert.InRange(sw.ElapsedMilliseconds, 500, 3000);
    }

    [Fact]
    public async Task KbWriteNoteCallerCancellationEndsBeforeNoteDeadline()
    {
        // Note deadline 10s; caller cancels after 500ms.
        using var handler = new FakeHandler(async _ =>
        {
            await Task.Delay(10000);
            return new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(RpcOk("stored"), Encoding.UTF8, "application/json") };
        });
        using var client = Client(handler, timeout: TimeSpan.FromSeconds(5), noteDeadline: TimeSpan.FromSeconds(10));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await client.KbWriteNoteWithDetailAsync("wi-1", "t", "b", cts.Token);
        sw.Stop();
        Assert.False(result.Ok);
        // Should have been cancelled at ~500ms, not 10s.
        Assert.InRange(sw.ElapsedMilliseconds, 300, 2000);
    }
}
