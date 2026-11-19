// FactorySteering over a real FactoryClient and the in-memory FakeHandler from FactoryClientTests.
// Time is fake: FakeClock only moves when a test moves it, and its timers fire from that movement, so the
// Task.Delay(delay, clock, ct) calls inside the follow-up loop are honoured without any real sleeping.
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using PiSharp.Core.Agent;
using PiSharp.Core.Factory;
using PiSharp.Core.Messages;
using PiSharp.Core.Models;
using PiSharp.Core.Session;

namespace PiSharp.Tests.Factory;

/// <summary>A TimeProvider whose clock and timers are driven by the test. Callbacks fire outside the lock so a
/// continuation that immediately schedules the next timer cannot deadlock the advance.</summary>
internal sealed class FakeClock : TimeProvider
{
    private readonly object _gate = new();
    private readonly List<FakeTimer> _timers = [];
    private DateTimeOffset _now = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);
    private TimeSpan _elapsed;
    private TaskCompletionSource _scheduled = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override DateTimeOffset GetUtcNow() { lock (_gate) return _now; }

    /// <summary>Total virtual time this test has advanced.</summary>
    public TimeSpan Elapsed { get { lock (_gate) return _elapsed; } }

    public bool HasPendingTimer { get { lock (_gate) return _timers.Exists(t => t.DueAt is not null); } }

    /// <summary>Completes the next time any timer is scheduled or rescheduled.</summary>
    public Task Scheduled { get { lock (_gate) return _scheduled.Task; } }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(callback);
        var timer = new FakeTimer(this, callback, state);
        timer.Change(dueTime, period);
        return timer;
    }

    public void Advance(TimeSpan by)
    {
        List<FakeTimer> due;
        lock (_gate)
        {
            _now += by;
            _elapsed += by;
            due = _timers.FindAll(t => t.DueAt is DateTimeOffset d && d <= _now);
            foreach (var timer in due)
            {
                if (timer.Period <= TimeSpan.Zero)
                {
                    timer.DueAt = null;
                    _timers.Remove(timer);
                }
                else
                {
                    timer.DueAt = _now + timer.Period;
                }
            }
        }
        foreach (var timer in due) timer.Fire();
    }

    /// <summary>Jumps straight to the earliest pending timer and fires it. Returns the time skipped.</summary>
    public TimeSpan AdvanceToNextTimer()
    {
        TimeSpan step;
        lock (_gate)
        {
            DateTimeOffset? next = null;
            foreach (var timer in _timers)
                if (timer.DueAt is DateTimeOffset d && (next is null || d < next)) next = d;
            if (next is null) return TimeSpan.Zero;
            step = next.Value > _now ? next.Value - _now : TimeSpan.Zero;
        }
        Advance(step);
        return step;
    }

    internal void Schedule(FakeTimer timer, TimeSpan dueTime, TimeSpan period)
    {
        TaskCompletionSource signal;
        lock (_gate)
        {
            timer.Period = period;
            timer.DueAt = dueTime < TimeSpan.Zero ? null : _now + dueTime;
            if (!_timers.Contains(timer)) _timers.Add(timer);
            signal = _scheduled;
            _scheduled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        signal.TrySetResult();
    }

    internal void Remove(FakeTimer timer)
    {
        lock (_gate)
        {
            timer.DueAt = null;
            _timers.Remove(timer);
        }
    }

    internal sealed class FakeTimer(FakeClock clock, TimerCallback callback, object? state) : ITimer
    {
        internal DateTimeOffset? DueAt { get; set; }

        internal TimeSpan Period { get; set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            clock.Schedule(this, dueTime, period);
            return true;
        }

        public void Dispose() => clock.Remove(this);

        public ValueTask DisposeAsync()
        {
            clock.Remove(this);
            return ValueTask.CompletedTask;
        }

        internal void Fire() => callback(state);
    }
}

public partial class FactorySteeringTests
{
    [Fact]
    public async Task OnlyHumanResumeGrantsAnotherBudgetAndRepeatedDeliveryDoesNotResetIt()
    {
        using var disk = new PiSharp.Tests.Tools.TempWorkspace();
        using var memory = new PiSharp.Core.Notebook.NotebookStore(disk.Path, Self);
        var chat = new LatchedChat(); chat.Deliver(Item(40201, thread: 40200));
        using var handler = chat.Handler(); using var client = Client(handler);
        var steering = new FactorySteering(client) { Notebook = memory };
        await steering.GetSteeringMessagesAsync();
        memory.Put("budget-chat-40201", "attempt", "{\"turns\":100,\"outputTokens\":5,\"toolCalls\":5}", "chat-40201", "open", 0);
        chat.Deliver(Item(40202, thread: 40200, author: "peer", body: "/resume-budget"));
        await steering.GetSteeringMessagesAsync();
        Assert.Contains("100", memory.Get("budget-chat-40201")!["text"]!.GetValue<string>());
        chat.Deliver(Item(40203, thread: 40200, author: "digital-scribe", body: "/resume-budget"));
        await steering.GetSteeringMessagesAsync();
        var resumed = memory.Get("budget-chat-40201")!;
        Assert.Equal(0, JsonNode.Parse(resumed["text"]!.GetValue<string>())!["turns"]!.GetValue<int>());
        await steering.GetSteeringMessagesAsync();
        Assert.Equal(resumed["revision"]!.GetValue<long>(), memory.Get("budget-chat-40201")!["revision"]!.GetValue<long>());
    }

    [Theory]
    [InlineData("agent-maker-2", "/resume-budget", true)]
    [InlineData("claude-agent-maker-2", "/resume-budget\nstop re-reading the thread; post the fix and finish", true)]
    [InlineData("claude-agent-maker-1", "/resume-budget", false)] // retired 2026-10-14
    [InlineData("digital-scribe", "  /resume-budget  ", true)]
    [InlineData("coder-06-pi5", "/resume-budget", false)]
    [InlineData("agent-maker-2", "please /resume-budget", false)]
    public void AgentMakerMayResumeABudgetWithANudge(string author, string body, bool expected) =>
        Assert.Equal(expected, FactorySteering.IsBudgetResume(author, body));

    [Fact]
    public async Task NotebookResendsAFailedReplyAcrossRestartButNotAnAmbiguousOne()
    {
        using var disk = new PiSharp.Tests.Tools.TempWorkspace();
        var clock = new FakeClock();
        var chat = new LatchedChat { FailPosts = true };
        chat.Deliver(Item(40101, thread: 40100, body: new string('x', 350) + " Preserve the final instruction"));
        using var handler = chat.Handler(); using var client = Client(handler);
        using (var memory = new PiSharp.Core.Notebook.NotebookStore(disk.Path, Self))
        {
            var steering = new FactorySteering(client, clock) { Notebook = memory };
            Assert.Single(await steering.GetSteeringMessagesAsync());
            Assert.Contains("Preserve the final instruction", memory.Get("chat-source-40101")!["text"]!.GetValue<string>());
            await steering.ShouldStopAfterTurnAsync(Turn(Context([]), Assistant("Done")));
            Assert.Equal("blocked", memory.Get("delivery-chat-40101")!["status"]!.GetValue<string>());
            Assert.Single(Posts(handler));
            // A post still pending when the process died may have reached chat: that one is never resent.
            var row = memory.Get("delivery-chat-40101")!;
            memory.Put("delivery-chat-40101", "attempt", row["text"]!.GetValue<string>(), "chat-40101", "open", row["revision"]!.GetValue<long>());
        }
        chat.FailPosts = false;
        using (var ambiguous = new PiSharp.Core.Notebook.NotebookStore(disk.Path, Self))
        {
            Assert.Empty(await new FactorySteering(client, clock) { Notebook = ambiguous }.GetSteeringMessagesAsync());
            Assert.Single(Posts(handler));
            Assert.NotEmpty(chat.Unhandled);
            // A post that came back failed is resent from its record, once.
            var row = ambiguous.Get("delivery-chat-40101")!;
            ambiguous.Put("delivery-chat-40101", "attempt", row["text"]!.GetValue<string>(), "chat-40101", "blocked", row["revision"]!.GetValue<long>());
        }
        using var resumed = new PiSharp.Core.Notebook.NotebookStore(disk.Path, Self);
        var restarted = new FactorySteering(client, clock) { Notebook = resumed };
        Assert.Empty(await restarted.GetSteeringMessagesAsync());
        Assert.Single(Posts(handler)); // persisted transient is queued with a backoff after restart
        clock.Advance(FactorySteering.InitialBackoff);
        Assert.Empty(await restarted.GetSteeringMessagesAsync());
        Assert.Equal(2, Posts(handler).Count);
        Assert.Equal("completed", resumed.Get("delivery-chat-40101")!["status"]!.GetValue<string>());
        await restarted.GetSteeringMessagesAsync();
        Assert.Equal(2, Posts(handler).Count);
    }

    [Fact]
    public async Task OverlongFinalTextGoesWholeInPartsChatAccepts()
    {
        // pi4, 10-05: 23,332 characters could never be delivered; 0.5.47 clipped it and lost the rest; 0.5.146 posts parts.
        var chat = new LatchedChat(); chat.Deliver(Item(40111, thread: 40110));
        using var handler = chat.Handler(); using var client = Client(handler);
        var steering = new FactorySteering(client);
        Assert.Single(await steering.GetSteeringMessagesAsync());
        var reply = new string('y', 23_332);
        await steering.ShouldStopAfterTurnAsync(Turn(Context([]), Assistant(reply)));
        var bodies = Posts(handler).Select(p => p.Arguments["body"]!.GetValue<string>()).ToList();
        Assert.Equal(2, bodies.Count);
        Assert.All(bodies, b => Assert.True(b.Length <= 20_000));
        Assert.Equal(reply, string.Concat(bodies.Select((b, i) => b[$"(part {i + 1} of 2)\n".Length..])));
    }

    [Fact]
    public async Task AnsweredMessageCompletesItsNotebookSourcesAndNoOthers()
    {
        using var disk = new PiSharp.Tests.Tools.TempWorkspace();
        using var memory = new PiSharp.Core.Notebook.NotebookStore(disk.Path, Self);
        memory.Put("chat-source-403011", "source", "[inbox from @peer in general, message 403011, mention] other", "chat-403011", "open", 0);
        var chat = new LatchedChat(); chat.Deliver(Item(40301, thread: 40300));
        using var handler = chat.Handler(); using var client = Client(handler);
        var steering = new FactorySteering(client) { Notebook = memory };
        var messages = await steering.GetSteeringMessagesAsync();
        PiSharp.Core.Notebook.NotebookContext.Capture(memory, messages);
        Assert.Equal(3, memory.List(kind: "source", status: "open").Count);
        await steering.ShouldStopAfterTurnAsync(Turn(Context([]), Assistant("the schema change is in 4bd96e5")));
        Assert.Single(Posts(handler));
        Assert.Equal(["chat-source-403011"], memory.List(kind: "source", status: "open").Select(r => r["id"]!.GetValue<string>()));
        Assert.Equal("completed", memory.Get("chat-source-40301")!["status"]!.GetValue<string>());
    }

    private const string KbUrl = "http://kb.test";
    private const string ChatUrl = "http://chat.test";
    private const string Token = "factory-token";

    /// <summary>The actor the digest items mention and the steering run as.</summary>
    private const string Self = "coder-06-pi1";

    private static readonly TimeSpan RealBudget = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task RetryCapRevalidatesBindingThenBlocksAndEscalatesOnBoard()
    {
        var wake = Item(5001, thread: 4999);
        wake["message"]!["about"] = new JsonArray("wi-vl3");
        Assert.Equal("wi-vl3", FactoryClient.ParseDigest(DigestText([wake]))!.Inbox.Single().Message.AboutSlugs.Single());
        var requests = new List<FakeRequest>();
        var allRequests = new List<string>();
        var whoCalls = 0;
        var kbReads = 0;
        using var handler = new FakeHandler(async request =>
        {
            allRequests.Add($"{request.Method} {request.RequestUri}");
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync();
            if (request.RequestUri!.AbsolutePath == "/whoami")
            {
                whoCalls++;
                return new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent("""{"slug":"coder-06-pi1","name":"Pi1"}""", Encoding.UTF8, "application/json") };
            }
            var call = JsonNode.Parse(body)!["params"]!;
            var tool = call["name"]!.GetValue<string>();
            string text = tool switch
            {
                "chat_read" => DigestText([wake]),
                "kb_get" => new JsonObject
                {
                    ["kind"] = "work_item", ["status"] = ++kbReads == 1 ? "in_progress" : "in_progress",
                    ["links"] = new JsonArray(new JsonObject { ["rel"] = "assigned_to", ["slug"] = Self }),
                }.ToJsonString(),
                "kb_transition" => "{\"status\":\"blocked\"}",
                "chat_post" => "{\"id\":6001}",
                _ => "{}",
            };
            lock (requests) requests.Add(new FakeRequest("POST", request.RequestUri, request.Headers.Authorization?.ToString(),
                request.Headers.Accept.ToString(), null, body));
            return new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(Rpc(text), Encoding.UTF8, "application/json") };
        });
        using var client = Client(handler);
        var steering = new FactorySteering(client);
        Assert.Single(await steering.GetSteeringMessagesAsync());
        Assert.Equal("chat-5001", steering.CurrentTaskKey);
        Assert.Equal("wi-vl3", steering.Current!.Message.AboutSlugs.Single());

        await steering.HandleVerificationCapAsync("chat-5001", "last failure", 3, 3);

        Assert.True(whoCalls == 2, string.Join(" | ", allRequests));
        Assert.Equal(2, kbReads);
        var calls = requests.Select(r => r.Envelope["params"]!).ToList();
        var transition = Assert.Single(calls, c => c["name"]!.GetValue<string>() == "kb_transition");
        Assert.Equal("wi-vl3", transition["arguments"]!["work_item"]!.GetValue<string>());
        Assert.Equal("blocked", transition["arguments"]!["to_status"]!.GetValue<string>());
        Assert.Contains("not atomic", transition["arguments"]!["note"]!.GetValue<string>());
        Assert.Contains("no conditional expected-status/assignee precondition", transition["arguments"]!["note"]!.GetValue<string>());
        var post = Assert.Single(calls, c => c["name"]!.GetValue<string>() == "chat_post");
        Assert.Equal("board", post["arguments"]!["room"]!.GetValue<string>());
        Assert.Equal("wi-vl3", post["arguments"]!["about"]![0]!.GetValue<string>());
    }

    [Theory]
    [InlineData("transition")]
    [InlineData("board")]
    public async Task RetryCapWriteFailuresAreReportedAccurately(string failureAt)
    {
        var wake = Item(5005, thread: 4995);
        wake["message"]!["about"] = new JsonArray("wi-write-failure");
        var calls = new List<JsonObject>();
        using var handler = new FakeHandler(async request =>
        {
            if (request.RequestUri!.AbsolutePath == "/whoami")
                return new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent("""{"slug":"coder-06-pi1","name":"Pi1"}""", Encoding.UTF8, "application/json") };
            var body = await request.Content!.ReadAsStringAsync();
            var call = (JsonObject)JsonNode.Parse(body)!["params"]!.DeepClone();
            calls.Add(call);
            var tool = call["name"]!.GetValue<string>();
            var args = call["arguments"] as JsonObject;
            var isError = tool == "kb_transition" && failureAt == "transition"
                || tool == "chat_post" && args?.ContainsKey("room") == true && failureAt == "board";
            var text = tool switch
            {
                "chat_read" => DigestText([wake]),
                "kb_get" => new JsonObject
                {
                    ["kind"] = "work_item", ["status"] = "in_progress",
                    ["links"] = new JsonArray(new JsonObject { ["rel"] = "assigned_to", ["slug"] = Self }),
                }.ToJsonString(),
                "kb_transition" => "{\"status\":\"blocked\"}",
                "chat_post" => "{\"id\":6005}",
                _ => "{}",
            };
            return new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(Rpc(text, isError), Encoding.UTF8, "application/json") };
        });
        using var client = Client(handler);
        var steering = new FactorySteering(client);
        Assert.Single(await steering.GetSteeringMessagesAsync());

        await steering.HandleVerificationCapAsync("chat-5005", "last failure", 3, 3);

        var fallback = Assert.Single(calls, c => c["name"]!.GetValue<string>() == "chat_post"
            && (c["arguments"] as JsonObject)?.ContainsKey("thread") == true);
        var bodyText = fallback["arguments"]!["body"]!.GetValue<string>();
        Assert.Contains(failureAt == "transition" ? "kb_transition failed" : "board escalation post failed", bodyText);
        Assert.DoesNotContain("no unique authenticated work-item binding", bodyText);
        Assert.DoesNotContain("no confirmed work-item binding", bodyText);
        if (failureAt == "board")
            Assert.Contains(calls, c => c["name"]!.GetValue<string>() == "chat_post"
                && (c["arguments"] as JsonObject)?.ContainsKey("room") == true);
    }

    [Fact]
    public async Task RetryCapAbortsWhenReReadShowsStaleOwnerAndRepliesOnlyToWakeThread()
    {
        var wake = Item(5002, thread: 4998);
        wake["message"]!["about"] = new JsonArray("wi-stale");
        var kbReads = 0;
        var requests = new List<FakeRequest>();
        using var handler = new FakeHandler(async request =>
        {
            if (request.RequestUri!.AbsolutePath == "/whoami")
                return new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent("""{"slug":"coder-06-pi1","name":"Pi1"}""", Encoding.UTF8, "application/json") };
            var body = await request.Content!.ReadAsStringAsync();
            var call = JsonNode.Parse(body)!["params"]!;
            var tool = call["name"]!.GetValue<string>();
            string text = tool switch
            {
                "chat_read" => DigestText([wake]),
                "kb_get" => new JsonObject
                {
                    ["kind"] = "work_item", ["status"] = ++kbReads == 1 ? "in_progress" : "review",
                    ["links"] = new JsonArray(new JsonObject { ["rel"] = "assigned_to", ["slug"] = Self }),
                }.ToJsonString(),
                "chat_post" => "{\"id\":6002}",
                _ => "{}",
            };
            lock (requests) requests.Add(new FakeRequest("POST", request.RequestUri, request.Headers.Authorization?.ToString(),
                request.Headers.Accept.ToString(), null, body));
            return new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(Rpc(text), Encoding.UTF8, "application/json") };
        });
        using var client = Client(handler);
        var steering = new FactorySteering(client);
        Assert.Single(await steering.GetSteeringMessagesAsync());

        await steering.HandleVerificationCapAsync("chat-5002", "last failure", 3, 3);

        var calls = requests.Select(r => r.Envelope["params"]!).ToList();
        Assert.DoesNotContain(calls, c => c["name"]!.GetValue<string>() == "kb_transition");
        var post = Assert.Single(calls, c => c["name"]!.GetValue<string>() == "chat_post");
        Assert.Equal(4998, post["arguments"]!["thread"]!.GetValue<long>());
        Assert.False(post["arguments"]!.AsObject().ContainsKey("room"));
    }

    [Fact]
    public async Task RetryCapWithTwoAssignedAboutSlugsDoesNotChooseEither()
    {
        var wake = Item(5004, thread: 4996);
        wake["message"]!["about"] = new JsonArray("wi-one", "wi-two");
        var requests = new List<JsonObject>();
        using var handler = new FakeHandler(async request =>
        {
            if (request.RequestUri!.AbsolutePath == "/whoami")
                return new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent("""{"slug":"coder-06-pi1","name":"Pi1"}""", Encoding.UTF8, "application/json") };
            var body = await request.Content!.ReadAsStringAsync();
            var call = JsonNode.Parse(body)!["params"]!;
            requests.Add((JsonObject)call.DeepClone());
            var tool = call["name"]!.GetValue<string>();
            var text = tool switch
            {
                "chat_read" => DigestText([wake]),
                "kb_get" => new JsonObject
                {
                    ["kind"] = "work_item", ["status"] = "in_progress",
                    ["links"] = new JsonArray(new JsonObject { ["rel"] = "assigned_to", ["slug"] = Self }),
                }.ToJsonString(),
                "chat_post" => "{\"id\":6004}",
                _ => "{}",
            };
            return new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(Rpc(text), Encoding.UTF8, "application/json") };
        });
        using var client = Client(handler);
        var steering = new FactorySteering(client);
        Assert.Single(await steering.GetSteeringMessagesAsync());

        await steering.HandleVerificationCapAsync("chat-5004", "last failure", 3, 3);

        Assert.Equal(2, requests.Count(c => c["name"]!.GetValue<string>() == "kb_get"));
        Assert.DoesNotContain(requests, c => c["name"]!.GetValue<string>() == "kb_transition");
        var post = Assert.Single(requests, c => c["name"]!.GetValue<string>() == "chat_post");
        Assert.Equal(4996, post["arguments"]!["thread"]!.GetValue<long>());
    }

    [Fact]
    public async Task RetryCapWithNoAboutSlugPostsOnlyToTrustedWakeThread()
    {
        var wake = Item(5003, thread: 4997);
        var requests = new List<JsonObject>();
        using var handler = new FakeHandler(async request =>
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync();
            if (request.RequestUri!.AbsolutePath == "/whoami")
                return new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent("""{"slug":"coder-06-pi1","name":"Pi1"}""", Encoding.UTF8, "application/json") };
            var call = JsonNode.Parse(body)!["params"]!;
            requests.Add((JsonObject)call.DeepClone());
            var tool = call["name"]!.GetValue<string>();
            var text = tool switch
            {
                "chat_read" => DigestText([wake]),
                "chat_post" => "{\"id\":6003}",
                _ => "{}",
            };
            return new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(Rpc(text), Encoding.UTF8, "application/json") };
        });
        using var client = Client(handler);
        var steering = new FactorySteering(client);
        Assert.Single(await steering.GetSteeringMessagesAsync());

        await steering.HandleVerificationCapAsync("chat-5003", "last failure", 3, 3);

        Assert.DoesNotContain(requests, c => c["name"]!.GetValue<string>() is "kb_get" or "kb_transition");
        var post = Assert.Single(requests, c => c["name"]!.GetValue<string>() == "chat_post");
        Assert.Equal(4997, post["arguments"]!["thread"]!.GetValue<long>());
        Assert.False(post["arguments"]!.AsObject().ContainsKey("room"));
    }

    [Fact]
    public async Task Quota429ImmediatelyHandsBackHeldItemsAndThreeFailuresSetUnavailableUntilSuccess()
    {
        var calls = new List<JsonObject>();
        using var handler = new FakeHandler(async request =>
        {
            var envelope = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!;
            var call = (JsonObject)envelope["params"]!.DeepClone();
            calls.Add(call);
            var tool = call["name"]!.GetValue<string>();
            var text = tool switch
            {
                "kb_list" => "{\"items\":[{\"slug\":\"wi-held\",\"status\":\"in_progress\"}]}",
                "kb_get" => "{\"kind\":\"work_item\",\"status\":\"in_progress\",\"links\":[{\"rel\":\"assigned_to\",\"slug\":\"coder-06-pi6\"},{\"rel\":\"<-has_role\",\"slug\":\"team-lead-2\",\"kind\":\"actor\"}]}",
                _ => "{\"ok\":true}",
            };
            return new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(Rpc(text), Encoding.UTF8, "application/json") };
        });
        using var client = Client(handler);
        var clock = new FakeClock();
        var steering = new FactorySteering(client, clock) { Actor = "coder-06-pi6" };
        var quotaFailure = new AssistantMessage
        {
            Model = "grok", Provider = "xai", Content = [], StopReason = StopReason.Error,
            ProviderRequestFailed = true, ProviderHttpStatus = 429, RateLimitReason = "quota",
        };

        await steering.ObserveProviderResponseAsync(quotaFailure);
        Assert.Contains(calls, c => c["name"]!.GetValue<string>() == "kb_write");
        var transition = Assert.Single(calls, c => c["name"]!.GetValue<string>() == "kb_transition");
        Assert.Equal("ready", transition["arguments"]!["to_status"]!.GetValue<string>());
        Assert.Equal("team-lead-2", transition["arguments"]!["assign_to"]!.GetValue<string>());
        var handover = Assert.Single(calls, c => c["name"]!.GetValue<string>() == "chat_post");
        Assert.Contains("HANDOVER wi-held", handover["arguments"]!["body"]!.GetValue<string>());
        Assert.Equal("wi-held", handover["arguments"]!["about"]![0]!.GetValue<string>());
        Assert.DoesNotContain(calls, c => c["name"]!.GetValue<string>() == "chat_manage");
        await steering.ObserveProviderResponseAsync(new AssistantMessage
        { Model = "grok", Provider = "xai", Content = [], StopReason = StopReason.Error });
        await steering.ObserveProviderResponseAsync(new AssistantMessage
        { Model = "grok", Provider = "xai", Content = [], StopReason = StopReason.Aborted, ProviderRequestFailed = true });
        Assert.DoesNotContain(calls, c => c["name"]!.GetValue<string>() == "chat_manage");

        await steering.ObserveProviderResponseAsync(quotaFailure);
        clock.Advance(TimeSpan.FromSeconds(7));
        await steering.ObserveProviderResponseAsync(quotaFailure);
        var unavailable = Assert.Single(calls, c => c["name"]!.GetValue<string>() == "chat_manage");
        Assert.Equal("model unavailable: xai HTTP 429 since 2026-09-14T12:00:00Z",
            unavailable["arguments"]!["status"]!.GetValue<string>());

        await steering.ObserveProviderResponseAsync(new AssistantMessage
        { Model = "grok", Provider = "xai", Content = [], StopReason = StopReason.Stop });
        Assert.Equal(string.Empty, calls.Last(c => c["name"]!.GetValue<string>() == "chat_manage")["arguments"]!["status"]!.GetValue<string>());
        Assert.Single(calls, c => c["name"]!.GetValue<string>() == "kb_transition");
        Assert.Single(calls, c => c["name"]!.GetValue<string>() == "chat_post");
    }

    // pi12, 2026-10-28: hybrid, Grok paused until 11-01, and one xai 429 handed its epic back to the team lead. A hybrid seat
    // runs such a turn on its own model and keeps its work (rule rate-limit-handover v10).
    [Fact]
    public async Task AHybridSeatKeepsItsWorkOnASpentAllowance_AndIsNotMarkedUnavailable()
    {
        var calls = new List<JsonObject>();
        using var handler = new FakeHandler(async request =>
        {
            var call = (JsonObject)JsonNode.Parse(await request.Content!.ReadAsStringAsync())!["params"]!.DeepClone();
            calls.Add(call);
            var text = call["name"]!.GetValue<string>() == "kb_list" ? "{\"items\":[{\"slug\":\"wi-held\",\"status\":\"in_progress\"}]}" : "{\"ok\":true}";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Rpc(text), Encoding.UTF8, "application/json") };
        });
        using var client = Client(handler);
        var lines = new List<string>();
        var steering = new FactorySteering(client, new FakeClock()) { Actor = "coder-06-pi12", Diagnostics = lines.Add, RunsElsewhereOnQuota = () => true };
        var quota = new AssistantMessage
        {
            Model = "grok", Provider = "xai", Content = [], StopReason = StopReason.Error,
            ProviderRequestFailed = true, ProviderHttpStatus = 429, RateLimitReason = "quota", ErrorCode = "subscription_limit_reached",
        };
        for (var i = 0; i < 4; i++) await steering.ObserveProviderResponseAsync(quota);
        Assert.DoesNotContain(calls, c => c["name"]!.GetValue<string>() is "kb_transition" or "chat_post" or "chat_manage");
        Assert.Contains(lines, l => l.Contains("keeps its work and runs on its own model"));
    }

    // digital-scribe, 2026-10-28: "anything from our KB or chat that truncates content? if so we need to fix that ASAP". A reply
    // over 19,000 characters was clipped and the rest lost; it now goes as parts that put it back together.
    [Fact]
    public void ALongReplyGoesAsPartsThatPutItBackTogether_AShortOneAsItIs()
    {
        Assert.Equal(["short"], FactorySteering.ReplyParts("short"));
        var paragraphs = string.Join("\n\n", Enumerable.Range(0, 900).Select(i => $"paragraph {i}: " + new string('x', 60)));
        var parts = FactorySteering.ReplyParts(paragraphs);
        Assert.True(parts.Count > 1);
        Assert.All(parts, p => Assert.True(p.Length <= FactorySteering.MaxReplyChars + 40));
        var bodies = parts.Select((p, i) =>
        {
            var label = $"(part {i + 1} of {parts.Count})\n";
            Assert.StartsWith(label, p);
            return p[label.Length..];
        }).ToList();
        Assert.Equal(paragraphs, string.Concat(bodies));
        Assert.All(bodies.SkipLast(1), b => Assert.EndsWith("\n", b));
    }

    [Fact]
    public async Task DegenerateModelSetsUnavailableUntilHealthyAgain()
    {
        var calls = new List<JsonObject>();
        using var handler = new FakeHandler(async request =>
        {
            var envelope = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!;
            calls.Add((JsonObject)envelope["params"]!.DeepClone());
            return new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(Rpc("{\"ok\":true}"), Encoding.UTF8, "application/json") };
        });
        using var client = Client(handler);
        var steering = new FactorySteering(client, new FakeClock()) { Actor = "coder-06-pi6" };
        string Status() => calls.Last(c => c["name"]!.GetValue<string>() == "chat_manage")["arguments"]!["status"]!.GetValue<string>();

        await steering.NoteModelHealthAsync(true, "openai-completions", DateTimeOffset.UnixEpoch, "fine");
        Assert.DoesNotContain(calls, c => c["name"]!.GetValue<string>() == "chat_manage");

        var since = new DateTimeOffset(2026, 10, 14, 9, 30, 0, TimeSpan.Zero);
        await steering.NoteModelHealthAsync(false, "openai-completions", since, "degenerate output (!!!) from fake");
        Assert.Equal("model unavailable: openai-completions degenerate output (!!!) from fake since 2026-10-14T09:30:00Z", Status());

        await steering.NoteModelHealthAsync(true, "openai-completions", since, "sane again");
        Assert.Equal(string.Empty, Status());
    }

    /// <summary>2026-10-29: team-lead-2 waited 14:03-14:41 on one routine turn while its line said it worked on a message
    /// (digital-scribe: "this is what I expected you to catch").</summary>
    [Fact]
    public async Task AModelWaitIsOnTheStatusLineUntilItEnds_ThenTheLineBeforeItComesBack()
    {
        var calls = new List<JsonObject>();
        using var handler = new FakeHandler(async request =>
        {
            var envelope = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!;
            calls.Add((JsonObject)envelope["params"]!.DeepClone());
            return new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(Rpc("{\"ok\":true}"), Encoding.UTF8, "application/json") };
        });
        using var client = Client(handler);
        var steering = new FactorySteering(client, new FakeClock()) { Actor = "team-lead-2" };
        string Status() => calls.Last(c => c["name"]!.GetValue<string>() == "chat_manage")["arguments"]!["status"]!.GetValue<string>();

        await steering.NoteModelWaitAsync(null);
        Assert.DoesNotContain(calls, c => c["name"]!.GetValue<string>() == "chat_manage"); // nothing was waited on

        await steering.NoteModelWaitAsync("waiting on qwen3-8-flash since 14:03Z (queued #3)");
        Assert.Equal("working — waiting on qwen3-8-flash since 14:03Z (queued #3)", Status());
        await steering.NoteModelWaitAsync("waiting on qwen3-8-flash since 14:03Z (the model has it)");
        Assert.Equal("working — waiting on qwen3-8-flash since 14:03Z (the model has it)", Status());

        await steering.NoteModelWaitAsync(null);
        Assert.Equal(string.Empty, Status());

        // A model that is unavailable says so; a wait does not cover it.
        await steering.NoteModelHealthAsync(false, "openai-completions", new DateTimeOffset(2026, 10, 29, 14, 0, 0, TimeSpan.Zero), "degenerate output");
        await steering.NoteModelWaitAsync("waiting on qwen3-8-flash since 14:03Z");
        Assert.StartsWith("model unavailable", Status());
    }

    [Fact]
    public async Task OverlappingQuotaFailuresLatchBeforeSideEffectsAndSuppressDuplicateHandover()
    {
        var scanEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseScan = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = new List<JsonObject>();
        using var handler = new FakeHandler(async request =>
        {
            var envelope = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!;
            var call = (JsonObject)envelope["params"]!.DeepClone();
            calls.Add(call);
            var tool = call["name"]!.GetValue<string>();
            if (tool == "kb_list")
            {
                scanEntered.TrySetResult();
                await releaseScan.Task;
            }
            var text = tool switch
            {
                "kb_list" => "{\"items\":[{\"slug\":\"wi-overlap\",\"status\":\"in_progress\"}]}",
                "kb_get" => "{\"kind\":\"work_item\",\"status\":\"in_progress\",\"links\":[{\"rel\":\"assigned_to\",\"slug\":\"coder-06-pi6\"},{\"rel\":\"<-has_role\",\"slug\":\"team-lead-2\",\"kind\":\"actor\"}]}",
                _ => "{\"ok\":true}",
            };
            return new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(Rpc(text), Encoding.UTF8, "application/json") };
        });
        using var client = Client(handler);
        var steering = new FactorySteering(client) { Actor = "coder-06-pi6" };
        var quotaFailure = new AssistantMessage
        {
            Model = "grok", Provider = "xai", Content = [], StopReason = StopReason.Error,
            ProviderRequestFailed = true, ProviderHttpStatus = 429, RateLimitReason = "quota",
        };

        var first = steering.ObserveProviderResponseAsync(quotaFailure);
        await scanEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var overlapping = steering.ObserveProviderResponseAsync(quotaFailure);
        releaseScan.TrySetResult();
        await Task.WhenAll(first, overlapping);

        Assert.Single(calls, c => c["name"]!.GetValue<string>() == "kb_list");
        Assert.Single(calls, c => c["name"]!.GetValue<string>() == "kb_transition");
        Assert.Single(calls, c => c["name"]!.GetValue<string>() == "chat_post");
    }

    [Fact]
    public async Task EmptyQuotaScanDoesNotLatchOutWorkHeldLaterOnTheSameFailingProcess()
    {
        var held = false;
        var calls = new List<JsonObject>();
        using var handler = new FakeHandler(async request =>
        {
            var envelope = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!;
            var call = (JsonObject)envelope["params"]!.DeepClone();
            calls.Add(call);
            var text = call["name"]!.GetValue<string>() switch
            {
                "kb_list" => held
                    ? "{\"items\":[{\"slug\":\"wi-later-held\",\"status\":\"in_progress\"}]}"
                    : "{\"items\":[]}",
                "kb_get" => "{\"kind\":\"work_item\",\"status\":\"in_progress\",\"links\":[{\"rel\":\"assigned_to\",\"slug\":\"coder-06-pi6\"},{\"rel\":\"<-has_role\",\"slug\":\"team-lead-2\",\"kind\":\"actor\"}]}",
                _ => "{\"ok\":true}",
            };
            return new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(Rpc(text), Encoding.UTF8, "application/json") };
        });
        using var client = Client(handler);
        var steering = new FactorySteering(client) { Actor = "coder-06-pi6" };
        var quotaFailure = new AssistantMessage
        {
            Model = "grok", Provider = "xai", Content = [], StopReason = StopReason.Error,
            ProviderRequestFailed = true, ProviderHttpStatus = 429, RateLimitReason = "quota",
        };

        await steering.ObserveProviderResponseAsync(quotaFailure);
        Assert.DoesNotContain(calls, c => c["name"]!.GetValue<string>() == "kb_transition");

        held = true;
        await steering.ObserveProviderResponseAsync(quotaFailure);

        var transition = Assert.Single(calls, c => c["name"]!.GetValue<string>() == "kb_transition");
        Assert.Equal("wi-later-held", transition["arguments"]!["work_item"]!.GetValue<string>());
        Assert.Equal("ready", transition["arguments"]!["to_status"]!.GetValue<string>());
        Assert.Equal("team-lead-2", transition["arguments"]!["assign_to"]!.GetValue<string>());
        Assert.Single(calls, c => c["name"]!.GetValue<string>() == "chat_post");
    }

    // ---------------------------------------------------------------- paused rate-band handover (rate-bands-d)

    [Fact]
    public async Task PausedBandCommitsAndPushesWipThenHandsHeldItemBackToTeamLead()
    {
        var sim = new KbSim { BandJson = PausedBandJson("2026-09-14T11:58:00Z") };
        sim.Held.Add("wi-paused");
        using var wa = new WorkArea();
        var (dir, origin) = wa.Repo("wi-paused", branch: "coder-06-pi6/wi-paused", dirty: true);
        using var client = Client(KbHandler(sim));
        var steering = new FactorySteering(client, new FakeClock()) { Actor = "coder-06-pi6", WorkArea = wa.Root };

        await steering.CheckRateBandAsync();

        Assert.Equal("WIP handover wi-paused: rate band paused", Git(dir, "log", "-1", "--format=%s"));
        Assert.Equal(Git(dir, "rev-parse", "HEAD"), Git(dir, "--git-dir", origin, "rev-parse", "refs/heads/coder-06-pi6/wi-paused"));
        var note = Assert.Single(sim.Calls, c => c["name"]!.GetValue<string>() == "kb_write");
        Assert.Equal("Handover: rate band paused", note["arguments"]!["doc"]!["title"]!.GetValue<string>());
        var body = note["arguments"]!["doc"]!["body"]!.GetValue<string>();
        Assert.Contains("pushed branch coder-06-pi6/wi-paused to origin", body);
        Assert.Contains("the window resets 2026-09-14T13:00:00Z", body);
        var transition = Assert.Single(sim.Calls, c => c["name"]!.GetValue<string>() == "kb_transition");
        Assert.Equal("wi-paused", transition["arguments"]!["work_item"]!.GetValue<string>());
        Assert.Equal("ready", transition["arguments"]!["to_status"]!.GetValue<string>());
        Assert.Equal("team-lead-2", transition["arguments"]!["assign_to"]!.GetValue<string>());
        var handover = Assert.Single(sim.Calls, c => c["name"]!.GetValue<string>() == "chat_post");
        Assert.Equal("board", handover["arguments"]!["room"]!.GetValue<string>());
        Assert.Contains("HANDOVER wi-paused: rate band paused", handover["arguments"]!["body"]!.GetValue<string>());
        Assert.Equal("wi-paused", handover["arguments"]!["about"]![0]!.GetValue<string>());
    }

    [Fact]
    public async Task PausedBandWithCleanTreeStillPushesTheBranchWithoutANewCommit()
    {
        var sim = new KbSim { BandJson = PausedBandJson("2026-09-14T11:58:00Z") };
        sim.Held.Add("wi-clean");
        using var wa = new WorkArea();
        var (dir, origin) = wa.Repo("wi-clean", branch: "coder-06-pi6/wi-clean", dirty: false);
        var baseSha = Git(dir, "rev-parse", "HEAD");
        using var client = Client(KbHandler(sim));
        var steering = new FactorySteering(client, new FakeClock()) { Actor = "coder-06-pi6", WorkArea = wa.Root };

        await steering.CheckRateBandAsync();

        Assert.Equal("base", Git(dir, "log", "-1", "--format=%s"));
        Assert.Equal(baseSha, Git(dir, "--git-dir", origin, "rev-parse", "refs/heads/coder-06-pi6/wi-clean"));
        var note = Assert.Single(sim.Calls, c => c["name"]!.GetValue<string>() == "kb_write");
        Assert.Contains("clean (nothing to commit)", note["arguments"]!["doc"]!["body"]!.GetValue<string>());
        var transition = Assert.Single(sim.Calls, c => c["name"]!.GetValue<string>() == "kb_transition");
        Assert.Equal("wi-clean", transition["arguments"]!["work_item"]!.GetValue<string>());
    }

    [Fact]
    public async Task PausedBandOnTheDefaultBranchNeverCommitsOrPushesButStillHandsBack()
    {
        var sim = new KbSim { BandJson = PausedBandJson("2026-09-14T11:58:00Z") };
        sim.Held.Add("wi-main");
        using var wa = new WorkArea();
        var (dir, origin) = wa.Repo("wi-main", branch: "main", dirty: true);
        var baseSha = Git(dir, "rev-parse", "HEAD");
        using var client = Client(KbHandler(sim));
        var steering = new FactorySteering(client, new FakeClock()) { Actor = "coder-06-pi6", WorkArea = wa.Root };

        await steering.CheckRateBandAsync();

        Assert.Equal("base", Git(dir, "log", "-1", "--format=%s"));                 // no WIP commit
        Assert.Equal(baseSha, Git(dir, "--git-dir", origin, "rev-parse", "refs/heads/main"));  // nothing pushed
        Assert.Equal(" M app.txt\n", GitRaw(dir, "status", "--porcelain"));  // dirty state left as-is (leading space is the porcelain format)
        var note = Assert.Single(sim.Calls, c => c["name"]!.GetValue<string>() == "kb_write");
        Assert.Contains("on the default branch main", note["arguments"]!["doc"]!["body"]!.GetValue<string>());
        var transition = Assert.Single(sim.Calls, c => c["name"]!.GetValue<string>() == "kb_transition");
        Assert.Equal("ready", transition["arguments"]!["to_status"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("ok")]
    [InlineData("low")]
    [InlineData("critical")]
    [InlineData("bogus")]  // outside the four bands the sweep may write: not a pause, nothing to act on
    public async Task NonPausedOrUnclassifiedBandsDoNothing(string band)
    {
        var sim = new KbSim { BandJson = $"{{\"attrs\":{{\"rate_band\":\"{band}\"}}}}" };
        sim.Held.Add("wi-paused");
        using var wa = new WorkArea();
        using var client = Client(KbHandler(sim));
        var steering = new FactorySteering(client, new FakeClock()) { Actor = "coder-06-pi6", WorkArea = wa.Root };

        await steering.CheckRateBandAsync();

        Assert.DoesNotContain(sim.Calls, c => c["name"]!.GetValue<string>() is "kb_list" or "kb_write" or "kb_transition" or "chat_post");
    }

    /// <summary>2026-10-16: a hybrid seat runs every turn locally when paused, so it keeps its work.</summary>
    [Fact]
    public async Task APausedHybridSeatKeepsItsWork()
    {
        var sim = new KbSim { BandJson = """{"attrs":{"rate_band":"paused","rate_band_since":"2026-09-14T11:58:00Z","rate_hybrid":true}}""" };
        sim.Held.Add("wi-paused");
        using var wa = new WorkArea();
        using var client = Client(KbHandler(sim));
        var steering = new FactorySteering(client, new FakeClock()) { Actor = "coder-06-pi6", WorkArea = wa.Root };

        await steering.CheckRateBandAsync();

        Assert.DoesNotContain(sim.Calls, c => c["name"]!.GetValue<string>() is "kb_list" or "kb_write" or "kb_transition" or "chat_post");
    }

    [Fact]
    public async Task BandChecksAreThrottledAndTheSamePausedEpochIsNotRepeatedUntilANewSince()
    {
        var sim = new KbSim { BandJson = PausedBandJson("2026-09-14T11:58:00Z") };
        sim.Held.Add("wi-paused");
        using var wa = new WorkArea();
        var (dir, _) = wa.Repo("wi-paused", branch: "coder-06-pi6/wi-paused", dirty: true);
        using var client = Client(KbHandler(sim));
        var clock = new FakeClock();
        var steering = new FactorySteering(client, clock) { Actor = "coder-06-pi6", WorkArea = wa.Root };
        int actorGets() => sim.Calls.Count(c => c["name"]!.GetValue<string>() == "kb_get" && c["arguments"]!["slug"]!.GetValue<string>() == "coder-06-pi6");
        int transitions() => sim.Calls.Count(c => c["name"]!.GetValue<string>() == "kb_transition");

        await steering.CheckRateBandAsync();
        Assert.Equal(1, transitions());
        await steering.CheckRateBandAsync();                      // same tick: throttled out
        Assert.Equal(1, actorGets());
        clock.Advance(TimeSpan.FromSeconds(61));
        await steering.CheckRateBandAsync();                      // new check, same pause epoch: handed back already
        Assert.Equal(2, actorGets());
        Assert.Equal(1, transitions());
        sim.BandJson = PausedBandJson("2026-09-14T12:30:00Z");   // a second pause starts fresh
        clock.Advance(TimeSpan.FromSeconds(61));
        await steering.CheckRateBandAsync();
        Assert.Equal(2, transitions());
        Assert.Equal(2, sim.Calls.Count(c => c["name"]!.GetValue<string>() == "kb_write"));
        _ = dir;
    }

    [Fact]
    public async Task PausedBandWithoutAWorkingDirectoryHandsBackWithAHonestNote()
    {
        var sim = new KbSim { BandJson = PausedBandJson("2026-09-14T11:58:00Z") };
        sim.Held.Add("wi-gone");
        using var wa = new WorkArea();                            // empty: no wi-gone directory
        using var client = Client(KbHandler(sim));
        var steering = new FactorySteering(client, new FakeClock()) { Actor = "coder-06-pi6", WorkArea = wa.Root };

        await steering.CheckRateBandAsync();

        var note = Assert.Single(sim.Calls, c => c["name"]!.GetValue<string>() == "kb_write");
        Assert.Contains("no working directory", note["arguments"]!["doc"]!["body"]!.GetValue<string>());
        var transition = Assert.Single(sim.Calls, c => c["name"]!.GetValue<string>() == "kb_transition");
        Assert.Equal("wi-gone", transition["arguments"]!["work_item"]!.GetValue<string>());
        Assert.Single(sim.Calls, c => c["name"]!.GetValue<string>() == "chat_post");
    }

    [Fact]
    public async Task PausedBandWhosePushFailsStillHandsBackWithTheFailureInTheNote()
    {
        var sim = new KbSim { BandJson = PausedBandJson("2026-09-14T11:58:00Z") };
        sim.Held.Add("wi-push");
        using var wa = new WorkArea();
        var (dir, _) = wa.Repo("wi-push", branch: "coder-06-pi6/wi-push", dirty: true, brokenOrigin: true);
        using var client = Client(KbHandler(sim));
        var steering = new FactorySteering(client, new FakeClock()) { Actor = "coder-06-pi6", WorkArea = wa.Root };

        await steering.CheckRateBandAsync();

        Assert.Equal("WIP handover wi-push: rate band paused", Git(dir, "log", "-1", "--format=%s"));  // committed locally
        var note = Assert.Single(sim.Calls, c => c["name"]!.GetValue<string>() == "kb_write");
        Assert.Contains("push to origin failed", note["arguments"]!["doc"]!["body"]!.GetValue<string>());
        var transition = Assert.Single(sim.Calls, c => c["name"]!.GetValue<string>() == "kb_transition");
        Assert.Equal("team-lead-2", transition["arguments"]!["assign_to"]!.GetValue<string>());
    }

    [Fact]
    public async Task PausedBandWhoseNoteFailsKeepsTheItemAssignedUntilANewCheckSucceeds()
    {
        var sim = new KbSim { BandJson = PausedBandJson("2026-09-14T11:58:00Z") };
        sim.Held.Add("wi-note");
        sim.FailNotes = true;
        using var wa = new WorkArea();
        var (dir, _) = wa.Repo("wi-note", branch: "coder-06-pi6/wi-note", dirty: true);
        using var client = Client(KbHandler(sim));
        var clock = new FakeClock();
        var steering = new FactorySteering(client, clock) { Actor = "coder-06-pi6", WorkArea = wa.Root };

        await steering.CheckRateBandAsync();                      // note write fails: nothing is reassigned
        Assert.DoesNotContain(sim.Calls, c => c["name"]!.GetValue<string>() is "kb_transition" or "chat_post");

        sim.FailNotes = false;
        clock.Advance(TimeSpan.FromSeconds(61));                  // same epoch, not latched: retried
        await steering.CheckRateBandAsync();

        var transition = Assert.Single(sim.Calls, c => c["name"]!.GetValue<string>() == "kb_transition");
        Assert.Equal("wi-note", transition["arguments"]!["work_item"]!.GetValue<string>());
        Assert.Single(sim.Calls, c => c["name"]!.GetValue<string>() == "chat_post");
        _ = dir;
    }

    // ---------------------------------------------------------------- fixtures

    private static FactoryClient Client(FakeHandler handler) => new(KbUrl, ChatUrl, Token, handler);

    private static string Rpc(string toolText, bool isError = false) =>
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

    private static JsonObject Item(
        long messageId,
        string reason = "mention",
        long? thread = null,
        string author = "coder-06-pi2",
        string room = "board",
        string kind = "note",
        string body = "do the thing")
    {
        var message = new JsonObject
        {
            ["id"] = messageId,
            ["room"] = room,
            ["author"] = author,
            ["kind"] = kind,
            ["body"] = body,
            ["mentions"] = new JsonArray(Self),
            ["about"] = new JsonArray(),
            ["at"] = "2026-09-14T16:41:00Z",
        };
        if (thread is not null) message["thread"] = thread.Value;
        return new JsonObject
        {
            ["inboxId"] = 90_000 + messageId,
            ["messageId"] = messageId,
            ["reason"] = reason,
            ["message"] = message,
        };
    }

    /// <summary>An item the parser must refuse: the top-level messageId disagrees with message.id.</summary>
    private static JsonObject Mismatched(long messageId)
    {
        var item = Item(messageId);
        item["messageId"] = messageId + 1_000;
        return item;
    }

    /// <summary>An item the parser must refuse for having no message object: only the top-level ids survive.</summary>
    private static JsonObject WithoutMessageObject(long messageId)
    {
        var item = Item(messageId);
        item.Remove("message");
        return item;
    }

    /// <summary>A heads-only row as the live chat server sends it: the message's fields inline, a one-line head
    /// instead of the body, no nested object.</summary>
    private static JsonObject HeadOnly(long messageId, string reason = "mention", string head = "do the thing")
    {
        var item = Item(messageId, reason);
        item.Remove("message");
        item["room"] = "board";
        item["author"] = "coder-06-pi2";
        item["kind"] = "note";
        item["head"] = head;
        item["at"] = "2026-09-14T16:41:00Z";
        item["replies"] = 1;
        return item;
    }

    /// <summary>An item the parser must refuse that carries no usable id at all, so it can never be acked.</summary>
    private static JsonObject WithoutAnyMessageId()
    {
        var item = Item(330);
        item.Remove("messageId");
        item["message"]!.AsObject().Remove("id");
        return item;
    }

    private static string DigestText(IEnumerable<JsonObject> items, bool? timedOut = null)
    {
        var array = new JsonArray();
        // Clone: the same fixture object is served on every latched digest, and a JsonNode has one parent.
        foreach (var item in items) array.Add(item.DeepClone());
        var obj = new JsonObject
        {
            ["inbox"] = array,
            ["rooms"] = new JsonArray(),
            ["reachable"] = new JsonArray(),
        };
        if (timedOut is not null) obj["timedOut"] = timedOut.Value;
        return obj.ToJsonString();
    }

    /// <summary>Answers each tools/call by tool name; the returned string is the tool's text payload.</summary>
    private static FakeHandler ByTool(Func<string, string> respond, bool isError = false) =>
        new(async request =>
        {
            var body = await request.Content!.ReadAsStringAsync();
            var tool = JsonNode.Parse(body)!["params"]!["name"]!.GetValue<string>();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Rpc(respond(tool), isError), Encoding.UTF8, "application/json"),
            };
        });

    /// <summary>Like <see cref="ByTool"/>, but chat_ack answers a receipt acknowledging every id it was sent, so a
    /// test about drops or digests is not derailed by an ack the digest fixture cannot answer.</summary>
    private static FakeHandler ByToolAckingAll(Func<string, string> respond) =>
        new(async request =>
        {
            var body = await request.Content!.ReadAsStringAsync();
            var call = JsonNode.Parse(body)!["params"]!;
            var tool = call["name"]!.GetValue<string>();
            string text;
            if (tool == "chat_ack")
            {
                var ids = new JsonArray();
                foreach (var id in call["arguments"]!["message_ids"]!.AsArray().Select(n => n!.GetValue<long>()))
                    ids.Add((JsonNode)JsonValue.Create(id));
                text = new JsonObject
                {
                    ["actor"] = "coder-06-pi1",
                    ["acknowledged"] = ids,
                    ["alreadySeen"] = new JsonArray(),
                    ["unknown"] = new JsonArray(),
                }.ToJsonString();
            }
            else
            {
                text = respond(tool);
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Rpc(text), Encoding.UTF8, "application/json"),
            };
        });

    /// <summary>An in-memory chat with the server's real latch: every digest carries every delivered item whose
    /// nested message id has not been acked; chat_wait returns at once while any exists (timedOut otherwise);
    /// chat_ack answers acknowledged / alreadySeen / unknown by nested message id, never by inbox id.</summary>
    private sealed class LatchedChat
    {
        private readonly object _gate = new();
        private readonly List<JsonObject> _items = [];
        private readonly HashSet<long> _handled = [];

        /// <summary>chat_post answers HTTP 502, exercising the transient retry path.</summary>
        public bool FailPosts { get; set; }

        /// <summary>When true, chat_post answers HTTP 502 for posts whose thread is in this set; everything
        /// else succeeds. Used to make one target transiently fail while its sibling succeeds.</summary>
        public HashSet<long>? FailThreads { get; set; }

        /// <summary>chat_post answers with this HTTP refusal and exact body.</summary>
        public int? RefusePostsStatus { get; set; }
        public string RefusalBody { get; set; } = "`10` is not in this conversation";

        /// <summary>chat_ack answers HTTP 502.</summary>
        public bool FailAcks { get; set; }

        /// <summary>kb_list's work items (slug, status) for the seat; null = the KB call fails.</summary>
        public List<(string Slug, string Status)>? Held { get; set; }

        /// <summary>kb_get (no doc) of a work item: its summary, the epic's objective.</summary>
        public Dictionary<string, string> Objectives { get; } = new(StringComparer.Ordinal);

        /// <summary>Held items that wait on a test run: kb_list gives them a <c>waitingOn</c>.</summary>
        public HashSet<string> WaitingOnRun { get; } = new(StringComparer.Ordinal);

        /// <summary>kb_get doc=memory: each epic's memory document; an epic without one answers "no document".</summary>
        public Dictionary<string, string> Memory { get; } = new(StringComparer.Ordinal);

        /// <summary>Nested ids still latched (delivered, not acked).</summary>
        public List<long> Unhandled
        {
            get { lock (_gate) return _items.Select(Id).Where(id => !_handled.Contains(id)).ToList(); }
        }

        /// <summary>Adds items to the inbox; like the server's insert, an id already there is left alone.</summary>
        public void Deliver(params JsonObject[] items)
        {
            lock (_gate)
                foreach (var item in items)
                    if (!_items.Exists(i => Id(i) == Id(item))) _items.Add(item);
        }

        /// <summary>Removes an item from the inbox entirely, so a later ack of it comes back unknown.</summary>
        public void Evict(long id)
        {
            lock (_gate) _items.RemoveAll(i => Id(i) == id);
        }

        public FakeHandler Handler() => new(async request =>
        {
            var body = await request.Content!.ReadAsStringAsync();
            var envelope = JsonNode.Parse(body)!;
            var tool = envelope["params"]!["name"]!.GetValue<string>();
            var arguments = envelope["params"]!["arguments"]!.AsObject();
            lock (_gate)
            {
                switch (tool)
                {
                    case "chat_read":
                        return Ok(DigestText(Latched()));
                    case "chat_wait":
                        var latched = Latched();
                        return Ok(DigestText(latched, timedOut: latched.Count == 0));
                    case "chat_post":
                        {
                            bool fail = FailPosts;
                            if (FailThreads is not null
                                && arguments.TryGetPropertyValue("thread", out var threadNode))
                                fail = fail || FailThreads.Contains(threadNode!.GetValue<long>());
                            if (fail) return HttpError(HttpStatusCode.BadGateway, "temporary chat backend failure");
                            if (RefusePostsStatus is { } refusal)
                                return HttpError((HttpStatusCode)refusal, RefusalBody);
                            return Ok("""{"id":9001,"room":"board"}""");
                        }
                    case "chat_manage":
                        return Ok("""{"ok":true}""");
                    case "kb_get" when arguments["doc"]?.GetValue<string>() == "memory":
                        {
                            var slug = arguments["slug"]!.GetValue<string>();
                            if (Held is null) return Ok("KB down", isError: true);
                            return Memory.TryGetValue(slug, out var memory)
                                ? Ok(new JsonObject { ["slug"] = slug, ["docs"] = new JsonArray(new JsonObject { ["key"] = "memory", ["body"] = memory, ["retiredAt"] = null }) }.ToJsonString())
                                : Ok($"'{slug}' has no document 'memory'.", isError: true);
                        }
                    case "kb_get" when arguments["doc"] is null && Objectives.TryGetValue(arguments["slug"]!.GetValue<string>(), out var objective):
                        return Ok(new JsonObject { ["slug"] = arguments["slug"]!.GetValue<string>(), ["name"] = "The epic", ["summary"] = objective }.ToJsonString());
                    case "kb_list":
                        if (Held is null) return Ok("KB down", isError: true);
                        var rows = new JsonArray();
                        foreach (var (slug, status) in Held)
                            rows.Add(new JsonObject { ["kind"] = "work_item", ["slug"] = slug, ["status"] = status,
                                ["waitingOn"] = WaitingOnRun.Contains(slug) ? "strata:20261025-060000-abcd" : null });
                        return Ok(new JsonObject { ["total"] = Held.Count, ["count"] = Held.Count, ["items"] = rows }.ToJsonString());
                    case "chat_ack":
                        if (FailAcks) return new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent("down") };
                        var acknowledged = new JsonArray();
                        var alreadySeen = new JsonArray();
                        var unknown = new JsonArray();
                        foreach (var id in arguments["message_ids"]!.AsArray().Select(n => n!.GetValue<long>()).Distinct())
                        {
                            var target = !_items.Exists(i => Id(i) == id) ? unknown : _handled.Add(id) ? acknowledged : alreadySeen;
                            target.Add((JsonNode)JsonValue.Create(id));
                        }
                        return Ok(new JsonObject
                        {
                            ["actor"] = "coder-06-pi1",
                            ["acknowledged"] = acknowledged,
                            ["alreadySeen"] = alreadySeen,
                            ["unknown"] = unknown,
                        }.ToJsonString());
                    default:
                        return Ok("Unknown tool.", isError: true);
                }
            }
        });

        private List<JsonObject> Latched() => _items.Where(i => !_handled.Contains(Id(i))).ToList();

        // A heads-only row (no nested object) is identified by its top-level messageId, exactly as the server
        // latches it.
        private static long Id(JsonObject item) => (item["message"]?["id"] ?? item["messageId"]!).GetValue<long>();

        private static HttpResponseMessage Ok(string toolText, bool isError = false) =>
            new(HttpStatusCode.OK) { Content = new StringContent(Rpc(toolText, isError), Encoding.UTF8, "application/json") };

        private static HttpResponseMessage HttpError(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "text/plain") };
    }

    private static List<FakeRequest> Snapshot(FakeHandler handler)
    {
        lock (handler.Requests) return [.. handler.Requests];
    }

    private static List<string> Tools(FakeHandler handler) => Snapshot(handler).Select(r => r.Tool).ToList();

    private static List<FakeRequest> Acks(FakeHandler handler) => Snapshot(handler).FindAll(r => r.Tool == "chat_ack");

    private static List<long> AckIds(FakeRequest request) =>
        request.Arguments["message_ids"]!.AsArray().Select(n => n!.GetValue<long>()).ToList();

    private static AssistantMessage Assistant(string text, bool toolCall = false)
    {
        var content = new List<ContentPart>();
        if (text.Length > 0) content.Add(new TextContent(text));
        if (toolCall) content.Add(new ToolCallContent("call-1", "bash", new JsonObject { ["command"] = "ls" }));
        return new AssistantMessage { Content = content, Model = "test-model" };
    }

    private static TurnContext Turn(AgentContext context, AssistantMessage message) => new(message, [], context, []);

    private static AgentContext Context(List<IAgentTool> tools) =>
        new() { SystemPrompt = "you are attached", Tools = tools, Messages = [new UserMessage("previous task")] };

    private static string Text(Message message) => Assert.IsType<UserMessage>(message).Text;

    private static List<FakeRequest> Posts(FakeHandler handler)
    {
        lock (handler.Requests) return handler.Requests.FindAll(r => r.Tool == "chat_post");
    }

    private static List<FakeRequest> StatusCalls(FakeHandler handler)
    {
        lock (handler.Requests) return handler.Requests.FindAll(r => r.Tool == "chat_manage");
    }

    private static int Count(FakeHandler handler)
    {
        lock (handler.Requests) return handler.Requests.Count;
    }

    // ---------------------------------------------------------------- fake-clock driver

    /// <summary>Waits until the loop parks on a timer, then fires the earliest one. False once the task finished.</summary>
    private static async Task<bool> StepAsync(FakeClock clock, Task task)
    {
        var deadline = DateTime.UtcNow + RealBudget;
        while (!task.IsCompleted)
        {
            var scheduled = clock.Scheduled;
            if (clock.HasPendingTimer)
            {
                clock.AdvanceToNextTimer();
                return true;
            }
            if (DateTime.UtcNow > deadline) throw new TimeoutException("the steering loop never parked on a timer");
            await Task.WhenAny(task, scheduled, Task.Delay(TimeSpan.FromMilliseconds(25)));
        }
        return false;
    }

    private static async Task<IReadOnlyList<Message>> DriveAsync(
        FakeClock clock,
        ValueTask<IReadOnlyList<Message>> pending,
        int maxSteps = 400)
    {
        var task = pending.AsTask();
        for (var i = 0; i < maxSteps && !task.IsCompleted; i++)
            if (!await StepAsync(clock, task)) break;
        Assert.True(task.IsCompleted, "the steering loop did not finish inside the step budget");
        return await task;
    }

    /// <summary>Drives an arbitrary task (e.g. a real <see cref="AgentLoop"/> run) by stepping the fake clock until it
    /// completes or the step budget trips. The generic half of the wi-78 driver: the loop is driven on virtual clock
    /// only, so a poll that still loops on real wall clock never completes and the assertion below fails fast.</summary>
    private static async Task<T> DriveTaskAsync<T>(FakeClock clock, Task<T> task, int maxSteps = 400)
    {
        for (var i = 0; i < maxSteps && !task.IsCompleted; i++)
            if (!await StepAsync(clock, task)) break;
        Assert.True(task.IsCompleted, "the run did not finish inside the step budget");
        return await task;
    }

    // ---------------------------------------------------------------- 1. steering poll

    [Fact]
    public async Task SteeringTurnsUnseenInboxItemsIntoUserMessagesAndDedupesById()
    {
        var latched = new[]
        {
            Item(2965, "mention", thread: 2940, author: "architect-1", room: "architecture", body: "ship it"),
            Item(2966, "dm", thread: null, author: "digital-scribe", room: "dm", body: "status?"),
        };
        using var handler = ByTool(_ => DigestText(latched));
        using var client = Client(handler);
        var steering = new FactorySteering(client);

        var first = await steering.GetSteeringMessagesAsync();
        Assert.Equal(
            [
                "[inbox from @architect-1 in architecture, message 2965, thread 2940, mention] ship it",
                "[inbox from @digital-scribe in dm, message 2966, dm] status?",
            ],
            first.Select(Text));
        Assert.Equal(2, steering.Seen.Count);
        Assert.Contains(2965L, steering.Seen);
        Assert.Contains(2966L, steering.Seen);

        // The server latches items until chat_ack, so the very same payload comes back: nothing new.
        Assert.Empty(await steering.GetSteeringMessagesAsync());
        Assert.Equal(2, steering.Seen.Count);

        // Besides the status line the new task writes (chat_manage), only the two digests.
        var reads = handler.Requests.FindAll(r => r.Tool != "chat_manage");
        Assert.Equal(2, reads.Count);
        Assert.All(reads, r =>
        {
            Assert.Equal("chat_read", r.Tool);
            Assert.True(r.Arguments["mark_read"]!.GetValue<bool>());
            Assert.False(r.Arguments.ContainsKey("room"));
        });
    }

    [Fact]
    public async Task SteeringGrowsSeenWithEachNewMessage()
    {
        var pending = new List<JsonObject> { Item(10) };
        using var handler = ByTool(_ => DigestText(pending));
        using var client = Client(handler);
        var steering = new FactorySteering(client);

        Assert.Single(await steering.GetSteeringMessagesAsync());
        Assert.Single(steering.Seen);
        pending.Add(Item(11));
        Assert.Single(await steering.GetSteeringMessagesAsync());
        Assert.Equal(2, steering.Seen.Count);
        pending.Add(Item(12));
        Assert.Single(await steering.GetSteeringMessagesAsync());
        Assert.Equal([10L, 11L, 12L], steering.Seen.Order());
    }

    // ---------------------------------------------------------------- 2. current item and context reset

    [Fact]
    public async Task CurrentIsFirstItemOfFirstBatchAndPrepareNextTurnResetsContext()
    {
        using var handler = ByTool(_ => DigestText([Item(2965, thread: 2940), Item(2966, thread: 2941)]));
        using var client = Client(handler);
        var steering = new FactorySteering(client);
        Assert.Null(steering.Current);

        Assert.Equal(2, (await steering.GetSteeringMessagesAsync()).Count);
        Assert.NotNull(steering.Current);
        Assert.Equal(2965, steering.Current.Message.Id);

        var tools = new List<IAgentTool>();
        var context = Context(tools);
        var update = await steering.PrepareNextTurnAsync(Turn(context, Assistant("done")));

        Assert.NotNull(update);
        Assert.NotNull(update.Context);
        Assert.NotSame(context, update.Context);
        Assert.Equal("you are attached", update.Context.SystemPrompt);
        Assert.Same(tools, update.Context.Tools);
        Assert.Empty(update.Context.Messages);
        Assert.Null(update.Model);
        Assert.Null(update.ThinkingLevel);

        // The arrival is consumed: a second call resets nothing.
        Assert.Null(await steering.PrepareNextTurnAsync(Turn(context, Assistant("done"))));
    }

    [Fact]
    public async Task PrepareNextTurnReturnsNullWithoutArrival()
    {
        using var handler = ByTool(_ => DigestText([]));
        using var client = Client(handler);
        var steering = new FactorySteering(client);

        Assert.Empty(await steering.GetSteeringMessagesAsync());
        Assert.Null(await steering.PrepareNextTurnAsync(Turn(Context([]), Assistant("idle"))));
    }

    // ---------------------------------------------------------------- operator restart trigger

    [Fact]
    public async Task AuthorizedOperatorRestartIsConsumedAndStopsAfterCurrentTurn()
    {
        var chat = new LatchedChat();
        using var handler = chat.Handler();
        using var client = Client(handler);
        var lines = new List<string>();
        var steering = new FactorySteering(client) { Actor = "coder-06-pi6", Diagnostics = lines.Add };
        chat.Deliver(Item(701, reason: "dm", author: "digital-scribe", body: "/restart\noperator-request"));

        Assert.Empty(await steering.GetSteeringMessagesAsync());
        Assert.True(steering.OperatorRestartRequested);
        Assert.True(await steering.ShouldStopAfterTurnAsync(Turn(Context([]), Assistant("current turn finished"))));
        Assert.Contains(lines, line => line.Contains("seat=@coder-06-pi6") && line.Contains("requester=@digital-scribe"));
    }

    [Fact]
    public async Task MigrationWakeFromTheKbIsARestart_FromAnyoneElseItIsIgnored()
    {
        var chat = new LatchedChat();
        using var handler = chat.Handler();
        using var client = Client(handler);
        var lines = new List<string>();
        var steering = new FactorySteering(client) { Actor = "coder-06-pi6", Diagnostics = lines.Add };
        chat.Deliver(Item(711, reason: "dm", author: "coder-06-pi2", body: "/migrate-out 4"));
        Assert.Empty(await steering.GetSteeringMessagesAsync());
        Assert.False(steering.OperatorRestartRequested);
        Assert.Contains(lines, line => line.Contains("ignored /migrate-out from @coder-06-pi2"));

        chat.Deliver(Item(712, reason: "dm", author: FactorySteering.MigrationWakeAuthor, body: "/migrate-in 4"));
        Assert.Empty(await steering.GetSteeringMessagesAsync());
        Assert.True(steering.OperatorRestartRequested);
    }

    [Fact]
    public async Task UnauthorizedRestartCommandIsIgnoredAndDoesNotStop()
    {
        var chat = new LatchedChat();
        using var handler = chat.Handler();
        using var client = Client(handler);
        var lines = new List<string>();
        var clock = new FakeClock();
        var steering = new FactorySteering(client, clock) { Diagnostics = lines.Add };
        chat.Deliver(Item(702, author: "coder-06-pi2", body: "/restart"));

        Assert.Empty(await steering.GetSteeringMessagesAsync());
        Assert.False(steering.OperatorRestartRequested);
        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(Context([]), Assistant("continue"))));
        Assert.Contains(lines, line => line.Contains("ignored /restart from @coder-06-pi2"));
    }

    [Fact]
    public async Task RestartCommandInRoomIsNotAnOperatorTrigger()
    {
        var chat = new LatchedChat();
        using var handler = chat.Handler();
        using var client = Client(handler);
        var steering = new FactorySteering(client);
        chat.Deliver(Item(703, reason: "mention", author: "digital-scribe", body: "/restart"));

        Assert.Empty(await steering.GetSteeringMessagesAsync());
        Assert.False(steering.OperatorRestartRequested);
    }

    // ---------------------------------------------------------------- 2b. fresh context on assignment only

    private static FactorySteering AssignmentSteering(FactoryClient client) => new(client)
    {
        FreshContextOnAssignment = true,
        FreshTaskFrom = new HashSet<string>(StringComparer.Ordinal) { "team-lead-2" },
    };

    private static string TrailingText(TurnUpdate? update) =>
        string.Join("\n", (update?.Trailing ?? []).OfType<UserMessage>().Select(m => ContentText.Of(m.Content)));

    // ---------------------------------------------------------------- 2c. one epic, one session

    private static FactorySteering EpicSteering(FactoryClient client) => new(client)
    {
        FreshContextOnAssignment = true,
        FreshTaskFrom = new HashSet<string>(StringComparer.Ordinal) { "team-lead-2" },
        Actor = "coder-06-pi5",
    };

    private static string MessagesText(IEnumerable<Message> messages) =>
        string.Join("\n", messages.OfType<UserMessage>().Select(m => ContentText.Of(m.Content)));

    [Fact]
    public async Task TheFirstArrivalBindsTheSessionToTheHeldEpic_WithItsMemory()
    {
        var chat = new LatchedChat { Held = [("hc2000-hdd", "in_progress"), ("hc2000-tape", "ready")] };
        chat.Memory["hc2000-hdd"] = "Goal: hard disks. Next: the catalog.";
        using var handler = chat.Handler();
        using var client = Client(handler);
        var steering = EpicSteering(client);

        chat.Deliver(Item(10, body: "carry on"));
        var arrived = await steering.GetSteeringMessagesAsync();
        Assert.Contains("[epic memory of hc2000-hdd", MessagesText(arrived));
        Assert.Contains("Next: the catalog.", MessagesText(arrived));
        Assert.Null(await steering.PrepareNextTurnAsync(Turn(Context([]), Assistant("working"))));
        Assert.Equal("hc2000-hdd", steering.BoundEpic);
        Assert.Contains("Next: the catalog.", steering.EpicMemoryNote());
    }

    [Fact]
    public async Task AStartedSeatResumesItsEpicFromTheMemory_AndKeepsItWhenAnotherItemComesBack()
    {
        // Audit 2026-10-24: binding ran only on inbox arrivals, so a restarted seat resumed without its memory; and held[0]
        // (the most recently touched) flipped the session to an epic coming back from review.
        var chat = new LatchedChat { Held = [("hc2000-hdd", "in_progress")] };
        chat.Memory["hc2000-hdd"] = "Goal: hard disks. Next: the catalog.";
        using var handler = chat.Handler();
        using var client = Client(handler);
        var steering = EpicSteering(client);

        var prompt = await steering.WithEpicMemoryAsync("Service start for coder-06-pi5.");
        Assert.StartsWith("Service start for coder-06-pi5.", prompt);
        Assert.Contains("[epic memory of hc2000-hdd", prompt);
        Assert.Contains("Next: the catalog.", prompt);
        Assert.Equal("hc2000-hdd", steering.BoundEpic);

        chat.Held = [("hc2000-tape", "ready"), ("hc2000-hdd", "in_progress")];
        chat.Deliver(Item(10, body: "hc2000-tape is back from review"));
        Assert.DoesNotContain("[epic memory of hc2000-tape", MessagesText(await steering.GetSteeringMessagesAsync()));
        Assert.Equal("hc2000-hdd", steering.BoundEpic);
    }

    // ---------------------------------------------------------------- 2d. a free seat never sits on placed work

    private static FactorySteering IdleSteering(FactoryClient client, FakeClock clock) => new(client, clock)
    {
        FreshContextOnAssignment = true,
        FreshTaskFrom = new HashSet<string>(StringComparer.Ordinal) { "team-lead-2" },
        Actor = "coder-06-pi6",
        FollowUpWaitSeconds = 30,
        LatchPollInterval = TimeSpan.FromSeconds(5),
        IdleTimeout = TimeSpan.FromSeconds(10),
    };

    [Fact]
    public async Task AnIdleSeatHoldingAPlacedEpicWakesItselfOnceForIt()
    {
        // pi6, 2026-10-24: hc2000-app-machine-tabs-config placed at 16:12Z during a restart; from 20:02Z the seat sat in
        // chat_wait with it ready on it.
        var chat = new LatchedChat { Held = [("hc2000-app-machine-tabs-config", "ready")] };
        chat.Memory["hc2000-app-machine-tabs-config"] = "Goal: tabs. Next: the Paged Save proof.";
        using var handler = chat.Handler();
        using var client = Client(handler);
        var clock = new FakeClock();
        var steering = IdleSteering(client, clock);

        var wake = MessagesText(await DriveAsync(clock, steering.GetFollowUpMessagesAsync()));
        Assert.Contains("[harness] You hold hc2000-app-machine-tabs-config: it was placed on you and is not started yet", wake);
        Assert.Contains("kb_transition work_item=hc2000-app-machine-tabs-config to_status=in_progress", wake);
        Assert.Contains("Next: the Paged Save proof.", wake);
        Assert.Equal("hc2000-app-machine-tabs-config", steering.BoundEpic);

        // It looked and left it: the same item in the same status does not wake it again.
        Assert.Empty(await DriveAsync(clock, steering.GetFollowUpMessagesAsync()));
        Assert.Empty(Posts(handler));
    }

    /// <summary>2026-10-30: the harness study placed on pi10 and pi11 while they waited idle reached them only with a message
    /// (05:37-05:43Z). A waiting seat asks the KB now and then whether work was placed on it, and wakes itself for it.</summary>
    [Fact]
    public async Task WorkPlacedOnASeatAlreadyWaitingWakesItWithinTheCheckInterval()
    {
        var chat = new LatchedChat();
        using var handler = chat.Handler();
        using var client = Client(handler);
        var clock = new FakeClock();
        var steering = new FactorySteering(client, clock)
        {
            FreshContextOnAssignment = true,
            FreshTaskFrom = new HashSet<string>(StringComparer.Ordinal) { "team-lead-2" },
            Actor = "coder-06-pi10",
            FollowUpWaitSeconds = 30,
            LatchPollInterval = TimeSpan.FromSeconds(5),
            IdleTimeout = TimeSpan.FromMinutes(30),
            PlacementCheckEvery = TimeSpan.FromMinutes(1),
        };

        var waiting = steering.GetFollowUpMessagesAsync().AsTask();
        for (var i = 0; i < 4 && !waiting.IsCompleted; i++) await StepAsync(clock, waiting);
        Assert.False(waiting.IsCompleted); // nothing held: it waits
        chat.Held = [("harness-study-landscape", "ready")];

        var wake = MessagesText(await DriveTaskAsync(clock, waiting));
        Assert.Contains("[harness] You hold harness-study-landscape: it was placed on you and is not started yet", wake);
    }

    [Fact]
    public async Task AnInboxTaskRearmsTheWake_AndAStartedEpicWakesForItsNextStep()
    {
        var chat = new LatchedChat { Held = [("hc2000-hdd", "ready")] };
        using var handler = chat.Handler();
        using var client = Client(handler);
        var clock = new FakeClock();
        var steering = IdleSteering(client, clock);
        var context = Context([]);

        Assert.Contains("not started yet", MessagesText(await DriveAsync(clock, steering.GetFollowUpMessagesAsync())));
        Assert.Empty(await DriveAsync(clock, steering.GetFollowUpMessagesAsync()));

        chat.Deliver(Item(20, body: "a question about hc2000-hdd"));
        Assert.NotEmpty(await steering.GetSteeringMessagesAsync());
        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(context, Assistant("answered"))));
        Assert.Contains("not started yet", MessagesText(await DriveAsync(clock, steering.GetFollowUpMessagesAsync())));

        chat.Held = [("hc2000-hdd", "in_progress")];
        Assert.Contains("[harness] You hold hc2000-hdd in progress", MessagesText(await DriveAsync(clock, steering.GetFollowUpMessagesAsync())));
        Assert.Empty(await DriveAsync(clock, steering.GetFollowUpMessagesAsync()));
    }

    // plan-pi-sharp-codex item 14 (Codex goal continuation): the held epic is the seat's goal while each continuation acts.
    [Fact]
    public async Task AnEpicInProgressIsContinuedWhileEachContinuationActs_WithItsCriteria()
    {
        var chat = new LatchedChat { Held = [("hc2000-hdd", "in_progress")] };
        chat.Objectives["hc2000-hdd"] = "Acceptance: C: and D: switch at runtime; proven by recorded tests.";
        using var handler = chat.Handler();
        using var client = Client(handler);
        var clock = new FakeClock();
        var steering = IdleSteering(client, clock);
        var context = Context([]);

        var first = MessagesText(await DriveAsync(clock, steering.GetFollowUpMessagesAsync()));
        Assert.Contains("continue it toward its whole outcome", first);
        Assert.Contains("<objective>\nThe epic\nAcceptance: C: and D: switch at runtime", first);
        Assert.Contains("Completion audit before you hand the epic in", first);
        Assert.Contains("Blocked audit", first);

        // It acted (a tool call), so the next idle wakes it again.
        await steering.ShouldStopAfterTurnAsync(Turn(context, Assistant("working", toolCall: true)));
        await steering.ShouldStopAfterTurnAsync(Turn(context, Assistant("done for now")));
        Assert.Contains("continue it toward its whole outcome", MessagesText(await DriveAsync(clock, steering.GetFollowUpMessagesAsync())));

        // This continuation called no tool: no progress, no further wake until something new arrives.
        await steering.ShouldStopAfterTurnAsync(Turn(context, Assistant("still blocked on the same thing")));
        Assert.Empty(await DriveAsync(clock, steering.GetFollowUpMessagesAsync()));
    }

    // digital-scribe, 2026-10-28: "agents going quiet and holding work... how do we prevent and address that?" One continuation
    // without a tool call used to stop the self-wake for good; now it comes back after 30, then 60 minutes, and after three in
    // a row the seat says it is stuck until something arrives.
    [Fact]
    public async Task ASelfWakeWithoutProgressIsRetriedTwice_ThenTheSeatSaysItIsStuck_UntilAMessageArrives()
    {
        var chat = new LatchedChat { Held = [("hc2000-hdd", "in_progress")] };
        using var handler = chat.Handler();
        using var client = Client(handler);
        var clock = new FakeClock();
        var steering = IdleSteering(client, clock);
        var context = Context([]);

        Assert.Contains("continue it toward its whole outcome", MessagesText(await DriveAsync(clock, steering.GetFollowUpMessagesAsync())));
        await steering.ShouldStopAfterTurnAsync(Turn(context, Assistant("I will look at it later")));
        Assert.Empty(await DriveAsync(clock, steering.GetFollowUpMessagesAsync()));

        clock.Advance(FactorySteering.StalledWakeBackoff);
        var second = MessagesText(await DriveAsync(clock, steering.GetFollowUpMessagesAsync()));
        Assert.Contains("you made no progress (no tool call). This is wake 2 of 3", second);
        await steering.ShouldStopAfterTurnAsync(Turn(context, Assistant("still thinking")));
        Assert.Empty(await DriveAsync(clock, steering.GetFollowUpMessagesAsync()));
        clock.Advance(FactorySteering.StalledWakeBackoff);
        Assert.Empty(await DriveAsync(clock, steering.GetFollowUpMessagesAsync())); // the second wait is twice as long

        clock.Advance(FactorySteering.StalledWakeBackoff);
        Assert.Contains("This is wake 3 of 3", MessagesText(await DriveAsync(clock, steering.GetFollowUpMessagesAsync())));
        await steering.ShouldStopAfterTurnAsync(Turn(context, Assistant("nothing to do")));
        Assert.Empty(await DriveAsync(clock, steering.GetFollowUpMessagesAsync()));
        Assert.StartsWith("stuck — hc2000-hdd: 3 wakes in a row made no progress", steering.CurrentStatus);
        clock.Advance(TimeSpan.FromHours(4));
        Assert.Empty(await DriveAsync(clock, steering.GetFollowUpMessagesAsync()));

        // A message is news: the seat works again, and its next idle wakes it for the epic as before.
        chat.Deliver(Item(40, body: "the architect answered"));
        Assert.NotEmpty(await steering.GetSteeringMessagesAsync());
        Assert.False(steering.CurrentStatus?.StartsWith("stuck", StringComparison.Ordinal));
        await steering.ShouldStopAfterTurnAsync(Turn(context, Assistant("thanks")));
        Assert.Contains("continue it toward its whole outcome", MessagesText(await DriveAsync(clock, steering.GetFollowUpMessagesAsync())));
    }

    [Fact]
    public async Task NoWakeForWorkWaitingOnATestRun_ForAnOpenTask_OrOutsideAssignmentMode()
    {
        var chat = new LatchedChat { Held = [("hc2000-hdd", "in_progress")] };
        chat.WaitingOnRun.Add("hc2000-hdd");
        using var handler = chat.Handler();
        using var client = Client(handler);
        var clock = new FakeClock();
        Assert.Empty(await DriveAsync(clock, IdleSteering(client, clock).GetFollowUpMessagesAsync()));

        chat.WaitingOnRun.Clear();
        var open = IdleSteering(client, clock);
        chat.Deliver(Item(30, body: "task still open"));
        Assert.NotEmpty(await open.GetSteeringMessagesAsync());
        Assert.DoesNotContain("[harness]", MessagesText(await DriveAsync(clock, open.GetFollowUpMessagesAsync())));

        var lead = new FactorySteering(client, clock) { Actor = "team-lead-2", FollowUpWaitSeconds = 30, LatchPollInterval = TimeSpan.FromSeconds(5), IdleTimeout = TimeSpan.FromSeconds(10) };
        Assert.DoesNotContain("[harness]", MessagesText(await DriveAsync(clock, lead.GetFollowUpMessagesAsync())));
    }

    [Fact]
    public async Task AStartWithNothingHeldKeepsThePromptAsItIs()
    {
        var chat = new LatchedChat { Held = [] };
        using var handler = chat.Handler();
        using var client = Client(handler);
        Assert.Equal("Service start.", await EpicSteering(client).WithEpicMemoryAsync("Service start."));
    }

    [Fact]
    public async Task ANewEpicStartsAFreshContextOnceThePreviousTaskIsDelivered_WithTheNewEpicsMemory()
    {
        var chat = new LatchedChat { Held = [("hc2000-hdd", "in_progress")] };
        using var handler = chat.Handler();
        using var client = Client(handler);
        var steering = EpicSteering(client);
        var tools = new List<IAgentTool>();
        var context = Context(tools);

        chat.Deliver(Item(10, body: "task a"));
        Assert.Contains("None yet", MessagesText(await steering.GetSteeringMessagesAsync()));
        Assert.Null(await steering.PrepareNextTurnAsync(Turn(context, Assistant("working"))));
        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(context, Assistant("task a delivered"))));

        chat.Held = [("hc2000-video", "in_progress")];
        chat.Memory["hc2000-video"] = "Goal: VGA modes.";
        chat.Deliver(Item(11, author: "team-lead-2", body: "@coder-06-pi5 hc2000-video is yours"));
        Assert.Single(await steering.GetSteeringMessagesAsync());
        var update = await steering.PrepareNextTurnAsync(Turn(context, Assistant("task a delivered")));

        Assert.NotNull(update?.Context);
        Assert.Empty(update.Context.Messages);
        Assert.Same(tools, update.Context.Tools);
        Assert.Contains("You hold epic hc2000-video now (before: hc2000-hdd)", TrailingText(update));
        Assert.Contains("Goal: VGA modes.", TrailingText(update));
        Assert.Equal("hc2000-video", steering.BoundEpic);
    }

    [Fact]
    public async Task ANewEpicUnderAnOpenTaskKeepsTheContext_AndSwitchesAtTheNextArrivalAfterDelivery()
    {
        var chat = new LatchedChat { Held = [("hc2000-hdd", "in_progress")] };
        using var handler = chat.Handler();
        using var client = Client(handler);
        var steering = EpicSteering(client);
        var context = Context([]);

        chat.Deliver(Item(10, body: "task a"));
        await steering.GetSteeringMessagesAsync();
        Assert.Null(await steering.PrepareNextTurnAsync(Turn(context, Assistant("working"))));

        chat.Held = [("hc2000-video", "in_progress")];
        chat.Deliver(Item(11, body: "a note while task a is open"));
        await steering.GetSteeringMessagesAsync();
        Assert.Null(await steering.PrepareNextTurnAsync(Turn(context, Assistant("still on task a"))));
        Assert.Equal("hc2000-hdd", steering.BoundEpic);

        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(context, Assistant("task a delivered"))));
        chat.Deliver(Item(12, body: "next"));
        await steering.GetSteeringMessagesAsync();
        Assert.Contains("You hold epic hc2000-video now", TrailingText(await steering.PrepareNextTurnAsync(Turn(context, Assistant("task a delivered")))));
    }

    [Fact]
    public async Task HoldingNothingTheSameEpicOrASilentKbKeepsTheContext()
    {
        var chat = new LatchedChat { Held = [("hc2000-hdd", "in_progress")] };
        using var handler = chat.Handler();
        using var client = Client(handler);
        var steering = EpicSteering(client);
        var context = Context([]);

        chat.Deliver(Item(10, body: "task a"));
        await steering.GetSteeringMessagesAsync();
        Assert.Null(await steering.PrepareNextTurnAsync(Turn(context, Assistant("working"))));
        foreach (var (held, id) in new[] { (new List<(string, string)>(), 11L), ([("hc2000-hdd", "in_progress")], 12L), (null!, 13L) })
        {
            Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(context, Assistant("delivered"))));
            chat.Held = held;
            chat.Deliver(Item(id, body: "next"));
            await steering.GetSteeringMessagesAsync();
            Assert.Null(await steering.PrepareNextTurnAsync(Turn(context, Assistant("delivered"))));
            Assert.Equal("hc2000-hdd", steering.BoundEpic);
        }
    }

    // digital-scribe, 2026-10-28: "we need to be able to have also parked agents when no work and not churn tokens for nothing".
    private static AgentContext LargeContext(List<IAgentTool> tools) => new()
    {
        SystemPrompt = "you are attached", Tools = tools,
        Messages = [new UserMessage(new string('x', (FactorySteering.ParkMinTokens + 1000) * TokenEstimate.CharsPerToken))],
    };

    [Fact]
    public async Task AParkedSeatStartsItsNextWakeFromAFreshContext_AndItsNextEpicFromTheMemory()
    {
        var chat = new LatchedChat { Held = [("hc2000-hdd", "in_progress")] };
        using var handler = chat.Handler();
        using var client = Client(handler);
        var steering = EpicSteering(client);
        var tools = new List<IAgentTool>();
        var context = LargeContext(tools);

        chat.Deliver(Item(10, body: "task a"));
        await steering.GetSteeringMessagesAsync();
        Assert.Null(await steering.PrepareNextTurnAsync(Turn(context, Assistant("working"))));
        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(context, Assistant("handed in"))));

        // The epic left the seat (review, a demo seat): the next wake, whatever it is, starts from the role text.
        chat.Held = [];
        chat.Deliver(Item(11, body: "FYI: the layout amendment is published"));
        Assert.Single(await steering.GetSteeringMessagesAsync());
        var update = await steering.PrepareNextTurnAsync(Turn(context, Assistant("handed in")));
        Assert.NotNull(update?.Context);
        Assert.Empty(update.Context.Messages);
        Assert.Same(tools, update.Context.Tools);
        Assert.Contains("You hold no work item now (your last epic: hc2000-hdd)", TrailingText(update));
        Assert.Null(steering.BoundEpic);

        // Work placed on it opens with that epic's memory.
        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(update.Context, Assistant("noted"))));
        chat.Held = [("hc2000-video", "ready")];
        chat.Memory["hc2000-video"] = "Goal: VGA modes.";
        chat.Deliver(Item(12, author: "team-lead-2", body: "ASSIGNED hc2000-video"));
        Assert.Contains("Goal: VGA modes.", MessagesText(await steering.GetSteeringMessagesAsync()));
        Assert.Equal("hc2000-video", steering.BoundEpic);
    }

    [Fact]
    public async Task AParkedSeatKeepsItsContextWhileATaskIsOpen()
    {
        var chat = new LatchedChat { Held = [] };
        using var handler = chat.Handler();
        using var client = Client(handler);
        var steering = EpicSteering(client);
        var context = LargeContext([]);

        chat.Deliver(Item(10, body: "task a"));
        await steering.GetSteeringMessagesAsync();
        Assert.NotNull((await steering.PrepareNextTurnAsync(Turn(context, Assistant("before"))))?.Context);

        chat.Deliver(Item(11, body: "more for task a"));
        await steering.GetSteeringMessagesAsync();
        Assert.Null(await steering.PrepareNextTurnAsync(Turn(context, Assistant("still on task a", toolCall: true))));
    }

    [Fact]
    public async Task ASeatInAssignmentModeThatHoldsNothingSaysItIsParked()
    {
        var chat = new LatchedChat { Held = [] };
        chat.Deliver(Item(43, thread: 30));
        using var handler = chat.Handler();
        using var client = Client(handler);
        var steering = EpicSteering(client);

        Assert.Single(await steering.GetSteeringMessagesAsync());
        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(Context([]), Assistant("answered"))));
        Assert.Equal("idle (parked: holds no work) — last: message 43 in board", steering.CurrentStatus);
    }

    [Fact]
    public async Task AssignmentModeKeepsTheContextForAPlainArrival()
    {
        using var handler = ByTool(_ => DigestText([Item(10, body: "the architect answers your question")]));
        using var client = Client(handler);
        var steering = AssignmentSteering(client);

        Assert.Single(await steering.GetSteeringMessagesAsync());
        Assert.Null(await steering.PrepareNextTurnAsync(Turn(Context([]), Assistant("waiting on the architect"))));
    }

    [Fact]
    public async Task AssignmentModeClearsForAFreshTaskOnceThePreviousTaskIsDelivered()
    {
        var chat = new LatchedChat();
        using var handler = chat.Handler();
        using var client = Client(handler);
        var steering = AssignmentSteering(client);
        var tools = new List<IAgentTool>();
        var context = Context(tools);

        chat.Deliver(Item(10, body: "task a"));
        Assert.Single(await steering.GetSteeringMessagesAsync());
        Assert.Null(await steering.PrepareNextTurnAsync(Turn(context, Assistant("working"))));
        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(context, Assistant("task a delivered"))));

        chat.Deliver(Item(11, author: "team-lead-2", body: "/fresh-task\n@coder-06-pi5 wi-1 is yours"));
        Assert.Single(await steering.GetSteeringMessagesAsync());
        var update = await steering.PrepareNextTurnAsync(Turn(context, Assistant("task a delivered")));

        Assert.NotNull(update?.Context);
        Assert.Empty(update.Context.Messages);
        Assert.Same(tools, update.Context.Tools);
        Assert.Contains("[fresh context]", TrailingText(update));
        Assert.Contains("message 11", TrailingText(update));
    }

    [Fact]
    public async Task AssignmentModeRefusesAFreshTaskWhileThePreviousTaskIsUnanswered()
    {
        var chat = new LatchedChat();
        using var handler = chat.Handler();
        using var client = Client(handler);
        var lines = new List<string>();
        var steering = new FactorySteering(client)
        {
            FreshContextOnAssignment = true,
            FreshTaskFrom = new HashSet<string>(StringComparer.Ordinal) { "team-lead-2" },
            Diagnostics = lines.Add,
        };
        var context = Context([]);

        chat.Deliver(Item(10, body: "task a"));
        Assert.Single(await steering.GetSteeringMessagesAsync());
        Assert.Null(await steering.PrepareNextTurnAsync(Turn(context, Assistant("thinking"))));

        chat.Deliver(Item(11, author: "team-lead-2", body: "/fresh-task\nwi-2"));
        Assert.Single(await steering.GetSteeringMessagesAsync());
        var update = await steering.PrepareNextTurnAsync(Turn(context, Assistant("still on task a")));

        Assert.NotNull(update);
        Assert.Null(update.Context);
        Assert.Contains("[fresh-task refused]", TrailingText(update));
        Assert.Contains("message 10 has not been answered", TrailingText(update));
        Assert.Contains(lines, l => l.Contains("fresh context refused for message 11", StringComparison.Ordinal));
    }

    /// <summary>demo-01-pi1, 2026-10-19: a /fresh-task refused inside a running task was acked with that task's
    /// parked reply, and the assignment never started. It is held instead, and starts its own task, with a fresh
    /// context, once the running one is delivered.</summary>
    [Fact]
    public async Task ARefusedFreshTaskIsHeldAndStartsItsOwnTaskOnceTheRunningOneIsDelivered()
    {
        var chat = new LatchedChat();
        using var handler = chat.Handler();
        using var client = Client(handler);
        var lines = new List<string>();
        var steering = new FactorySteering(client)
        {
            FreshContextOnAssignment = true,
            FreshTaskFrom = new HashSet<string>(StringComparer.Ordinal) { "team-lead-2" },
            Diagnostics = lines.Add,
        };
        var context = Context([]);

        chat.Deliver(Item(10, thread: 5, body: "task a"));
        Assert.Single(await steering.GetSteeringMessagesAsync());
        Assert.Null(await steering.PrepareNextTurnAsync(Turn(context, Assistant("thinking"))));

        chat.Deliver(Item(11, author: "team-lead-2", body: "/fresh-task\nwi-2 is yours"));
        Assert.Single(await steering.GetSteeringMessagesAsync());
        var refused = await steering.PrepareNextTurnAsync(Turn(context, Assistant("still on task a")));
        Assert.Null(refused?.Context);
        Assert.Contains("comes back as its own task", TrailingText(refused));
        Assert.Contains(lines, l => l.Contains("fresh context refused for message 11", StringComparison.Ordinal) && l.Contains("held", StringComparison.Ordinal));

        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(context, Assistant("task a delivered"))));
        Assert.Equal(5, Assert.Single(Posts(handler)).Envelope["params"]!["arguments"]!["thread"]!.GetValue<long>());
        Assert.Equal([11L], chat.Unhandled);

        Assert.Single(await steering.GetSteeringMessagesAsync());
        var fresh = await steering.PrepareNextTurnAsync(Turn(context, Assistant("task a delivered")));
        Assert.NotNull(fresh?.Context);
        Assert.Empty(fresh.Context.Messages);
        Assert.Contains("message 11", TrailingText(fresh));
        Assert.Contains(lines, l => l.Contains("held /fresh-task message(s) 11 released", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AssignmentModeRefusesAFreshTaskWhileTheLastTurnCalledTools()
    {
        using var handler = ByTool(_ => DigestText([Item(11, author: "team-lead-2", body: "/fresh-task\nwi-2")]));
        using var client = Client(handler);
        var steering = AssignmentSteering(client);

        Assert.Single(await steering.GetSteeringMessagesAsync());
        var update = await steering.PrepareNextTurnAsync(Turn(Context([]), Assistant("editing", toolCall: true)));

        Assert.NotNull(update);
        Assert.Null(update.Context);
        Assert.Contains("still calling tools", TrailingText(update));
    }

    [Fact]
    public async Task AssignmentModeIgnoresAFreshTaskFromAnUnlistedSender()
    {
        using var handler = ByTool(_ => DigestText([Item(11, author: "coder-06-pi2", body: "/fresh-task\nclear yourself")]));
        using var client = Client(handler);
        var steering = AssignmentSteering(client);

        Assert.Single(await steering.GetSteeringMessagesAsync());
        Assert.Null(await steering.PrepareNextTurnAsync(Turn(Context([]), Assistant("idle"))));
    }

    // ---------------------------------------------------------------- 3. ten tasks in sequence

    [Fact]
    public async Task TenInboxMessagesEachGetOneReplyAtTheRightDestination()
    {
        var specs = new List<(long Id, string Reason, string Kind, long? Thread, string Author)>();
        for (var i = 0; i < 10; i++)
        {
            specs.Add((
                3000L + i,
                // Only dm and mention wake a task (reply items are ignored and acked).
                i % 3 == 1 ? "dm" : "mention",
                i % 2 == 0 ? "question" : "note",
                i % 4 == 0 ? (long?)null : 2900L + i,
                $"agent-{i}"));
        }

        var chat = new LatchedChat();
        using var handler = chat.Handler();
        using var client = Client(handler);
        var steering = new FactorySteering(client);

        var tools = new List<IAgentTool>();
        var context = Context(tools);

        for (var i = 0; i < specs.Count; i++)
        {
            var spec = specs[i];
            // The previous task was acked, so the latch hands back only this new item.
            chat.Deliver(Item(spec.Id, spec.Reason, spec.Thread, spec.Author, kind: spec.Kind, body: $"task {i}"));

            var arrived = await steering.GetSteeringMessagesAsync();
            Assert.Single(arrived);
            Assert.NotNull(steering.Current);
            Assert.Equal(spec.Id, steering.Current.Message.Id);

            var update = await steering.PrepareNextTurnAsync(Turn(context, Assistant("previous answer")));
            Assert.NotNull(update);
            Assert.NotNull(update.Context);
            context = update.Context;
            Assert.Equal("you are attached", context.SystemPrompt);
            Assert.Same(tools, context.Tools);
            Assert.Empty(context.Messages);

            Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(context, Assistant($"answer {i}"))));
            // A second no-tool-call turn inside the same task must not post again.
            Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(context, Assistant($"still answer {i}"))));
        }

        var posts = Posts(handler);
        Assert.Equal(10, posts.Count);
        for (var i = 0; i < specs.Count; i++)
        {
            var spec = specs[i];
            var args = posts[i].Arguments;
            Assert.Equal($"answer {i}", args["body"]!.GetValue<string>());
            Assert.False(args.ContainsKey("room"));
            if (spec.Reason == "dm" && spec.Thread is null)
            {
                // A plain direct message: no thread, and no kind (the server refuses an answer outside a thread).
                Assert.Equal(spec.Author, args["to"]!.GetValue<string>());
                Assert.False(args.ContainsKey("thread"));
                Assert.False(args.ContainsKey("kind"));
                continue;
            }

            Assert.Equal(spec.Thread ?? spec.Id, args["thread"]!.GetValue<long>());
            Assert.False(args.ContainsKey("to"));
            if (spec.Kind == "question") Assert.Equal("answer", args["kind"]!.GetValue<string>());
            else Assert.False(args.ContainsKey("kind"));
        }

        // Every task was acked after its post, so nothing is left latched.
        Assert.Equal(10, Acks(handler).Count);
        Assert.Empty(chat.Unhandled);
        Assert.Equal(10, steering.AckedCount);
    }

    [Fact]
    public async Task EmptyAssistantTextIsNotPosted()
    {
        var chat = new LatchedChat();
        chat.Deliver(Item(40, thread: 30));
        using var handler = chat.Handler();
        using var client = Client(handler);
        var steering = new FactorySteering(client);

        Assert.Single(await steering.GetSteeringMessagesAsync());
        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(Context([]), Assistant("   "))));
        Assert.Empty(Posts(handler));
    }

    [Fact]
    public async Task EmptyAssistantTextIsAcknowledgedWithoutAPostAndReported()
    {
        var lines = new List<string>();
        var chat = new LatchedChat();
        chat.Deliver(Item(43, thread: 30));
        using var handler = chat.Handler();
        using var client = Client(handler);
        var steering = new FactorySteering(client) { Diagnostics = lines.Add };

        Assert.Single(await steering.GetSteeringMessagesAsync());
        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(Context([]), Assistant("   "))));

        Assert.Empty(Posts(handler));
        Assert.Equal([43L], AckIds(Assert.Single(Acks(handler))));
        Assert.Empty(chat.Unhandled);
        Assert.Equal(["factory: task for message 43 ended with no reply text; acknowledged without a post"], lines);
        var calls = StatusCalls(handler);
        Assert.Equal(2, calls.Count);
        Assert.Equal("working — message 43 in board", calls[0].Arguments["status"]!.GetValue<string>());
        var idle = calls[1];
        Assert.Equal("status", idle.Arguments["op"]!.GetValue<string>());
        Assert.Equal("idle — last: message 43 in board", idle.Arguments["status"]!.GetValue<string>());
        Assert.Equal("idle — last: message 43 in board", steering.CurrentStatus);
    }

    [Fact]
    public async Task EmptyFinalReplyMarkerIsAcknowledgedWithoutAPost()
    {
        var chat = new LatchedChat();
        chat.Deliver(Item(106512, thread: 106511, room: "board"));
        using var handler = chat.Handler();
        using var client = Client(handler);
        var steering = new FactorySteering(client);

        Assert.Single(await steering.GetSteeringMessagesAsync());
        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(Context([]),
            Assistant("No action needed from me.\n\n[empty final reply]"))));

        // The marker is stripped; the leftover "no action needed from me" is availability-only, so nothing posts.
        Assert.Empty(Posts(handler));
        Assert.Equal([106512L], AckIds(Assert.Single(Acks(handler))));
        Assert.Empty(chat.Unhandled);
        Assert.Equal("idle — last: message 106512 in board", steering.CurrentStatus);
    }

    [Fact]
    public async Task AvailabilityOnlyFinalTextIsNotPostedAndStatusIsSet()
    {
        var chat = new LatchedChat();
        chat.Deliver(Item(50, thread: 49, room: "board"));
        using var handler = chat.Handler();
        using var client = Client(handler);
        var steering = new FactorySteering(client);

        Assert.Single(await steering.GetSteeringMessagesAsync());
        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(Context([]), Assistant("I am free"))));

        Assert.Empty(Posts(handler));
        Assert.Equal([50L], AckIds(Assert.Single(Acks(handler))));
        Assert.Equal("idle — last: message 50 in board", StatusCalls(handler)[^1].Arguments["status"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("NO_REPLY")]
    [InlineData("`NO_REPLY`")]
    [InlineData("The PACE line is informational; my item is unchanged.\n\nNO_REPLY")]
    [InlineData("No pending work for me — nothing requires a reply.")]
    [InlineData("saved")]
    [InlineData("Noted.")]
    public async Task NoReplyAndRitualLinesAreAcknowledgedWithoutAPost(string text)
    {
        var chat = new LatchedChat();
        chat.Deliver(Item(166760, thread: 163343, room: "board"));
        using var handler = chat.Handler();
        using var client = Client(handler);
        var steering = new FactorySteering(client);

        Assert.Single(await steering.GetSteeringMessagesAsync());
        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(Context([]), Assistant(text))));

        Assert.Empty(Posts(handler));
        Assert.Equal([166760L], AckIds(Assert.Single(Acks(handler))));
        Assert.Equal("idle — last: message 166760 in board", steering.CurrentStatus);
    }

    [Theory]
    [InlineData("done")]
    [InlineData("No pending work for me except wi-12, which is blocked on @architect-2's schema answer since 08:00Z and needs it today.")]
    public async Task ShortAnswersAndLongLinesStillPost(string text)
    {
        var chat = new LatchedChat();
        chat.Deliver(Item(300, thread: 299, room: "board"));
        using var handler = chat.Handler();
        using var client = Client(handler);
        var steering = new FactorySteering(client);

        Assert.Single(await steering.GetSteeringMessagesAsync());
        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(Context([]), Assistant(text))));

        Assert.Single(Posts(handler));
    }

    [Fact]
    public async Task SessionStartWithNoInboxItemPostsNothingAndDoesNotSetStatus()
    {
        var chat = new LatchedChat();
        using var handler = chat.Handler();
        using var client = Client(handler);
        var steering = new FactorySteering(client);

        Assert.Empty(await steering.GetSteeringMessagesAsync());
        Assert.Null(steering.Current);
        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(Context([]), Assistant("I am free"))));

        Assert.Empty(Posts(handler));
        Assert.Empty(StatusCalls(handler));
        Assert.Null(steering.CurrentStatus);
    }

    [Fact]
    public async Task RealFinalTextStillPostsThenSetsIdleStatus()
    {
        var chat = new LatchedChat();
        chat.Deliver(Item(200, thread: 199, room: "pi-sharp"));
        using var handler = chat.Handler();
        using var client = Client(handler);
        var steering = new FactorySteering(client);

        Assert.Single(await steering.GetSteeringMessagesAsync());
        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(Context([]), Assistant("the probe is green"))));

        Assert.Equal("the probe is green", Assert.Single(Posts(handler)).Arguments["body"]!.GetValue<string>());
        Assert.Equal("idle — last: message 200 in pi-sharp", steering.CurrentStatus);
    }

    [Fact]
    public async Task HeldWorkItemKeepsTheLineWorkingAtTaskEnd()
    {
        var chat = new LatchedChat { Held = [("pi-sharp-hh-9", "ready"), ("pi-sharp-hh-8", "in_progress")] };
        chat.Deliver(Item(210, thread: 209, room: "board"));
        using var handler = chat.Handler();
        using var client = Client(handler);
        var steering = new FactorySteering(client) { Actor = "coder-06-pi1" };

        Assert.Single(await steering.GetSteeringMessagesAsync());
        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(Context([]), Assistant("   "))));

        var list = Assert.Single(Snapshot(handler), r => r.Tool == "kb_list");
        Assert.Equal("coder-06-pi1", list.Arguments["assigned_to"]!.GetValue<string>());
        Assert.Equal("ready,in_progress", list.Arguments["status"]!.GetValue<string>());
        Assert.True(list.Arguments["occupying"]!.GetValue<bool>());
        Assert.Equal("working — pi-sharp-hh-8 (in_progress) +1 more", steering.CurrentStatus);
    }

    [Fact]
    public async Task NoHeldWorkItemTurnsTheLineIdleAndAWorkingLineIsRecheckedWhileWaiting()
    {
        var chat = new LatchedChat { Held = [("pi-sharp-hh-8", "in_progress")] };
        chat.Deliver(Item(211, thread: 209, room: "board"));
        using var handler = chat.Handler();
        using var client = Client(handler);
        var steering = new FactorySteering(client)
        {
            Actor = "coder-06-pi1", StatusRecheckInterval = TimeSpan.Zero, LatchPollInterval = TimeSpan.FromMilliseconds(10),
            IdleTimeout = TimeSpan.FromMilliseconds(200),
        };

        Assert.Single(await steering.GetSteeringMessagesAsync());
        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(Context([]), Assistant("   "))));
        Assert.Equal("working — pi-sharp-hh-8 (in_progress)", steering.CurrentStatus);

        // The item went to review: the next re-check while the seat waits turns the line idle.
        chat.Held = [];
        Assert.Empty(await steering.GetFollowUpMessagesAsync());
        Assert.Equal("idle — last: message 211 in board", steering.CurrentStatus);
    }

    [Fact]
    public async Task UnansweredKbLeavesTheLineAlone()
    {
        var lines = new List<string>();
        var chat = new LatchedChat();
        chat.Deliver(Item(212, thread: 209, room: "board"));
        using var handler = chat.Handler();
        using var client = Client(handler);
        var steering = new FactorySteering(client) { Actor = "coder-06-pi1", Diagnostics = lines.Add };

        Assert.Single(await steering.GetSteeringMessagesAsync());
        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(Context([]), Assistant("   "))));

        Assert.Equal("working — message 212 in board", steering.CurrentStatus);
        Assert.Contains("factory: held work items unreadable (KB did not answer); status left unchanged", lines);
    }

    [Fact]
    public async Task RunningJobKeepsTheLineWorking()
    {
        using var tmp = new PiSharp.Tests.Tools.TempWorkspace();
        using var store = new PiSharp.Core.Notebook.NotebookStore(tmp.Path, "seat1");
        store.Put("job-0123", "attempt", "Running command:\ndotnet test", "", "open", 0);
        var chat = new LatchedChat();
        chat.Deliver(Item(213, thread: 209, room: "board"));
        using var handler = chat.Handler();
        using var client = Client(handler);
        var steering = new FactorySteering(client) { Notebook = store };

        Assert.Single(await steering.GetSteeringMessagesAsync());
        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(Context([]), Assistant("   "))));

        Assert.Equal("working — 1 job running", steering.CurrentStatus);
    }

    [Fact]
    public async Task ParkedReplyDoesNotSetIdleStatus()
    {
        var chat = new LatchedChat { FailPosts = true };
        chat.Deliver(Item(201, thread: 199));
        using var handler = chat.Handler();
        using var client = Client(handler);
        var steering = new FactorySteering(client);

        Assert.Single(await steering.GetSteeringMessagesAsync());
        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(Context([]), Assistant("answered"))));

        // The task started, so the line says working; the parked reply keeps it from turning idle.
        Assert.Equal(["working — message 201 in board"], StatusCalls(handler).Select(c => c.Arguments["status"]!.GetValue<string>()));
        Assert.Equal("working — message 201 in board", steering.CurrentStatus);
    }

    [Fact]
    public async Task PostRepliesFalseNeverPosts()
    {
        var chat = new LatchedChat();
        chat.Deliver(Item(41, thread: 30), Item(44, reason: "reply", thread: 30));
        using var handler = chat.Handler();
        using var client = Client(handler);
        var steering = new FactorySteering(client) { PostReplies = false };

        Assert.Equal(2, (await steering.GetSteeringMessagesAsync()).Count);
        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(Context([]), Assistant("an answer nobody sees"))));
        Assert.Empty(Posts(handler));
        // Nothing is written to chat at all: an unanswered item must stay in the inbox for a process that answers.
        Assert.Empty(Acks(handler));
        Assert.Equal([41L, 44L], chat.Unhandled);
    }

    [Fact]
    public async Task ReplyIsNotPostedWhileToolCallsContinue()
    {
        var chat = new LatchedChat();
        chat.Deliver(Item(42, thread: 30));
        using var handler = chat.Handler();
        using var client = Client(handler);
        var steering = new FactorySteering(client);

        Assert.Single(await steering.GetSteeringMessagesAsync());
        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(Context([]), Assistant("working", toolCall: true))));
        Assert.Empty(Posts(handler));

        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(Context([]), Assistant("finished"))));
        Assert.Single(Posts(handler));
    }

    [Fact]
    public async Task ARunEndedOnAToolBatchClosesTheTaskAndSaysWhy()
    {
        // Team watch 2026-10-03: the loop guard ended pi4's run on a tool batch; the task stayed open, no reply, no ack.
        var chat = new LatchedChat();
        chat.Deliver(Item(42, thread: 30));
        using var handler = chat.Handler();
        using var client = Client(handler);
        var steering = new FactorySteering(client);

        Assert.Single(await steering.GetSteeringMessagesAsync());
        var refused = new ToolResultMessage
        {
            ToolCallId = "call-1",
            ToolName = "kb_list",
            Content = [new TextContent("Tool call \"kb_list\" was not executed: the same tool call was repeated 3 times in a row.")],
            IsError = true,
        };
        var final = new TurnContext(Assistant("checking again", toolCall: true), [refused], Context([]), [], Final: true);
        Assert.False(await steering.ShouldStopAfterTurnAsync(final));

        var post = Assert.Single(Posts(handler));
        Assert.Equal(30, post.Arguments["thread"]!.GetValue<long>());
        var body = post.Arguments["body"]!.GetValue<string>();
        Assert.StartsWith("My run stopped before I answered: Tool call \"kb_list\" was not executed", body);
        Assert.DoesNotContain("checking again", body);
        Assert.Contains(42L, Acks(handler).SelectMany(AckIds));
    }

    // ---------------------------------------------------------------- 4. mid-task arrival

    [Fact]
    public async Task MidTaskArrivalKeepsMemoryAndCurrentItem()
    {
        var chat = new LatchedChat();
        chat.Deliver(Item(50, thread: 49, body: "first"));
        using var handler = chat.Handler();
        using var client = Client(handler);
        var steering = new FactorySteering(client);

        Assert.Single(await steering.GetSteeringMessagesAsync());
        Assert.Equal(50, steering.Current!.Message.Id);

        // A second item lands while the first task is still calling tools.
        chat.Deliver(Item(51, thread: 49, body: "and this too"));
        Assert.Single(await steering.GetSteeringMessagesAsync());
        Assert.Equal(50, steering.Current!.Message.Id);

        var context = Context([]);
        Assert.Null(await steering.PrepareNextTurnAsync(Turn(context, Assistant("thinking", toolCall: true))));
        Assert.Equal(50, steering.Current!.Message.Id);

        // The task ends once: one reply, addressed to the item that started it.
        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(context, Assistant("both done"))));
        var post = Assert.Single(Posts(handler));
        Assert.Equal(49, post.Arguments["thread"]!.GetValue<long>());
        Assert.Equal("both done", post.Arguments["body"]!.GetValue<string>());
    }

    // ---------------------------------------------------------------- backlog replay: one task boundary

    [Fact]
    public async Task BacklogReplayOfFiveItemsIsOneArrivalOneResetAndOneReply()
    {
        var chat = new LatchedChat();
        for (var i = 0; i < 5; i++) chat.Deliver(Item(4000L + i, thread: 3999, body: $"backlog {i}"));
        using var handler = chat.Handler();
        using var client = Client(handler);
        var steering = new FactorySteering(client);

        var arrived = await steering.GetSteeringMessagesAsync();
        Assert.Equal(5, arrived.Count);
        for (var i = 0; i < 5; i++)
            Assert.Equal(
                $"[inbox from @coder-06-pi2 in board, message {4000 + i}, thread 3999, mention] backlog {i}",
                Text(arrived[i]));
        Assert.Equal(5, steering.Seen.Count);
        Assert.Equal(4000, steering.Current!.Message.Id);

        var tools = new List<IAgentTool>();
        var context = Context(tools);
        var update = await steering.PrepareNextTurnAsync(Turn(context, Assistant("previous")));
        Assert.NotNull(update);
        Assert.NotNull(update.Context);
        Assert.Same(tools, update.Context.Tools);
        Assert.Equal("you are attached", update.Context.SystemPrompt);
        Assert.Empty(update.Context.Messages);
        // Exactly one boundary for the whole batch.
        Assert.Null(await steering.PrepareNextTurnAsync(Turn(context, Assistant("previous"))));

        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(update.Context, Assistant("handled all five"))));
        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(update.Context, Assistant("still here"))));
        var post = Assert.Single(Posts(handler));
        Assert.Equal(3999, post.Arguments["thread"]!.GetValue<long>());
        Assert.Equal("handled all five", post.Arguments["body"]!.GetValue<string>());
    }

    // ---------------------------------------------------------------- 5. fanout: one digest, two targets (acceptance test)

    /// <summary>Two mentions in two different threads arrive in one digest: two user messages, one task, and the
    /// final text goes to the waking message's thread only. The second thread is a secondary target: no copy of the
    /// text is posted there (live 15494/15495 put the same text in both threads), and its id is acked with the one
    /// post. architect-2's ruling note-20260915-final-text-single-thread.</summary>
    [Fact]
    public async Task TwoMentionsInTwoThreadsInOneDigestGetOnePostToTheWakingThreadAndOneAckOfBoth() =>
        await FanoutAsync(emptySecond: false);

    [Fact]
    public async Task TwoMentionsInTwoThreadsInOneDigest_WhenTheWakingPostFailsTheSecondaryIsStillAckedAndOnlyTheWakingReplyParks() =>
        await FanoutAsync(emptySecond: false, failWakingUntilRetry: true);

    [Fact]
    public async Task TwoMentionsInTwoThreadsInOneDigestWithEmptyTextAckBothWithoutAPost() =>
        await FanoutAsync(emptySecond: true);

    private static async Task FanoutAsync(bool emptySecond, bool failWakingUntilRetry = false)
    {
        var chat = new LatchedChat();
        chat.Deliver(
            Item(2345, reason: "mention", thread: 2340, author: "architect-1", room: "architecture", body: "ship it"),
            Item(2456, reason: "mention", thread: 2450, author: "product-owner-2", room: "roadmap", body: "status?"));
        using var handler = chat.Handler();
        using var client = Client(handler);
        var lines = new List<string>();
        var clock = new FakeClock();
        var steering = new FactorySteering(client, clock) { Diagnostics = lines.Add };

        // One digest, two unseen mentions: two user messages, one task, woken by the first.
        var arrived = await steering.GetSteeringMessagesAsync();

        Assert.Equal(2, arrived.Count);
        Assert.Contains("[inbox from @architect-1 in architecture, message 2345, thread 2340, mention] ship it", arrived.Select(Text));
        Assert.Contains("[inbox from @product-owner-2 in roadmap, message 2456, thread 2450, mention] status?", arrived.Select(Text));
        Assert.Equal(2345, steering.Current!.Message.Id);

        // The task is open, so nothing has been posted or acked yet.
        Assert.Empty(Posts(handler));
        Assert.Empty(Acks(handler));

        if (failWakingUntilRetry) chat.FailThreads = [2340L];

        var text = emptySecond ? "   " : "one reply answers both";
        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(Context([]), Assistant(text))));

        if (emptySecond)
        {
            // The empty-reply branch is unchanged: both targets are acked without any post.
            Assert.Empty(Posts(handler));
            Assert.Equal([2345L, 2456L], AckIds(Assert.Single(Acks(handler))));
            Assert.Empty(chat.Unhandled);
            return;
        }

        var acks = Acks(handler);
        if (failWakingUntilRetry)
        {
            // The waking thread's post fails and parks; the secondary target is acked at once, never posted to.
            Assert.Equal(2340, Assert.Single(Posts(handler)).Arguments["thread"]!.GetValue<long>());
            Assert.Equal([2456L], AckIds(Assert.Single(acks)));
            Assert.Equal([2345L], chat.Unhandled);
            Assert.Equal(
                [
                    "factory: reply to message 2345 was not posted; it stays in the inbox",
                    "factory: final-text not posted to thread 2340, 1 secondary target(s) skipped (task for message 2345, acknowledged 2456)",
                ],
                lines);
            // The next digest read retries the parked post, to the same thread; when it lands, only 2345 acks.
            chat.FailThreads = null;
            clock.Advance(FactorySteering.InitialBackoff);
            Assert.Empty(await steering.GetSteeringMessagesAsync());
            Assert.Equal(2, (acks = Acks(handler)).Count);
            Assert.Equal([2345L], AckIds(acks[1]));
            Assert.Empty(chat.Unhandled);
        }
        else
        {
            Assert.Equal([2345L, 2456L], AckIds(Assert.Single(acks)));
            Assert.Empty(chat.Unhandled);
            Assert.Equal(
                ["factory: final-text posted to thread 2340, 1 secondary target(s) skipped (task for message 2345, acknowledged 2345, 2456)"],
                lines);
        }

        // Every post went to the waking thread; none to 2450. Neither carries kind=answer (the items are notes).
        // (In the retry case the one reply lands twice on the wire: the failed attempt and the retry that succeeds.)
        var posts = Posts(handler);
        Assert.Equal(failWakingUntilRetry ? 2 : 1, posts.Count);
        Assert.All(posts, p => Assert.Equal(2340, p.Arguments["thread"]!.GetValue<long>()));
        Assert.All(posts, p => Assert.False(p.Arguments.ContainsKey("to")));
        Assert.All(posts, p => Assert.False(p.Arguments.ContainsKey("kind")));
        Assert.All(posts, p => Assert.Equal("one reply answers both", p.Arguments["body"]!.GetValue<string>()));

        // No second answer for either target: both were answered and acked.
        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(Context([]), Assistant("again"))));
        Assert.Equal(failWakingUntilRetry ? 2 : 1, Posts(handler).Count);
        Assert.Empty(chat.Unhandled);
    }

    /// <summary>The live shape: a coordinator seat mentioned in two ops threads in one wake answers with no tool
    /// post. One post, in the waking thread, and the diagnostics line names it and the skipped thread.</summary>
    [Fact]
    public async Task ANoToolFinalTextOfATwoThreadWakeIsPostedOnceInTheWakingThread()
    {
        var chat = new LatchedChat();
        chat.Deliver(
            Item(15441, reason: "mention", thread: 15328, author: "team-lead-2", room: "ops", body: "gate status"),
            Item(15442, reason: "mention", thread: 15059, author: "team-lead-2", room: "ops", body: "observer named"));
        using var handler = chat.Handler();
        using var client = Client(handler);
        var lines = new List<string>();
        var steering = new FactorySteering(client) { Diagnostics = lines.Add };

        Assert.Equal(2, (await steering.GetSteeringMessagesAsync()).Count);
        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(Context([]), Assistant("Standing by as named observer."))));

        var post = Assert.Single(Posts(handler));
        Assert.Equal(15328, post.Arguments["thread"]!.GetValue<long>());
        Assert.Equal([15441L, 15442L], AckIds(Assert.Single(Acks(handler))));
        Assert.Equal(
            ["factory: final-text posted to thread 15328, 1 secondary target(s) skipped (task for message 15441, acknowledged 15441, 15442)"],
            lines);
        Assert.Empty(chat.Unhandled);
    }

    /// <summary>The waking message is a plain DM and a mention in a thread joins the task: the reply goes back to the
    /// DM author only, and the thread is a skipped secondary target.</summary>
    [Fact]
    public async Task APlainDmWakeWithAThreadMentionAnswersTheAuthorOnlyAndAcksBoth()
    {
        var chat = new LatchedChat();
        chat.Deliver(
            Item(240, reason: "dm", thread: null, author: "digital-scribe", room: "dm", body: "status?"),
            Item(2345, reason: "mention", thread: 2340, author: "architect-1", room: "architecture", body: "ship it"));
        using var handler = chat.Handler();
        using var client = Client(handler);
        var lines = new List<string>();
        var steering = new FactorySteering(client) { Diagnostics = lines.Add };

        Assert.Equal(2, (await steering.GetSteeringMessagesAsync()).Count);
        Assert.Equal(240, steering.Current!.Message.Id);
        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(Context([]), Assistant("all green"))));

        var post = Assert.Single(Posts(handler));
        Assert.Equal("digital-scribe", post.Arguments["to"]!.GetValue<string>());
        Assert.False(post.Arguments.ContainsKey("thread"));
        Assert.Equal([240L, 2345L], AckIds(Assert.Single(Acks(handler))));
        Assert.Equal(
            ["factory: final-text posted to @digital-scribe, 1 secondary target(s) skipped (task for message 240, acknowledged 240, 2345)"],
            lines);
        Assert.Empty(chat.Unhandled);
    }

    /// <summary>A single-target task logs the final-text line too, with zero secondary targets, so the no-tool path
    /// is visible in the seat log (probe arm B had only the chat reading).</summary>
    [Fact]
    public async Task ASingleTargetFinalTextLogsZeroSecondaryTargets()
    {
        var chat = new LatchedChat();
        chat.Deliver(Item(2345, reason: "mention", thread: 2340, author: "architect-1", room: "architecture", body: "ship it"));
        using var handler = chat.Handler();
        using var client = Client(handler);
        var lines = new List<string>();
        var steering = new FactorySteering(client) { Diagnostics = lines.Add };

        Assert.Single(await steering.GetSteeringMessagesAsync());
        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(Context([]), Assistant("shipped"))));

        Assert.Equal(2340, Assert.Single(Posts(handler)).Arguments["thread"]!.GetValue<long>());
        Assert.Equal([2345L], AckIds(Assert.Single(Acks(handler))));
        Assert.Equal(
            ["factory: final-text posted to thread 2340, 0 secondary target(s) skipped (task for message 2345, acknowledged 2345)"],
            lines);
    }

    /// <summary>A target whose reply from an earlier task is still parked is not a secondary target of the next task:
    /// the next task's text is not posted there, its ids are not acked by that task, and the parked reply still
    /// lands and acks them on its own.</summary>
    [Fact]
    public async Task AParkedTargetFromAnEarlierTaskIsNeitherCopiedToNorAckedByTheNextTask()
    {
        var chat = new LatchedChat { FailThreads = [2340L] };
        chat.Deliver(Item(2345, reason: "mention", thread: 2340, author: "architect-1", room: "architecture", body: "ship it"));
        using var handler = chat.Handler();
        using var client = Client(handler);
        var lines = new List<string>();
        var clock = new FakeClock();
        var steering = new FactorySteering(client, clock) { Diagnostics = lines.Add };

        Assert.Single(await steering.GetSteeringMessagesAsync());
        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(Context([]), Assistant("first reply"))));
        Assert.Empty(Acks(handler));

        // A new task wakes in another thread while the first reply is parked behind its transient backoff.
        chat.Deliver(Item(2456, reason: "mention", thread: 2450, author: "product-owner-2", room: "roadmap", body: "status?"));
        Assert.Single(await steering.GetSteeringMessagesAsync());
        Assert.Equal(2456, steering.Current!.Message.Id);
        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(Context([]), Assistant("second reply"))));

        Assert.Equal([2456L], AckIds(Assert.Single(Acks(handler))));
        Assert.Equal([2345L], chat.Unhandled);
        Assert.DoesNotContain(Posts(handler), p => p.Arguments["body"]!.GetValue<string>() == "second reply"
            && p.Arguments["thread"]!.GetValue<long>() == 2340);
        Assert.Contains("factory: final-text posted to thread 2450, 0 secondary target(s) skipped (task for message 2456, acknowledged 2456)", lines);

        // The parked reply lands on the next digest read and acks its own id.
        chat.FailThreads = null;
        clock.Advance(FactorySteering.InitialBackoff);
        Assert.Empty(await steering.GetSteeringMessagesAsync());
        var last = Posts(handler)[^1];
        Assert.Equal(2340, last.Arguments["thread"]!.GetValue<long>());
        Assert.Equal("first reply", last.Arguments["body"]!.GetValue<string>());
        Assert.Equal([2345L], AckIds(Acks(handler)[^1]));
        Assert.Empty(chat.Unhandled);
    }

    // ---------------------------------------------------------------- acknowledgement (ADR v23.18)

    [Fact]
    public async Task TheReplyIsPostedBeforeTheAckAndTheItemLeavesTheInbox()
    {
        var chat = new LatchedChat();
        chat.Deliver(Item(200, thread: 199));
        using var handler = chat.Handler();
        using var client = Client(handler);
        var steering = new FactorySteering(client);

        Assert.Single(await steering.GetSteeringMessagesAsync());
        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(Context([]), Assistant("answered"))));

        // Post first: a crash between the two costs one duplicate reply, never a lost one.
        Assert.Equal(["chat_read", "chat_manage", "chat_post", "chat_ack", "chat_manage"], Tools(handler));
        Assert.Equal([200L], AckIds(Assert.Single(Acks(handler))));
        Assert.Empty(chat.Unhandled);
        Assert.Equal(1, steering.AckedCount);
    }

    [Fact]
    public async Task AFailedPostAcksNothingAndSaysTheItemStaysInTheInbox()
    {
        var lines = new List<string>();
        var chat = new LatchedChat { FailPosts = true };
        chat.Deliver(Item(201, thread: 199));
        using var handler = chat.Handler();
        using var client = Client(handler);
        var steering = new FactorySteering(client) { Diagnostics = lines.Add };

        Assert.Single(await steering.GetSteeringMessagesAsync());
        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(Context([]), Assistant("answered"))));

        Assert.Single(Posts(handler));
        Assert.Empty(Acks(handler));
        Assert.Equal([201L], chat.Unhandled);
        Assert.Equal(["factory: reply to message 201 was not posted; it stays in the inbox"], lines);
    }

    [Fact]
    public async Task RefusedReplyLogsExactBodyOnceAndDoesNotReplayAfterLatchRedelivery()
    {
        using var disk = new PiSharp.Tests.Tools.TempWorkspace();
        var lines = new List<string>();
        var body = "`10` is not in this conversation";
        var chat = new LatchedChat { RefusePostsStatus = 403, RefusalBody = body, FailAcks = true };
        chat.Deliver(Item(203, thread: 199));
        using var handler = chat.Handler(); using var client = Client(handler);
        using (var memory = new PiSharp.Core.Notebook.NotebookStore(disk.Path, Self))
        {
            var steering = new FactorySteering(client) { Notebook = memory, Diagnostics = lines.Add };
            Assert.Single(await steering.GetSteeringMessagesAsync());
            Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(Context([]), Assistant("answer"))));
            Assert.Single(Posts(handler));
            Assert.Equal("cancelled", memory.Get("delivery-chat-203")!["status"]!.GetValue<string>());
            Assert.Contains(body, Assert.Single(lines, line => line.StartsWith("factory: chat refused reply", StringComparison.Ordinal)));
            Assert.Equal([203L], chat.Unhandled); // ack failed; the same latched id will be delivered again
        }

        chat.FailAcks = false;
        using var resumedMemory = new PiSharp.Core.Notebook.NotebookStore(disk.Path, Self);
        var resumed = new FactorySteering(client) { Notebook = resumedMemory, Diagnostics = lines.Add };
        Assert.Empty(await resumed.GetSteeringMessagesAsync());
        Assert.Single(Posts(handler)); // cancelled refusal is acked, never retried unchanged
        Assert.Empty(chat.Unhandled);
        Assert.Single(lines, line => line.StartsWith("factory: chat refused reply", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AFailedAckIsReportedOnceAndRetriedBeforeTheNextDigest()
    {
        var lines = new List<string>();
        var chat = new LatchedChat { FailAcks = true };
        chat.Deliver(Item(202, thread: 199));
        using var handler = chat.Handler();
        using var client = Client(handler);
        var steering = new FactorySteering(client) { Diagnostics = lines.Add };

        Assert.Single(await steering.GetSteeringMessagesAsync());
        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(Context([]), Assistant("answered"))));

        Assert.Single(Posts(handler));
        Assert.Single(Acks(handler));
        Assert.Equal([202L], chat.Unhandled);
        Assert.Equal(
            [
                "factory: final-text posted to thread 199, 0 secondary target(s) skipped (task for message 202, acknowledged 202)",
                "factory: acknowledging message(s) 202 failed; will retry",
            ],
            lines);

        // The owed ack is tried once in front of the next digest read.
        chat.FailAcks = false;
        Assert.Empty(await steering.GetSteeringMessagesAsync());
        Assert.Equal(2, Acks(handler).Count);
        Assert.Equal([202L], AckIds(Acks(handler)[1]));
        Assert.Empty(chat.Unhandled);
        // The retry that lands says nothing more.
        Assert.Equal(2, lines.Count);
        Assert.Equal(1, steering.AckedCount);
    }

    [Fact]
    public async Task AnAckIdTheServerDoesNotKnowIsReportedAndNeverRetried()
    {
        var lines = new List<string>();
        var chat = new LatchedChat();
        chat.Deliver(Item(203, thread: 199));
        using var handler = chat.Handler();
        using var client = Client(handler);
        var steering = new FactorySteering(client) { Diagnostics = lines.Add };

        Assert.Single(await steering.GetSteeringMessagesAsync());
        chat.Evict(203);   // the row is not in this actor's inbox: an id from the wrong namespace looks like this
        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(Context([]), Assistant("answered"))));

        Assert.Equal(
            [
                "factory: final-text posted to thread 199, 0 secondary target(s) skipped (task for message 203, acknowledged 203)",
                "factory: ack of message 203 was not in this actor's inbox",
            ],
            lines);
        Assert.Single(Acks(handler));
        Assert.Equal(0, steering.AckedCount);

        // An unknown id is a namespace mistake, not a transient failure: it is never retried.
        Assert.Empty(await steering.GetSteeringMessagesAsync());
        Assert.Single(Acks(handler));
    }

    [Fact]
    public async Task ABacklogOfFiveIsOnePostThenOneAckOfAllFiveIds()
    {
        var chat = new LatchedChat();
        for (var i = 0; i < 5; i++) chat.Deliver(Item(4100L + i, thread: 4099, body: $"backlog {i}"));
        using var handler = chat.Handler();
        using var client = Client(handler);
        var steering = new FactorySteering(client);

        Assert.Equal(5, (await steering.GetSteeringMessagesAsync()).Count);
        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(Context([]), Assistant("all five handled"))));

        Assert.Single(Posts(handler));
        Assert.Equal([4100L, 4101L, 4102L, 4103L, 4104L], AckIds(Assert.Single(Acks(handler))));
        Assert.Empty(chat.Unhandled);
        Assert.Equal(5, steering.AckedCount);
    }

    [Fact]
    public async Task MidTaskArrivalsAreAcknowledgedWithTheTaskThatAbsorbedThem()
    {
        var chat = new LatchedChat();
        chat.Deliver(Item(210, thread: 209, body: "first"));
        using var handler = chat.Handler();
        using var client = Client(handler);
        var steering = new FactorySteering(client);

        Assert.Single(await steering.GetSteeringMessagesAsync());
        chat.Deliver(Item(211, thread: 209, body: "and this too"));
        Assert.Single(await steering.GetSteeringMessagesAsync());
        Assert.Equal(210, steering.Current!.Message.Id);

        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(Context([]), Assistant("both done"))));
        Assert.Single(Posts(handler));
        Assert.Equal([210L, 211L], AckIds(Assert.Single(Acks(handler))));
        Assert.Empty(chat.Unhandled);
    }

    [Fact]
    public async Task AQuietReplyWhileIdleStartsNoTurnIsAckedAndIsReadWithTheNextItemThatWakesTheSeat()
    {
        // 2026-10-13: 467 thread-reply wakes a day, each a model turn that ended "observed, not addressed to you".
        var lines = new List<string>();
        var chat = new LatchedChat();
        chat.Deliver(Item(220, reason: "reply", thread: 219, body: "addressed to someone else"));
        using var handler = chat.Handler();
        using var client = Client(handler);
        var steering = new FactorySteering(client) { Diagnostics = lines.Add };

        Assert.Empty(await steering.GetSteeringMessagesAsync());
        Assert.Null(steering.Current);
        Assert.Contains(220L, steering.Seen);
        Assert.Equal([220L], AckIds(Assert.Single(Acks(handler))));
        Assert.Empty(chat.Unhandled);

        chat.Deliver(Item(221, reason: "mention", thread: 300, body: "your call"));
        var arrived = await steering.GetSteeringMessagesAsync();
        Assert.Equal(2, arrived.Count);
        Assert.StartsWith(
            "[inbox from @coder-06-pi2 in board, message 220, thread 219, reply] addressed to someone else\n(observed: not addressed to you.",
            Text(arrived[0]));
        Assert.StartsWith("[inbox from @coder-06-pi2 in board, message 221, thread 300, mention] your call", Text(arrived[1]));
        Assert.Equal(221, steering.Current!.Message.Id);
    }

    [Fact]
    public async Task AQuestionInAThreadStillWakesTheSeat()
    {
        var chat = new LatchedChat();
        chat.Deliver(Item(222, reason: "reply", thread: 219, kind: "question", body: "who owns this?"));
        using var handler = chat.Handler();
        using var client = Client(handler);
        var steering = new FactorySteering(client);

        Assert.Single(await steering.GetSteeringMessagesAsync());
        Assert.Equal(222, steering.Current!.Message.Id);
    }

    [Fact]
    public async Task AnOwnerItemIsObservedLikeAReply()
    {
        var chat = new LatchedChat();
        chat.Deliver(Item(225, reason: "owner", thread: null, author: "digital-scribe", room: "general", body: "heads up"));
        using var handler = chat.Handler();
        using var client = Client(handler);
        var steering = new FactorySteering(client);

        Assert.Single(await steering.GetSteeringMessagesAsync());
        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(Context([]), Assistant("ok"))));
        Assert.Empty(Posts(handler));
        Assert.Equal([225L], AckIds(Assert.Single(Acks(handler))));
        Assert.Empty(chat.Unhandled);
    }

    [Fact]
    public async Task AnObserveItemIsObservedLikeAnOwnerItem()
    {
        // The role-declared half of the same rule (chat 1.0.6): this seat's role follows @team-lead-2, so an
        // untagged channel message of his arrives with reason "observe". It wakes the model and is acked on
        // delivery; nobody addressed the seat, so nothing is posted back.
        var chat = new LatchedChat();
        chat.Deliver(Item(227, reason: "observe", thread: null, author: "team-lead-2", room: "board", body: "sprint starts monday"));
        using var handler = chat.Handler();
        using var client = Client(handler);
        var steering = new FactorySteering(client);

        var arrived = await steering.GetSteeringMessagesAsync();
        Assert.Contains("observed: not addressed to you", Text(Assert.Single(arrived)));
        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(Context([]), Assistant("ok"))));
        Assert.Empty(Posts(handler));
        Assert.Equal([227L], AckIds(Assert.Single(Acks(handler))));
        Assert.Empty(chat.Unhandled);
    }

    [Fact]
    public async Task AMentionJoiningAnObservedTaskIsTheOneAnswered()
    {
        var chat = new LatchedChat();
        // The owner's untagged post still starts an observed task (a plain thread reply no longer does).
        chat.Deliver(Item(226, reason: "owner", thread: 219, author: "digital-scribe", room: "general"));
        using var handler = chat.Handler();
        using var client = Client(handler);
        var steering = new FactorySteering(client);

        Assert.Single(await steering.GetSteeringMessagesAsync());
        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(Context([]), Assistant("reading", toolCall: true))));
        chat.Deliver(Item(227, reason: "mention", thread: 300, body: "your call"));
        Assert.Single(await steering.GetSteeringMessagesAsync());
        Assert.Equal(227, steering.Current!.Message.Id);

        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(Context([]), Assistant("decided"))));
        var post = Assert.Single(Posts(handler));
        Assert.Equal(300, post.Arguments["thread"]!.GetValue<long>());
        Assert.Empty(chat.Unhandled);
    }

    [Fact]
    public async Task DmAndMentionAreAnsweredReplyIsObservedAndEveryOtherReasonIsAckedWithALine()
    {
        var lines = new List<string>();
        var chat = new LatchedChat();
        chat.Deliver(
            Item(230, reason: "reply", thread: 229),
            Item(231, reason: "about", thread: 229),
            Item(232, reason: "mention", thread: 229, body: "your turn"));
        using var handler = chat.Handler();
        using var client = Client(handler);
        var steering = new FactorySteering(client) { Diagnostics = lines.Add };

        var arrived = await steering.GetSteeringMessagesAsync();
        Assert.Equal(2, arrived.Count);
        Assert.Equal(
            "[inbox from @coder-06-pi2 in board, message 232, thread 229, mention] your turn",
            Text(arrived[1]));
        Assert.Equal(232, steering.Current!.Message.Id);
        Assert.Equal([230L, 231L], AckIds(Assert.Single(Acks(handler))));
        Assert.Equal(["factory: ignored inbox item messageId=231: reason about does not start a task"], lines);

        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(Context([]), Assistant("done"))));
        Assert.Equal([232L], AckIds(Acks(handler)[1]));
        Assert.Empty(chat.Unhandled);
    }

    [Fact]
    public async Task StampTimePutsTheCurrentUtcTimeInTheHeader()
    {
        var chat = new LatchedChat();
        chat.Deliver(Item(241, reason: "mention", thread: 239, body: "your turn"));
        using var handler = chat.Handler();
        using var client = Client(handler);
        var steering = new FactorySteering(client, new FakeClock()) { StampTime = true };

        var arrived = Assert.Single(await steering.GetSteeringMessagesAsync());
        Assert.Equal("[inbox from @coder-06-pi2 in board, message 241, thread 239, mention, now 2026-09-14 12:00 UTC] your turn", Text(arrived));
    }

    [Fact]
    public async Task ADmWithoutAThreadIsAnsweredToTheAuthorWithNoKind()
    {
        var chat = new LatchedChat();
        chat.Deliver(Item(240, reason: "dm", thread: null, author: "digital-scribe", room: "dm", kind: "question", body: "status?"));
        using var handler = chat.Handler();
        using var client = Client(handler);
        var steering = new FactorySteering(client);

        Assert.Single(await steering.GetSteeringMessagesAsync());
        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(Context([]), Assistant("all green"))));

        var post = Assert.Single(Posts(handler));
        // The server refuses kind=answer outside a thread, so a plain DM goes back as a plain DM.
        Assert.Equal("digital-scribe", post.Arguments["to"]!.GetValue<string>());
        Assert.False(post.Arguments.ContainsKey("thread"));
        Assert.False(post.Arguments.ContainsKey("kind"));
        Assert.False(post.Arguments.ContainsKey("room"));
        Assert.Equal([240L], AckIds(Assert.Single(Acks(handler))));
    }

    [Fact]
    public async Task ADmQuestionInsideAThreadIsAnsweredInThatThread()
    {
        var chat = new LatchedChat();
        chat.Deliver(Item(241, reason: "dm", thread: 238, author: "digital-scribe", room: "dm", kind: "question", body: "and now?"));
        using var handler = chat.Handler();
        using var client = Client(handler);
        var steering = new FactorySteering(client);

        Assert.Single(await steering.GetSteeringMessagesAsync());
        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(Context([]), Assistant("still green"))));

        var post = Assert.Single(Posts(handler));
        Assert.Equal(238, post.Arguments["thread"]!.GetValue<long>());
        Assert.Equal("answer", post.Arguments["kind"]!.GetValue<string>());
        Assert.False(post.Arguments.ContainsKey("to"));
        Assert.Equal([241L], AckIds(Assert.Single(Acks(handler))));
    }


    // ---------------------------------------------------------------- the agent's own chat_post answers the target

    /// <summary>One tool-call turn in which the agent called chat_post itself, with the tool result it got back.</summary>
    private static TurnContext AgentPostTurn(JsonObject arguments, string resultText, bool isError = false, string name = "chat__chat_post")
    {
        var call = new ToolCallContent("post-1", name, arguments);
        var message = new AssistantMessage { Content = [new TextContent("posting"), call], Model = "test-model" };
        var result = new ToolResultMessage
        {
            ToolCallId = "post-1",
            ToolName = name,
            Content = [new TextContent(resultText)],
            IsError = isError,
        };
        return new TurnContext(message, [result], Context([]), []);
    }

    private static string Receipt(long id, string room, long? thread) =>
        new JsonObject { ["id"] = id, ["room"] = room, ["thread"] = thread, ["notified"] = new JsonArray() }.ToJsonString();

    /// <summary>Live 13715/13716: the agent replied in the thread with chat_post, then ended with a line of narration,
    /// and that line was posted as a second message into the same thread. The agent's post is the answer to its
    /// target: ack only. The waking item shares that target, so no target is left to post to, and the narration
    /// is dropped (contract: the final text is posted to exactly one target per task - the wake's own - and
    /// nowhere else).</summary>
    [Fact]
    public async Task AThreadTheAgentAlreadyPostedToGetsNoSecondPost()
    {
        var lines = new List<string>();
        var chat = new LatchedChat();
        chat.Deliver(Item(13710, reason: "mention", thread: 13611, author: "claude-agent-maker-1", room: "ops"));
        using var handler = chat.Handler();
        using var client = Client(handler);
        var steering = new FactorySteering(client) { Diagnostics = lines.Add };

        Assert.Single(await steering.GetSteeringMessagesAsync());
        // The agent names the message it answers; the server's receipt reports the thread root it landed in.
        Assert.False(await steering.ShouldStopAfterTurnAsync(AgentPostTurn(
            new JsonObject { ["thread"] = 13710, ["body"] = "Got it" },
            Receipt(13715, "ops", 13611))));
        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(Context([]), Assistant("Acked. Wire check B is confirmed received."))));

        Assert.Empty(Posts(handler));
        Assert.Equal([13710L], AckIds(Assert.Single(Acks(handler))));
        Assert.Empty(chat.Unhandled);
        Assert.Equal([
            "factory: task for message 13710: agent chat_post(s) 13715 (thread 13710->thread 13611) answered 1 of 1 target(s).",
            "factory: task for message 13710 was answered by the agent's own chat_post (message 13715); final text not posted, acknowledged 13710",
        ], lines);
    }

    /// <summary>Live 13529/13539: a plain DM question answered by the agent in its own thread, then the narration
    /// went out as a new DM root. The agent's reply reached the waking item's own target (thread 13521 = the
    /// message itself), so that bucket gets an ack only and no target is left for the narration: no post.</summary>
    [Fact]
    public async Task ADmTheAgentAnsweredInItsThreadGetsNoDmRootPost()
    {
        var chat = new LatchedChat();
        chat.Deliver(Item(13521, reason: "dm", thread: null, author: "claude-agent-maker-1", room: "dm", kind: "question", body: "reply BRAVO"));
        using var handler = chat.Handler();
        using var client = Client(handler);
        var steering = new FactorySteering(client);

        Assert.Single(await steering.GetSteeringMessagesAsync());
        Assert.False(await steering.ShouldStopAfterTurnAsync(AgentPostTurn(
            new JsonObject { ["thread"] = 13521, ["body"] = "BRAVO" },
            Receipt(13529, "dm-agent-maker-2--claude-agent-maker-1", 13521))));
        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(Context([]), Assistant("BRAVO, posted in this thread."))));

        Assert.Empty(Posts(handler));
        Assert.Equal([13521L], AckIds(Assert.Single(Acks(handler))));
        Assert.Empty(chat.Unhandled);
    }

    /// <summary>Defect A closure bar (live 13519/13521, note-architect-cause-20260915): two threadless DMs wake one
    /// task; the agent answers one of them with chat_post (to its author, the DM root) and ends with narration.
    /// The answered DM's bucket gets an ack only. The other DM is a second target, and the wake is agent-answered
    /// with no postable primary left, so the narration is posted nowhere and both buckets are acked. Before the
    /// fix this shape put the narration at the DM root of one or the other depending on model ordering (13528/13529).</summary>
    [Fact]
    public async Task TwoDmsOneWakeAndOneAgentPostLeaveTheOtherDmAckedNotNarrated()
    {
        var chat = new LatchedChat();
        chat.Deliver(
            Item(13519, reason: "dm", thread: null, author: "digital-scribe", room: "dm", kind: "question", body: "is the branch green?"),
            Item(13521, reason: "dm", thread: null, author: "claude-agent-maker-1", room: "dm", kind: "question", body: "did the merge land?"));
        using var handler = chat.Handler();
        using var client = Client(handler);
        var steering = new FactorySteering(client);

        var woken = await steering.GetSteeringMessagesAsync();
        Assert.Equal(2, woken.Count);
        // The agent answers the 13521 DM with its own DM to its author; the receipt echoes no thread and no "to".
        Assert.False(await steering.ShouldStopAfterTurnAsync(AgentPostTurn(
            new JsonObject { ["to"] = "claude-agent-maker-1", ["body"] = "it landed" },
            Receipt(13529, "dm-agent-maker-2--claude-agent-maker-1", null))));
        // The agent also answers the 13519 DM with a DM to its author. Both buckets of the task are now
        // agent-answered, so the task stays open only until the loop's final turn narrates.
        Assert.False(await steering.ShouldStopAfterTurnAsync(AgentPostTurn(
            new JsonObject { ["to"] = "digital-scribe", ["body"] = "green as of the last merge" },
            Receipt(13530, "dm-agent-maker-2--digital-scribe", null))));
        Assert.False((await steering.GetSteeringMessagesAsync()).Count > 0);
        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(Context([]), Assistant("Answered both DMs."))));

        // No steering post anywhere: every bucket was answered by the agent's own posts, so the final text
        // narrates and is posted nowhere. Each item was answered exactly once, whichever order the agent
        // posted in (see the reverse-order test below) - that is what Defect A broke.
        Assert.Empty(Posts(handler));
        var ack = Assert.Single(Acks(handler));
        Assert.Equal([13519L, 13521L], AckIds(ack).Order().ToList());
        Assert.Empty(chat.Unhandled);
    }

    /// <summary>The same two DMs and the same agent answers posted in the reverse order: the outcome is
    /// identical. Before the fix the final text landed at the DM root of whichever bucket the model had
    /// posted to first (or last) - live 13528/13529 - so this pair of tests is Defect A's closure bar:
    /// the reply target must not depend on the agent's posting order.</summary>
    [Fact]
    public async Task TheSameTwoDmsWithTheAgentPostingFirstAnswerOneThenTheOtherEndTheSameWay()
    {
        var chat = new LatchedChat();
        chat.Deliver(
            Item(13519, reason: "dm", thread: null, author: "digital-scribe", room: "dm", kind: "question", body: "is the branch green?"),
            Item(13521, reason: "dm", thread: null, author: "claude-agent-maker-1", room: "dm", kind: "question", body: "did the merge land?"));
        using var handler = chat.Handler();
        using var client = Client(handler);
        var steering = new FactorySteering(client);

        var woken = await steering.GetSteeringMessagesAsync();
        Assert.Equal(2, woken.Count);
        // The reverse posting order of TwoDmsOneWakeAndOneAgentPostLeaveTheOtherDmAckedNotNarrated.
        Assert.False(await steering.ShouldStopAfterTurnAsync(AgentPostTurn(
            new JsonObject { ["to"] = "digital-scribe", ["body"] = "green as of the last merge" },
            Receipt(13530, "dm-agent-maker-2--digital-scribe", null))));
        Assert.False((await steering.GetSteeringMessagesAsync()).Count > 0);
        Assert.False(await steering.ShouldStopAfterTurnAsync(AgentPostTurn(
            new JsonObject { ["to"] = "claude-agent-maker-1", ["body"] = "it landed" },
            Receipt(13529, "dm-agent-maker-2--claude-agent-maker-1", null))));
        Assert.False((await steering.GetSteeringMessagesAsync()).Count > 0);
        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(Context([]), Assistant("Answered both DMs."))));

        // Identical outcome to the forward order: no steering post anywhere, one ack batch with both ids.
        Assert.Empty(Posts(handler));
        var ack = Assert.Single(Acks(handler));
        Assert.Equal([13519L, 13521L], AckIds(ack).Order().ToList());
        Assert.Empty(chat.Unhandled);
    }

    /// <summary>Defect A, same two-DM shape one step further: the agent answers one DM with its tool but leaves
    /// the other DM unanswered, and another wake's item shares a thread target that is not the wake's. With the
    /// wake agent-answered there is no primary; the open buckets are acked, never narrated into. (The contract's
    /// per-target decision never posts to a target the agent answered, and never posts a second time per task.)</summary>
    [Fact]
    public async Task ADmWakeAnsweredByTheAgentAndAThreadTargetAreBothSettledWithoutASecondPost()
    {
        var chat = new LatchedChat();
        chat.Deliver(
            Item(300, reason: "dm", thread: null, author: "digital-scribe", room: "dm", kind: "question", body: "status?"),
            Item(301, reason: "mention", thread: 299, author: "product-owner-2", room: "board", body: "and the tests?"));
        using var handler = chat.Handler();
        using var client = Client(handler);
        var steering = new FactorySteering(client);

        Assert.Equal(2, (await steering.GetSteeringMessagesAsync()).Count);
        // The agent answers the DM itself; the thread-299 mention stays open.
        Assert.False(await steering.ShouldStopAfterTurnAsync(AgentPostTurn(
            new JsonObject { ["to"] = "digital-scribe", ["body"] = "green" },
            Receipt(302, "dm-steering--digital-scribe", null))));
        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(Context([]), Assistant("told them"))));

        // The wake (DM to digital-scribe) was agent-answered; no wake bucket remains, so no post happens and
        // both buckets are acked in one batch.
        Assert.Empty(Posts(handler));
        var ack = Assert.Single(Acks(handler));
        Assert.Equal([300L, 301L], AckIds(ack).Order().ToList());
        Assert.Empty(chat.Unhandled);
    }

    [Fact]
    public async Task ADmTheAgentAnsweredWithItsOwnDirectMessageGetsNoSecondPost()
    {
        var chat = new LatchedChat();
        chat.Deliver(Item(260, reason: "dm", thread: null, author: "product-owner-2", room: "dm", body: "status?"));
        using var handler = chat.Handler();
        using var client = Client(handler);
        var steering = new FactorySteering(client);

        Assert.Single(await steering.GetSteeringMessagesAsync());
        Assert.False(await steering.ShouldStopAfterTurnAsync(AgentPostTurn(
            new JsonObject { ["to"] = "product-owner-2", ["body"] = "all green" },
            Receipt(261, "dm-product-owner-2--coder-06-pi1", null))));
        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(Context([]), Assistant("Sent the status."))));

        Assert.Empty(Posts(handler));
        Assert.Equal([260L], AckIds(Assert.Single(Acks(handler))));
    }

    /// <summary>The agent answered somewhere else. A room post without a thread answers no task target (its
    /// receipt echoes no thread); the thread-150 post answers only its own bucket. The waking item's bucket was
    /// answered by neither, but it is also not a postable primary on its own: with the wake agent-unanswered and
    /// no other open target holding it, there is nothing the steering may post into for it, so the narration is
    /// dropped and both buckets are acked (live board 14317: the narration must never fan out behind the tool).</summary>
    [Fact]
    public async Task AnAgentPostSomewhereElseAlsoEndsTheTaskWithoutTheFinalText()
    {
        var lines = new List<string>();
        var chat = new LatchedChat();
        // One bucket (thread 269) holds both items: the task and its wake are message 270, the final text
        // narrates the agent's own posts. The post "somewhere else" lands in that same bucket: thread 270 is
        // a member of it (a reply to message 270), so it answers the bucket and no second post happens.
        // A post into an unrelated thread leaves the bucket open and the final text posts into it: that is the
        // "UnansweredThreadMessageRepostsTheWakeTextIntoItsOwnThread" contract, which is what keeps a reply
        // from vanishing when the agent's posts went to rooms and other threads.
        chat.Deliver(Item(269, thread: 269, body: "deploy and tell me"));
        chat.Deliver(Item(270, thread: 269, body: "deploy and tell me"));
        using var handler = chat.Handler();
        using var client = Client(handler);
        var steering = new FactorySteering(client) { Diagnostics = lines.Add };

        // The two items share the target thread 269 and are answered by one post and one ack; the second
        // message id rides in the same bucket, so the final text would answer for both.
        Assert.Equal(2, (await steering.GetSteeringMessagesAsync()).Count);
        Assert.False(await steering.ShouldStopAfterTurnAsync(AgentPostTurn(
            new JsonObject { ["room"] = "ops", ["body"] = "redeploying" },
            Receipt(271, "ops", null))));
        // The room post named no bucket and answered nothing: the task stays open.
        Assert.False((await steering.GetSteeringMessagesAsync()).Count > 0);
        Assert.False(await steering.ShouldStopAfterTurnAsync(AgentPostTurn(
            new JsonObject { ["thread"] = 270, ["body"] = "elsewhere" },
            Receipt(272, "ops", 269))));
        Assert.False((await steering.GetSteeringMessagesAsync()).Count > 0);
        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(Context([]), Assistant("deployed"))));

        // No steering post anywhere: the bucket was answered by the agent's posts, and there is no other
        // open bucket left. The ack covers every id of the task, once.
        Assert.Empty(Posts(handler));
        Assert.Equal([269L, 270L], AckIds(Assert.Single(Acks(handler))).Order().ToList());
        Assert.Empty(chat.Unhandled);
        Assert.Equal([
            "factory: task for message 269: agent chat_post(s) 271, 272 (thread 270->thread 269) answered 1 of 1 target(s).",
            "factory: task for message 269 was answered by the agent's own chat_post (message 271, 272); final text not posted, acknowledged 269, 270",
        ], lines);
    }

    /// <summary>A task that ends with no text after the agent answered one of its targets itself settles every
    /// target: nothing would ever reply to one left open, and the open-tasks note re-woke it after each compaction.</summary>
    [Fact]
    public async Task EmptyTextAfterAPartialAgentAnswerSettlesEveryTarget()
    {
        var lines = new List<string>();
        var chat = new LatchedChat();
        chat.Deliver(Item(300, thread: 300, body: "first"));
        chat.Deliver(Item(310, thread: 310, body: "second"));
        using var handler = chat.Handler();
        using var client = Client(handler);
        var steering = new FactorySteering(client) { Diagnostics = lines.Add };

        Assert.Equal(2, (await steering.GetSteeringMessagesAsync()).Count);
        Assert.False(await steering.ShouldStopAfterTurnAsync(AgentPostTurn(
            new JsonObject { ["thread"] = 300, ["body"] = "done" },
            Receipt(301, "ops", 300))));
        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(Context([]), Assistant(""))));

        Assert.Empty(Posts(handler));
        Assert.Equal([300L, 310L], Acks(handler).SelectMany(AckIds).Order().ToList());
        Assert.Empty(steering.OpenTasks);
        Assert.Null(steering.OpenTasksNote());
    }
    [Theory]
    [InlineData("MCP error: An answer must be posted in a thread.", true)]
    [InlineData("MCP error: Message 99999 does not exist.", false)]
    [InlineData("(empty MCP result)", false)]
    public async Task AnAgentPostThatFailedStillGetsTheFinalReply(string resultText, bool isError)
    {
        var chat = new LatchedChat();
        chat.Deliver(Item(280, thread: 279));
        using var handler = chat.Handler();
        using var client = Client(handler);
        var steering = new FactorySteering(client);

        Assert.Single(await steering.GetSteeringMessagesAsync());
        Assert.False(await steering.ShouldStopAfterTurnAsync(AgentPostTurn(
            new JsonObject { ["thread"] = 279, ["body"] = "answer", ["kind"] = "answer" },
            resultText,
            isError)));
        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(Context([]), Assistant("the answer"))));

        var post = Assert.Single(Posts(handler));
        Assert.Equal(279, post.Arguments["thread"]!.GetValue<long>());
    }

    [Fact]
    public async Task AnAgentPostInThePreviousTaskDoesNotSilenceTheNextOne()
    {
        var chat = new LatchedChat();
        chat.Deliver(Item(290, thread: 289, body: "first"));
        using var handler = chat.Handler();
        using var client = Client(handler);
        var steering = new FactorySteering(client);

        Assert.Single(await steering.GetSteeringMessagesAsync());
        Assert.False(await steering.ShouldStopAfterTurnAsync(AgentPostTurn(
            new JsonObject { ["thread"] = 290, ["body"] = "done" },
            Receipt(291, "board", 289))));
        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(Context([]), Assistant("posted it"))));
        Assert.Empty(Posts(handler));

        // A new task in the same thread: the agent posts nothing itself this time, so the final text is the reply.
        chat.Deliver(Item(292, thread: 289, body: "second"));
        Assert.Single(await steering.GetSteeringMessagesAsync());
        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(Context([]), Assistant("second answer"))));

        var post = Assert.Single(Posts(handler));
        Assert.Equal(289, post.Arguments["thread"]!.GetValue<long>());
        Assert.Equal("second answer", post.Arguments["body"]!.GetValue<string>());
        Assert.Equal([292L], AckIds(Acks(handler)[1]));
    }

    /// <summary>Live 14235 then 14323..14330: one wake held mentions from eight threads, the agent answered one of
    /// them with chat_post, and its closing narration was posted into all eight. The per-target rule answers it
    /// exactly once each way: the answered bucket gets an ack only; the seven open buckets get one steering post
    /// - the wake's own target receives the narration, the six secondary targets are skipped and acked with it.
    /// Never a post into a bucket the agent answered.</summary>
    [Fact]
    public async Task EightTargetsAndOneAgentPostGetNoNarrationAnywhere()
    {
        var threads = new long[] { 12677, 12968, 13090, 13153, 13190, 13305, 13611, 14099 };
        var chat = new LatchedChat();
        chat.Deliver([.. threads.Select((t, i) => Item(14200 + i, reason: "mention", thread: t, author: "claude-agent-maker-1", room: "board"))]);
        using var handler = chat.Handler();
        using var client = Client(handler);
        var steering = new FactorySteering(client);

        Assert.Equal(8, (await steering.GetSteeringMessagesAsync()).Count);
        // The agent answers the waking thread (12677) and one other thread (14099) with its own posts.
        Assert.False(await steering.ShouldStopAfterTurnAsync(AgentPostTurn(
            new JsonObject { ["thread"] = 14200, ["body"] = "capacity conflict on this fix" },
            Receipt(14235, "board", 12677),
            name: "chat_post")));
        Assert.False((await steering.GetSteeringMessagesAsync()).Count > 0);
        Assert.False(await steering.ShouldStopAfterTurnAsync(AgentPostTurn(
            new JsonObject { ["thread"] = 14207, ["body"] = "that thread is already covered" },
            Receipt(14236, "board", 14099),
            name: "chat_post")));
        Assert.False((await steering.GetSteeringMessagesAsync()).Count > 0);
        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(Context([]), Assistant("Routed both wakes; nothing to reimplement."))));

        // No steering post anywhere: the agent answered the waking bucket and one secondary; the final text
        // narrates, and the six untouched buckets are acked with it (the ack is what stops them waking a
        // second answer). The ack is one batch with every id of the task.
        Assert.Empty(Posts(handler));
        var ack = Assert.Single(Acks(handler));
        var ids = AckIds(ack);
        Assert.Equal([.. Enumerable.Range(14200, 8).Select(i => (long)i)], ids.Order().ToList());
        Assert.DoesNotContain("Routed both wakes; nothing to reimplement.", Posts(handler).Select(p => p.Arguments["body"]!.GetValue<string>()));
        Assert.Empty(chat.Unhandled);
    }

    [Fact]
    public async Task TheFollowUpLoopAcknowledgesAnIgnoredItemInsteadOfLettingItLatch()
    {
        var chat = new LatchedChat();
        chat.Deliver(Item(250, reason: "about", thread: 249));
        using var handler = chat.Handler();
        using var client = Client(handler);
        var clock = new FakeClock();
        var steering = new FactorySteering(client, clock)
        {
            FollowUpWaitSeconds = 30,
            LatchPollInterval = TimeSpan.FromSeconds(5),
            IdleTimeout = TimeSpan.FromSeconds(10),
        };

        Assert.Empty(await DriveAsync(clock, steering.GetFollowUpMessagesAsync()));

        Assert.Empty(Posts(handler));
        Assert.Equal([250L], AckIds(Assert.Single(Acks(handler))));
        Assert.Empty(chat.Unhandled);
    }

    // ---------------------------------------------------------------- 5. follow-up loop

    [Fact]
    public async Task FollowUpsDisabledReturnEmptyWithoutAnyHttpCall()
    {
        using var handler = ByTool(_ => DigestText([Item(60)]));
        using var client = Client(handler);
        var steering = new FactorySteering(client) { FollowUpWaitSeconds = 0 };

        Assert.Empty(await steering.GetFollowUpMessagesAsync());
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task FollowUpWaitsOnInboxScopeThenReadsMarkingRead()
    {
        using var handler = ByTool(_ => DigestText([Item(61, thread: 60)]));
        using var client = Client(handler);
        var clock = new FakeClock();
        var steering = new FactorySteering(client, clock)
        {
            FollowUpWaitSeconds = 30,
            LatchPollInterval = TimeSpan.FromSeconds(5),
        };

        var messages = await DriveAsync(clock, steering.GetFollowUpMessagesAsync());
        Assert.Equal(
            "[inbox from @coder-06-pi2 in board, message 61, thread 60, mention] do the thing",
            Text(Assert.Single(messages)));

        // Besides the status line the new task writes (chat_manage), the wait and its marking read.
        var requests = handler.Requests.FindAll(r => r.Tool != "chat_manage");
        Assert.Equal(2, requests.Count);
        var wait = requests[0];
        Assert.Equal("chat_wait", wait.Tool);
        Assert.Equal(30, wait.Arguments["timeout_seconds"]!.GetValue<int>());
        Assert.Equal("inbox", wait.Arguments["scope"]!.GetValue<string>());
        Assert.False(wait.Arguments.ContainsKey("room"));
        Assert.False(wait.Arguments.ContainsKey("rooms"));
        var read = requests[1];
        Assert.Equal("chat_read", read.Tool);
        Assert.True(read.Arguments["mark_read"]!.GetValue<bool>());
        Assert.False(read.Arguments.ContainsKey("room"));
    }

    [Fact]
    public async Task FollowUpBacksOffDoublingToFiveMinutesThenRecovers()
    {
        var calls = 0;
        using var handler = new FakeHandler(_ =>
        {
            var failing = Interlocked.Increment(ref calls) <= 5;
            return Task.FromResult(new HttpResponseMessage(failing ? HttpStatusCode.InternalServerError : HttpStatusCode.OK)
            {
                Content = new StringContent(
                    failing ? "down" : Rpc(DigestText([Item(70, thread: 69)])),
                    Encoding.UTF8,
                    "application/json"),
            });
        });
        using var client = Client(handler);
        var clock = new FakeClock();
        var steering = new FactorySteering(client, clock)
        {
            FollowUpWaitSeconds = 30,
            LatchPollInterval = TimeSpan.FromSeconds(5),
        };

        var task = steering.GetFollowUpMessagesAsync().AsTask();
        var observed = new List<TimeSpan>();
        for (var i = 0; i < 400 && !task.IsCompleted; i++)
        {
            var backoff = steering.Backoff;
            if (backoff > TimeSpan.Zero && (observed.Count == 0 || observed[^1] != backoff)) observed.Add(backoff);
            if (!await StepAsync(clock, task)) break;
        }
        var messages = await task;

        Assert.Equal(
            [
                TimeSpan.FromSeconds(30),
                TimeSpan.FromSeconds(60),
                TimeSpan.FromSeconds(120),
                TimeSpan.FromSeconds(240),
                TimeSpan.FromMinutes(5),
            ],
            observed);
        Assert.Equal(FactorySteering.MaxBackoff, TimeSpan.FromMinutes(5));
        Assert.Equal(TimeSpan.Zero, steering.Backoff);
        Assert.Equal(
            "[inbox from @coder-06-pi2 in board, message 70, thread 69, mention] do the thing",
            Text(Assert.Single(messages)));
        Assert.Contains(70L, steering.Seen);
    }

    [Fact]
    public async Task FollowUpSleepsBetweenLatchedDigestsInsteadOfSpinning()
    {
        using var handler = ByTool(_ => DigestText([Item(80, thread: 79)]));
        using var client = Client(handler);
        var clock = new FakeClock();
        var steering = new FactorySteering(client, clock)
        {
            FollowUpWaitSeconds = 30,
            LatchPollInterval = TimeSpan.FromSeconds(5),
        };

        // Consume the item once so every later digest only repeats what was already seen.
        Assert.Single(await steering.GetSteeringMessagesAsync());
        var consumed = Count(handler);

        using var cts = new CancellationTokenSource();
        var task = steering.GetFollowUpMessagesAsync(cts.Token).AsTask();
        var cycles = 0;
        while (cycles < 10 && !task.IsCompleted && await StepAsync(clock, task)) cycles++;

        await cts.CancelAsync();
        Assert.Empty(await task);

        Assert.Equal(10, cycles);
        Assert.Equal(TimeSpan.FromSeconds(50), clock.Elapsed);
        // One chat_wait plus its chat_read per 5 s cycle, never a spin.
        Assert.InRange(Count(handler) - consumed, 2, 2 * (cycles + 2));
    }

    [Fact]
    public async Task FollowUpReturnsEmptyWhenIdleTimeoutElapses()
    {
        using var handler = ByTool(_ => DigestText([], timedOut: true));
        using var client = Client(handler);
        var clock = new FakeClock();
        var steering = new FactorySteering(client, clock)
        {
            FollowUpWaitSeconds = 30,
            LatchPollInterval = TimeSpan.FromSeconds(5),
            IdleTimeout = TimeSpan.FromSeconds(20),
        };

        Assert.Empty(await DriveAsync(clock, steering.GetFollowUpMessagesAsync()));
        Assert.Equal(TimeSpan.FromSeconds(20), clock.Elapsed);
        Assert.Equal(5, Count(handler));
        Assert.All(handler.Requests, r => Assert.Equal("chat_wait", r.Tool));
    }

    [Fact]
    public async Task IdleExitDoesNotWaitAFullLatchPollInterval()
    {
        using var handler = ByTool(_ => DigestText([Item(310)]));
        using var client = Client(handler);
        var clock = new FakeClock();
        var steering = new FactorySteering(client, clock)
        {
            FollowUpWaitSeconds = 30,
            LatchPollInterval = TimeSpan.FromSeconds(5),
            IdleTimeout = TimeSpan.FromSeconds(1),
        };

        // Consume the item so the latch only ever repeats it: the follow-up loop is idle from its first digest.
        Assert.Single(await steering.GetSteeringMessagesAsync());

        Assert.Empty(await DriveAsync(clock, steering.GetFollowUpMessagesAsync()));
        // One second of idle budget, not one 5 s latch poll.
        Assert.Equal(TimeSpan.FromSeconds(1), clock.Elapsed);
    }

    [Fact]
    public async Task AnIdleSeatWhoseAssignmentChangedEndsTheRunForARestart()
    {
        using var handler = ByTool(_ => DigestText([], timedOut: true));
        using var client = Client(handler);
        var clock = new FakeClock();
        var checks = 0;
        var steering = new FactorySteering(client, clock)
        {
            FollowUpWaitSeconds = 30,
            LatchPollInterval = TimeSpan.FromSeconds(5),
            AssignmentCheckInterval = TimeSpan.FromSeconds(60),
            AssignmentChanged = _ => Task.FromResult(++checks >= 2),
        };

        Assert.Empty(await DriveAsync(clock, steering.GetFollowUpMessagesAsync()));
        Assert.True(steering.RestartRequested);
        // The first check found no change; the next one waited out its interval, then ended the wait.
        Assert.Equal(2, checks);
        Assert.InRange(clock.Elapsed, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(70));
    }

    [Fact]
    public async Task AnUndeliveredTaskKeepsTheSeatOnItsAssignment()
    {
        using var handler = ByTool(_ => DigestText([Item(310)]));
        using var client = Client(handler);
        var clock = new FakeClock();
        var checks = 0;
        var steering = new FactorySteering(client, clock)
        {
            FollowUpWaitSeconds = 30,
            LatchPollInterval = TimeSpan.FromSeconds(5),
            IdleTimeout = TimeSpan.FromSeconds(120),
            AssignmentChanged = _ => Task.FromResult(++checks > 0),
        };

        // Message 310 is delivered and not answered: the seat is mid-task, whatever the broker says.
        Assert.Single(await steering.GetSteeringMessagesAsync());
        Assert.Empty(await DriveAsync(clock, steering.GetFollowUpMessagesAsync()));
        Assert.Equal(0, checks);
        Assert.False(steering.RestartRequested);
    }

    [Fact]
    public async Task AnUndeliveredTaskStillFollowsItsAssignmentInPlace()
    {
        using var handler = ByTool(_ => DigestText([Item(311)]));
        using var client = Client(handler);
        var clock = new FakeClock();
        var checks = 0;
        var steering = new FactorySteering(client, clock)
        {
            FollowUpWaitSeconds = 30,
            LatchPollInterval = TimeSpan.FromSeconds(5),
            IdleTimeout = TimeSpan.FromSeconds(120),
            CheckAssignmentWhileIdle = _ => Task.FromResult(++checks < 0),
        };

        // Mid-task (311 delivered, not answered) the seat still asks: a switch in place costs it no task.
        Assert.Single(await steering.GetSteeringMessagesAsync());
        Assert.Empty(await DriveAsync(clock, steering.GetFollowUpMessagesAsync()));
        Assert.True(checks > 0);
        Assert.False(steering.RestartRequested);
    }

    [Fact]
    public async Task ASeatParkedOnAFailingModelStopsWaitingWhenItIsMoved()
    {
        using var handler = ByTool(_ => DigestText([], timedOut: true));
        using var client = Client(handler);
        var clock = new FakeClock();
        var checks = 0;
        var steering = new FactorySteering(client, clock)
        {
            FollowUpWaitSeconds = 30,
            LatchPollInterval = TimeSpan.FromSeconds(5),
            CheckAssignmentWhileIdle = _ => Task.FromResult(++checks >= 3),
            EndIdleOnAssignmentChange = true,
        };

        Assert.Empty(await DriveAsync(clock, steering.GetFollowUpMessagesAsync()));
        Assert.Equal(3, checks);
        Assert.False(steering.RestartRequested);
    }

    [Fact]
    public async Task DroppedItemsAreReportedOnceEachAndCounted()
    {
        var lines = new List<string>();
        using var handler = ByToolAckingAll(_ => DigestText([Item(300), Mismatched(301), Mismatched(302)]));
        using var client = Client(handler);
        var steering = new FactorySteering(client) { Diagnostics = lines.Add };

        var messages = await steering.GetSteeringMessagesAsync();

        Assert.Single(messages);
        Assert.Equal(2, lines.Count);
        Assert.All(lines, line => Assert.Matches(@"^factory: dropped inbox item inboxId=\d+ messageId=\d+: .", line));
        Assert.Equal(2, steering.DroppedCount);
        // Only the good item entered the run: a dropped item is never seen, never a Current, never an arrival.
        Assert.Equal([300L], steering.Seen.Order());
        Assert.Equal(300, steering.Current!.Message.Id);
        // ADR v23.18: a refused item is still acked, by the top-level messageId it carried, so the latch lets go.
        Assert.Equal([1301L, 1302L], AckIds(Assert.Single(Acks(handler))));
        Assert.Equal(2, steering.AckedCount);
    }

    [Fact]
    public async Task AMismatchedItemIsAcknowledgedByItsTopLevelMessageId()
    {
        using var handler = ByToolAckingAll(_ => DigestText([Mismatched(301)]));
        using var client = Client(handler);
        var steering = new FactorySteering(client);

        Assert.Empty(await steering.GetSteeringMessagesAsync());

        // The nested id 301 is the one the parser refused to trust; 1301 is the inbox row's own message id.
        Assert.Equal([1301L], AckIds(Assert.Single(Acks(handler))));
        Assert.Empty(steering.Seen);
    }

    [Fact]
    public async Task AHeadsOnlyItemWakesTheSeatAndSaysTheBodyIsOnlyTheHead()
    {
        var chat = new LatchedChat();
        chat.Deliver(HeadOnly(304, head: "**Assignment** wi-77 is yours"));
        using var handler = chat.Handler();
        using var client = Client(handler);
        var steering = new FactorySteering(client);

        // Before the heads-only digest was understood, this row was dropped and the seat never woke.
        var arrived = await steering.GetSteeringMessagesAsync();
        Assert.Equal(
            "[inbox from @coder-06-pi2 in board, message 304, mention] **Assignment** wi-77 is yours\n"
            + "(head only: this is the message's first line, not the message. Read it with chat_read id=304 before you answer.)",
            Text(Assert.Single(arrived)));
        Assert.Contains(304L, steering.Seen);
        Assert.Equal(0, steering.DroppedCount);
    }

    /// <summary>The wrong-pointer incident (ops 99028, live check 2026-09-20 11:29Z): the maker's question
    /// 98907 woke coder-06-pi5 through a heads-only row, and the wrapper quoted another message's head and
    /// named chat_read thread=99001 - a reply in an unrelated thread - instead of the wake row. A row's
    /// wrapper may only ever quote that row's head and point at that row.</summary>
    private static JsonObject HeadOnlyReply(long messageId, long thread, string head, string reason = "mention")
    {
        var item = HeadOnly(messageId, reason: reason, head: head);
        item["thread"] = thread;
        return item;
    }

    [Fact]
    public async Task AHeadsOnlyReplyNamesItsOwnThreadAndQuotesItsOwnHead()
    {
        var chat = new LatchedChat();
        chat.Deliver(HeadOnlyReply(98907, 98887, "Which thread does the wake row point at?"));
        using var handler = chat.Handler();
        using var client = Client(handler);
        var steering = new FactorySteering(client);

        var arrived = await steering.GetSteeringMessagesAsync();
        // The pointer is the row's own thread (98887), not any other message id, and never a bare id=.
        Assert.Equal(
            "[inbox from @coder-06-pi2 in board, message 98907, thread 98887, mention] Which thread does the wake row point at?\n"
            + "(head only: this is the message's first line, not the message. Read it with chat_read thread=98887 before you answer.)",
            Text(Assert.Single(arrived)));
    }

    [Fact]
    public async Task EachHeadsOnlyRowOfADigestPointsAtItselfAndQuotesItself()
    {
        // The incident shape: one digest, two rows, and the fetch pointer of one must not name the other.
        var chat = new LatchedChat();
        chat.Deliver(
            HeadOnlyReply(98907, 98887, "the maker's question that woke pi5"),
            HeadOnly(99001, head: "an unrelated reply in another conversation"));
        using var handler = chat.Handler();
        using var client = Client(handler);
        var steering = new FactorySteering(client);

        var arrived = await steering.GetSteeringMessagesAsync();
        Assert.Equal(2, arrived.Count);
        var question = arrived[0];
        Assert.Contains("chat_read thread=98887", Text(question), StringComparison.Ordinal);
        Assert.DoesNotContain("99001", Text(question), StringComparison.Ordinal);
        Assert.DoesNotContain("unrelated reply", Text(question), StringComparison.Ordinal);
        // A root row with no thread id names itself by id, so a fetch can never land in another row's thread.
        var unrelated = arrived[1];
        Assert.Contains("chat_read id=99001", Text(unrelated), StringComparison.Ordinal);
        Assert.DoesNotContain("98907", Text(unrelated), StringComparison.Ordinal);
        Assert.DoesNotContain("98887", Text(unrelated), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnItemWithNoMessageObjectIsAcknowledgedByItsTopLevelMessageId()
    {
        using var handler = ByToolAckingAll(_ => DigestText([WithoutMessageObject(303)]));
        using var client = Client(handler);
        var steering = new FactorySteering(client);

        Assert.Empty(await steering.GetSteeringMessagesAsync());

        Assert.Equal([303L], AckIds(Assert.Single(Acks(handler))));
        Assert.Equal(1, steering.AckedCount);
    }

    [Fact]
    public async Task AnItemWithNoIdIsNeverAckedAndTheFollowUpLoopBacksOffInsteadOfSpinning()
    {
        using var handler = ByToolAckingAll(_ => DigestText([WithoutAnyMessageId()]));
        using var client = Client(handler);
        var clock = new FakeClock();
        var steering = new FactorySteering(client, clock)
        {
            FollowUpWaitSeconds = 30,
            LatchPollInterval = TimeSpan.FromSeconds(5),
        };

        using var cts = new CancellationTokenSource();
        var task = steering.GetFollowUpMessagesAsync(cts.Token).AsTask();
        var sleeps = new List<TimeSpan>();
        for (var i = 0; i < 4 && !task.IsCompleted; i++)
        {
            var before = clock.Elapsed;
            if (!await StepAsync(clock, task)) break;
            sleeps.Add(clock.Elapsed - before);
        }
        await cts.CancelAsync();
        Assert.Empty(await task);

        // An item that can never be acked keeps chat_wait returning at once: back off instead of the 5 s latch poll.
        Assert.Equal(
            [TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(120), TimeSpan.FromSeconds(240)],
            sleeps);
        Assert.Empty(Acks(handler));
        Assert.Equal(1, steering.DroppedCount);
    }

    [Fact]
    public async Task ALatchedDroppedItemIsReportedOnlyOnce()
    {
        var lines = new List<string>();
        // The latch hands the same refused item back on every digest until it is acked, which this surface cannot do.
        var latched = new List<JsonObject> { Mismatched(320) };
        using var handler = ByToolAckingAll(_ => DigestText(latched));
        using var client = Client(handler);
        var steering = new FactorySteering(client) { Diagnostics = lines.Add };

        Assert.Empty(await steering.GetSteeringMessagesAsync());
        Assert.Empty(await steering.GetSteeringMessagesAsync());
        latched.Add(Mismatched(321));
        Assert.Empty(await steering.GetSteeringMessagesAsync());

        // Three digests, four refusals, two distinct items: one line and one count each.
        Assert.Equal(2, lines.Count);
        Assert.Equal(2, steering.DroppedCount);
        Assert.Contains(lines, line => line.Contains("messageId=1320", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("messageId=1321", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AThrowingDiagnosticsSinkNeverReachesTheLoop()
    {
        using var handler = ByTool(_ => DigestText([Mismatched(305)]));
        using var client = Client(handler);
        var steering = new FactorySteering(client) { Diagnostics = _ => throw new InvalidOperationException("stderr is closed") };

        Assert.Empty(await steering.GetSteeringMessagesAsync());
        Assert.Equal(1, steering.DroppedCount);
    }

    [Fact]
    public async Task FollowUpReturnsEmptyOnCancellationWithoutThrowing()
    {
        using var handler = ByTool(_ => DigestText([Item(90)]));
        using var client = Client(handler);
        var clock = new FakeClock();
        var steering = new FactorySteering(client, clock) { FollowUpWaitSeconds = 30 };
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        Assert.Empty(await steering.GetFollowUpMessagesAsync(cts.Token));
        Assert.Empty(handler.Requests);
    }

    // ---------------------------------------------------------------- 6. nothing throws

    [Fact]
    public async Task ThrowingHandlerYieldsNoMessagesAndNoReply()
    {
        using var handler = new FakeHandler(_ => throw new HttpRequestException("socket closed"));
        using var client = Client(handler);
        var steering = new FactorySteering(client);

        Assert.Empty(await steering.GetSteeringMessagesAsync());
        Assert.Equal(FactorySteering.InitialBackoff, steering.Backoff);
        Assert.Null(steering.Current);
        Assert.Null(await steering.PrepareNextTurnAsync(Turn(Context([]), Assistant("hi"))));
        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(Context([]), Assistant("hi"))));
    }

    [Fact]
    public async Task NonJsonPayloadAndErrorFrameYieldNoMessages()
    {
        using var garbage = ByTool(_ => "this is not json at all");
        using var garbageClient = Client(garbage);
        var onGarbage = new FactorySteering(garbageClient);
        Assert.Empty(await onGarbage.GetSteeringMessagesAsync());
        Assert.Null(onGarbage.Current);
        Assert.Equal(FactorySteering.InitialBackoff, onGarbage.Backoff);

        using var failing = ByTool(_ => "Unauthenticated request.", isError: true);
        using var failingClient = Client(failing);
        var onError = new FactorySteering(failingClient);
        Assert.Empty(await onError.GetSteeringMessagesAsync());
        Assert.Equal(FactorySteering.InitialBackoff, onError.Backoff);
    }

    [Fact]
    public async Task FailingPostStillEndsTheTaskWithoutThrowing()
    {
        using var handler = new FakeHandler(async request =>
        {
            var body = await request.Content!.ReadAsStringAsync();
            var tool = JsonNode.Parse(body)!["params"]!["name"]!.GetValue<string>();
            if (tool == "chat_post") throw new HttpRequestException("post refused");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Rpc(DigestText([Item(100, thread: 99)])), Encoding.UTF8, "application/json"),
            };
        });
        using var client = Client(handler);
        var steering = new FactorySteering(client);

        Assert.Single(await steering.GetSteeringMessagesAsync());
        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(Context([]), Assistant("answer"))));
        Assert.Single(Posts(handler));

        // Never retried: the task is over whatever the server said.
        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(Context([]), Assistant("answer"))));
        Assert.Single(Posts(handler));
    }

    // ---------------------------------------------------------------- open tasks carried across a compaction

    private static TurnContext StatusTurn(string status, bool isError = false)
    {
        var call = new ToolCallContent("status-1", "chat__chat_manage", new JsonObject { ["op"] = "status", ["status"] = status });
        var message = new AssistantMessage { Content = [call], Model = "test-model" };
        var result = new ToolResultMessage
        {
            ToolCallId = "status-1",
            ToolName = "chat__chat_manage",
            Content = [new TextContent(isError ? "refused" : "{\"ok\":true}")],
            IsError = isError,
        };
        return new TurnContext(message, [result], Context([]), []);
    }

    [Fact]
    public async Task OpenTasksNoteListsNewestFirstAndPrioritizesCurrentWork()
    {
        var chat = new LatchedChat();
        chat.Deliver(
            Item(59494, thread: 59400, author: "digital-scribe", room: "pi-sharp", body: "fix the\n  compaction   drop"),
            Item(59501, reason: "dm", author: "team-lead-2", room: "dm", body: "status?"));
        using var handler = chat.Handler();
        using var client = Client(handler);
        var steering = new FactorySteering(client);

        Assert.Equal(2, (await steering.GetSteeringMessagesAsync()).Count);
        // Mid-task: a tool-call turn posts nothing, so both are still owed an answer.
        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(Context([]), Assistant("working", toolCall: true))));

        Assert.Equal([59501L, 59494L], steering.OpenTasks.Select(i => i.Message.Id));
        var note = steering.OpenTasksNote();
        Assert.NotNull(note);
        Assert.StartsWith("[open tasks carried across compaction]", note);
        Assert.Contains("1. message 59501 from @team-lead-2 in dm (dm): status?\n", note);
        Assert.Contains("2. message 59494 from @digital-scribe in pi-sharp, thread 59400 (mention): fix the compaction drop\n", note);
        Assert.Contains("newest first", note);
        Assert.Contains("stale or superseded", note);
        Assert.Contains("say so in its thread and why", note);
        // The only status is the one the task's start wrote.
        Assert.Contains("Your chat status: \"working — message 59494 in pi-sharp\"", note);
    }

    [Fact]
    public async Task OpenTasksNoteIsAbsentWhenNothingIsOpen()
    {
        var chat = new LatchedChat();
        using var handler = chat.Handler();
        using var client = Client(handler);
        var steering = new FactorySteering(client);
        Assert.Null(steering.OpenTasksNote());
        Assert.Empty(steering.OpenTasks);

        // Observed and ignored reasons are never owed an answer.
        chat.Deliver(Item(60, reason: "reply", thread: 50), Item(61, reason: "owner", room: "general"), Item(62, reason: "room"));
        await steering.GetSteeringMessagesAsync();
        Assert.Null(steering.OpenTasksNote());

        // A task whose final reply was posted is answered.
        chat.Deliver(Item(63, thread: 50));
        Assert.Single(await steering.GetSteeringMessagesAsync());
        Assert.NotNull(steering.OpenTasksNote());
        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(Context([]), Assistant("done"))));
        Assert.Single(Posts(handler));
        Assert.Null(steering.OpenTasksNote());
    }

    [Fact]
    public async Task AReplyInAnOpenItemThreadAcknowledgesTheEarlierItem()
    {
        var chat = new LatchedChat();
        chat.Deliver(Item(70, thread: 69, body: "please fix"));
        using var handler = chat.Handler();
        using var client = Client(handler);
        var steering = new FactorySteering(client);

        Assert.Single(await steering.GetSteeringMessagesAsync());
        Assert.Single(steering.OpenTasks);

        // A later reply in that thread closes the earlier request, even though it is not a post
        // addressed to the original inbox item itself.
        chat.Deliver(Item(72, reason: "reply", thread: 69, body: "resolved in the meantime"));
        Assert.Single(await steering.GetSteeringMessagesAsync()); // the reply itself remains observed
        Assert.Empty(steering.OpenTasks);
        Assert.Null(steering.OpenTasksNote());
    }

    [Fact]
    public async Task OpenTasksExcludeATargetTheAgentAnsweredWithItsOwnPost()
    {
        var chat = new LatchedChat();
        chat.Deliver(Item(70, thread: 69, body: "first"), Item(71, thread: 80, body: "second"));
        using var handler = chat.Handler();
        using var client = Client(handler);
        var steering = new FactorySteering(client);

        Assert.Equal(2, (await steering.GetSteeringMessagesAsync()).Count);
        Assert.False(await steering.ShouldStopAfterTurnAsync(AgentPostTurn(
            new JsonObject { ["thread"] = 80, ["body"] = "done with the second" },
            Receipt(9100, "board", 80))));

        Assert.Equal([70L], steering.OpenTasks.Select(i => i.Message.Id));
        var note = steering.OpenTasksNote()!;
        Assert.Contains("message 70 ", note);
        Assert.DoesNotContain("message 71 ", note);

        // A failed post answers nothing.
        Assert.False(await steering.ShouldStopAfterTurnAsync(AgentPostTurn(
            new JsonObject { ["thread"] = 69, ["body"] = "x" }, "refused", isError: true)));
        Assert.Equal([70L], steering.OpenTasks.Select(i => i.Message.Id));
    }

    [Fact]
    public async Task OpenTasksNoteIsBoundedInCountAndBodyLength()
    {
        var chat = new LatchedChat();
        var body = new string('x', 1000);
        chat.Deliver([.. Enumerable.Range(0, 15).Select(i => Item(200 + i, thread: 1000 + i, body: body))]);
        using var handler = chat.Handler();
        using var client = Client(handler);
        var steering = new FactorySteering(client);

        Assert.Equal(15, (await steering.GetSteeringMessagesAsync()).Count);
        Assert.Equal(15, steering.OpenTasks.Count);
        var note = steering.OpenTasksNote()!;
        var listed = note.Split('\n').Where(l => l.Contains(". message ", StringComparison.Ordinal)).ToList();
        Assert.Equal(FactorySteering.MaxOpenTasksListed, listed.Count);
        Assert.Contains("message 214 ", note);
        Assert.DoesNotContain("message 204 ", note);
        Assert.Contains("... and 5 more not listed.", note);
        Assert.All(listed, l => Assert.EndsWith(new string('x', FactorySteering.MaxOpenTaskBodyChars) + "...", l));
        Assert.DoesNotContain(new string('x', FactorySteering.MaxOpenTaskBodyChars + 1), note);
    }

    [Fact]
    public async Task OpenTasksNoteCarriesTheLastStatusTheAgentSet()
    {
        var chat = new LatchedChat();
        chat.Deliver(Item(300, thread: 299));
        using var handler = chat.Handler();
        using var client = Client(handler);
        var steering = new FactorySteering(client);

        Assert.Single(await steering.GetSteeringMessagesAsync());
        Assert.False(await steering.ShouldStopAfterTurnAsync(StatusTurn("wi-7: phase 2 tests")));
        Assert.False(await steering.ShouldStopAfterTurnAsync(StatusTurn("ignored, refused", isError: true)));

        Assert.Equal("wi-7: phase 2 tests", steering.CurrentStatus);
        Assert.Contains("Your chat status: \"wi-7: phase 2 tests\"", steering.OpenTasksNote());
    }

    // ---------------------------------------------------------------- the carried-across-compaction marker on the note

    [Fact]
    public async Task TheMarkerAppearsOnlyOnItemsStillOpenAfterANoteSurvivedACompaction()
    {
        var chat = new LatchedChat();
        chat.Deliver(Item(59494, thread: 59400, author: "digital-scribe", room: "pi-sharp", body: "fix the compaction drop"));
        using var handler = chat.Handler();
        using var client = Client(handler);
        var steering = new FactorySteering(client);

        Assert.Single(await steering.GetSteeringMessagesAsync());
        // A fresh item has no mark and no carried ids.
        Assert.Empty(steering.CarriedAcrossCompaction);
        Assert.DoesNotContain("[carried across a compaction", steering.OpenTasksNote());

        // The compaction happened with this item open: the note listed it, so it is marked now.
        steering.NoteCompactionOccurred(steering.OpenTasksNote());
        Assert.Equal([59494L], steering.CarriedAcrossCompaction);
        Assert.Contains("message 59494 from @digital-scribe in pi-sharp, thread 59400 (mention) [carried across a compaction - answer or hand back explicitly]: fix the compaction drop", steering.OpenTasksNote());

        // A second compaction keeps the mark and does not duplicate it.
        steering.NoteCompactionOccurred(steering.OpenTasksNote());
        Assert.Equal([59494L], steering.CarriedAcrossCompaction);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(steering.OpenTasksNote()!, @"\[carried across a compaction"));

        // Once answered, the mark leaves with the item.
        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(Context([]), Assistant("fixed it"))));
        Assert.Empty(steering.OpenTasks);
        Assert.Empty(steering.CarriedAcrossCompaction);
    }

    [Fact]
    public async Task AnItemThatOnlyLaterWentUnansweredNeverInheritsTheMark()
    {
        var chat = new LatchedChat();
        chat.Deliver(Item(70, thread: 69, body: "first"));
        using var handler = chat.Handler();
        using var client = Client(handler);
        var steering = new FactorySteering(client);

        Assert.Single(await steering.GetSteeringMessagesAsync());
        steering.NoteCompactionOccurred(steering.OpenTasksNote());
        Assert.Equal([70L], steering.CarriedAcrossCompaction);

        // A newer item arrives in the running task; a note that lists it marks it too, and each mark appears once.
        chat.Deliver(Item(71, thread: 80, body: "second"));
        Assert.Single(await steering.GetSteeringMessagesAsync());
        steering.NoteCompactionOccurred(steering.OpenTasksNote());
        Assert.Equal([70L, 71L], steering.CarriedAcrossCompaction);
        var note = steering.OpenTasksNote()!;
        Assert.Contains("thread 69 (mention) [carried across a compaction - answer or hand back explicitly]: first", note);
        Assert.Contains("thread 80 (mention) [carried across a compaction - answer or hand back explicitly]: second", note);
        var marks = System.Text.RegularExpressions.Regex.Matches(note, @"\[carried across a compaction");
        Assert.Equal(2, marks.Count);

        // A compaction with nothing open marks nothing and leaves the existing marks alone.
        steering.NoteCompactionOccurred(null);
        Assert.Equal([70L, 71L], steering.CarriedAcrossCompaction);
    }

    // ---------------------------------------------------------------- wi-60 wiring: the running seat's hook route

    /// <summary>End-to-end through the production route (CliHost.FactoryCompactionHooks): the hooks a real attached
    /// seat installs compute the carried-across mark via NoteCompactionOccurred. A running seat, not a test,
    /// calls the mark: the hook AgentSession.LogCompaction fires hands the open-tasks note to the same routing
    /// the seat uses, and the item listed across the compaction shows the suffix in the note the seat carries.</summary>
    [Fact]
    public async Task TheCliHostHookRouteMarksTheItemsCarriedAcrossARealCompaction()
    {
        var chat = new LatchedChat();
        chat.Deliver(Item(59494, thread: 59400, author: "digital-scribe", room: "pi-sharp", body: "fix the compaction drop"));
        using var handler = chat.Handler();
        using var client = Client(handler);
        var steering = new FactorySteering(client);

        Assert.Single(await steering.GetSteeringMessagesAsync());
        // A fresh item has no mark.
        Assert.DoesNotContain("[carried across a compaction", steering.OpenTasksNote());

        // The exact hooks CliHost installs on an attached seat (wi-60: before this, NoteCompactionOccurred
        // had zero production callers).
        var hooks = PiSharp.Cli.CliHost.FactoryCompactionHooks(steering, Self, "/tmp");
        Assert.NotNull(hooks);

        // What AgentSession.LogCompaction does on every completed automatic compaction: fire the hook with
        // the open-tasks note the seat carries across it.
        hooks!.OnCompactionOccurred?.Invoke(hooks.CarryForward?.Invoke());

        Assert.Equal([59494L], steering.CarriedAcrossCompaction);
        Assert.Contains("message 59494 from @digital-scribe in pi-sharp, thread 59400 (mention) [carried across a compaction - answer or hand back explicitly]", steering.OpenTasksNote());

        // A second compaction keeps the mark; the hook route never duplicates it.
        hooks.OnCompactionOccurred?.Invoke(hooks.CarryForward?.Invoke());
        Assert.Equal([59494L], steering.CarriedAcrossCompaction);

        // Once the seat answers, the mark leaves with the item.
        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(Context([]), Assistant("fixed it"))));
        Assert.Empty(steering.OpenTasks);
        Assert.Empty(steering.CarriedAcrossCompaction);
    }

    // ---------------------------------------------------------------- wi-78: the real end-to-drive fact

    /// <summary>What this fact pins, and why it exists: the steering-only fact (wi-48, the roundtrip) pins the
    /// delivery contract WITHOUT driving the loop - it calls the steering hooks by hand. This fact closes the
    /// uncovered half: it drives the REAL <c>AgentLoop.Run</c> (a scripted, no-network model) wired to the REAL seat
    /// hooks, end-to-drive, so the run flows through the follow-up idle poll to <c>AgentEnd</c>. Before wi-78 that
    /// path was untestable in the harness: the idle poll slept on real wall clock, so an end-to-drive run hung for
    /// minutes. The fake clock virtualizes both the poll's <c>Task.Delay</c> (it uses the injected <see cref="TimeProvider"/>
    /// timer) and the loop's virtual time, so the run now terminates on a condition the harness controls (the idle
    /// timeout, in virtual time). The acceptance bar is that the run completes under a wall-clock ceiling instead of
    /// hanging: if the poll still loops on real wall clock, the step budget below trips and this fails fast.</summary>
    [Fact]
    public async Task EndToDriveAgentLoopRunsThroughTheFollowUpPollToAgentEndUnderAWallClockCeiling()
    {
        const string wakeText = "land exactly once in the wake thread";
        const long wakeThread = 40700;
        const long wakeId = 40701;

        var chat = new LatchedChat();
        chat.Deliver(Item(wakeId, reason: "mention", thread: wakeThread, author: "team-lead-2", room: "board", body: "ship it"));
        using var handler = chat.Handler();
        using var client = Client(handler);

        // The seat wiring the real AgentLoop reads - one steering instance; both polls and the turn hook on it.
        var clock = new FakeClock();
        var steering = new FactorySteering(client, clock)
        {
            FollowUpWaitSeconds = 30,
            LatchPollInterval = TimeSpan.FromSeconds(5),
            IdleTimeout = TimeSpan.FromSeconds(20),
        };

        var config = new AgentLoopConfig
        {
            Model = PiSharp.Tests.Agent.Fakes.Model,
            GetSteeringMessages = () => steering.GetSteeringMessagesAsync(),
            GetFollowUpMessages = () => steering.GetFollowUpMessagesAsync(),
            PrepareNextTurn = t => steering.PrepareNextTurnAsync(t),
            ShouldStopAfterTurn = t => steering.ShouldStopAfterTurnAsync(t),
        };

        // A scripted model, no network: one clean reply for the wake, then one for the follow-up arrival.
        var stream = new PiSharp.Tests.Agent.ScriptedStream().Then(PiSharp.Tests.Agent.Fakes.Text(wakeText)).Then(PiSharp.Tests.Agent.Fakes.Text("follow-up answered"));

        var task = Task.Run(async () =>
        {
            var events = new List<AgentEvent>();
            await foreach (var e in AgentLoop.Run([new UserMessage("")], Context([]), config, stream.Stream))
                events.Add(e);
            return events;
        });

        // Drive the real loop, moving only the fake clock, to completion. The wall-clock ceiling is the acceptance
        // bar: the run must finish under a real-time bound, not hang on the idle poll's sleep. The step budget is the
        // trip wire - a non-terminating (real-wall-clock) poll never completes inside it and DriveAsync fails fast.
        var wall = System.Diagnostics.Stopwatch.StartNew();
        List<AgentEvent> events;
        events = await DriveTaskAsync(clock, task, maxSteps: 800);
        wall.Stop();

        // The run ended cleanly, not on a cancellation or an error turn.
        Assert.IsType<AgentEnd>(events[^1]);
        Assert.True(wall.Elapsed < TimeSpan.FromSeconds(5),
            $"the end-to-drive run took {wall.Elapsed} real time; the follow-up poll must be driven by the virtual clock");

        // The delivery contract end-to-drive: the wake's final text posted exactly once, in the wake thread,
        // verbatim, nothing anywhere else, and the wake's ids acked so the latch holds nothing.
        var wakePost = Posts(handler).Single(p => p.Arguments["body"]!.GetValue<string>() == wakeText);
        Assert.Equal(wakeThread, wakePost.Arguments["thread"]!.GetValue<long>());
        Assert.DoesNotContain(Posts(handler), p => p.Arguments["thread"] is { } t && t.GetValue<long>() != wakeThread);
        Assert.Contains(AckIds(Acks(handler)[^1]), id => id == wakeId);

        // The run reached the follow-up poll (it does not short-circuit on turn end, since ShouldStopAfterTurn
        // returns false by design) and the loop ended on the poll's virtual idle timeout, not a wall-clock one.
        Assert.True(stream.Calls >= 1);
        Assert.Empty(chat.Unhandled);
    }

    /// <summary>The stronger half of wi-78: the follow-up poll does not merely terminate - it DRIVES. A second item
    /// delivered mid-run (while the run is parked in the follow-up poll) is read by the poll and starts a second turn
    /// through the real loop; the whole end-to-drive run (two turns, one poll) completes under the same wall-clock
    /// ceiling. This is the fact that could not be written before wi-78: it needs the poll to move on the virtual
    /// clock instead of on real wall clock (a >300s hang).</summary>
    [Fact]
    public async Task EndToDriveFollowUpPollDrivesASecondTurnMidRunUnderAWallClockCeiling()
    {
        const string wakeText = "land exactly once in the wake thread";
        const long wakeThread = 40700;
        const long wakeId = 40701;
        const long followUpThread = 40702;
        const long followUpId = 40703;

        var chat = new LatchedChat();
        // Only the wake is latched before the run starts, so the loop's first steering poll consumes exactly the wake
        // (one turn). The follow-up is delivered mid-run, so the follow-up poll reads it and drives a second turn.
        chat.Deliver(Item(wakeId, reason: "mention", thread: wakeThread, author: "team-lead-2", room: "board", body: "ship it"));
        using var handler = chat.Handler();
        using var client = Client(handler);

        var clock = new FakeClock();
        var steering = new FactorySteering(client, clock)
        {
            FollowUpWaitSeconds = 30,
            LatchPollInterval = TimeSpan.FromSeconds(5),
            IdleTimeout = TimeSpan.FromSeconds(60),
        };

        var config = new AgentLoopConfig
        {
            Model = PiSharp.Tests.Agent.Fakes.Model,
            GetSteeringMessages = () => steering.GetSteeringMessagesAsync(),
            GetFollowUpMessages = () => steering.GetFollowUpMessagesAsync(),
            PrepareNextTurn = t => steering.PrepareNextTurnAsync(t),
            ShouldStopAfterTurn = t => steering.ShouldStopAfterTurnAsync(t),
        };

        // Two scripted replies: the wake, then the follow-up the poll hands over.
        var stream = new PiSharp.Tests.Agent.ScriptedStream().Then(PiSharp.Tests.Agent.Fakes.Text(wakeText)).Then(PiSharp.Tests.Agent.Fakes.Text("follow-up answered"));

        var task = Task.Run(async () =>
        {
            var events = new List<AgentEvent>();
            await foreach (var e in AgentLoop.Run([new UserMessage("")], Context([]), config, stream.Stream))
                events.Add(e);
            return events;
        });

        // Drive the real loop on the virtual clock. After the wake turn has posted (one chat_post to the wake
        // thread) the run is parked in the follow-up poll - deliver the second item then, so the poll reads it
        // and hands it to a second turn. Without wi-78 this parks for real-time minutes instead of completing.
        var wall = System.Diagnostics.Stopwatch.StartNew();
        var delivered = false;
        List<AgentEvent> events = null!;
        for (var i = 0; i < 800 && !task.IsCompleted; i++)
        {
            if (!delivered && Posts(handler).Where(p => p.Arguments["body"] is { } b && b.GetValue<string>() == wakeText).Any())
            {
                chat.Deliver(Item(followUpId, reason: "mention", thread: followUpThread, author: "team-lead-2", room: "board", body: "also this"));
                delivered = true;
            }
            if (!await StepAsync(clock, task)) break;
        }
        Assert.True(task.IsCompleted, "the two-turn run did not finish inside the step budget");
        events = await task;
        wall.Stop();

        // Clean end, under the real-time ceiling (not a >300s hang on the poll's real-time sleep).
        Assert.IsType<AgentEnd>(events[^1]);
        Assert.True(wall.Elapsed < TimeSpan.FromSeconds(5),
            $"the two-turn end-to-drive run took {wall.Elapsed} real time; the poll must run on the virtual clock");

        // The poll drove a second turn: the model was called twice (one per turn).
        Assert.Equal(2, stream.Calls);

        // Each final text landed exactly once, in its own waking thread, verbatim - nothing fanned out.
        var wakePost = Posts(handler).Single(p => p.Arguments["body"]!.GetValue<string>() == wakeText);
        Assert.Equal(wakeThread, wakePost.Arguments["thread"]!.GetValue<long>());
        var followPost = Posts(handler).Single(p => p.Arguments["body"]!.GetValue<string>() == "follow-up answered");
        Assert.Equal(followUpThread, followPost.Arguments["thread"]!.GetValue<long>());
        Assert.Equal(2, Posts(handler).Count);

        // Both wakes are settled: nothing latched.
        Assert.Empty(chat.Unhandled);
    }

    // ---------------------------------------------------------------- roundtrip: the attached-seat delivery contract

    /// <summary>What this fact pins, against the real steering wiring of an attached seat (the very hooks the
    /// CliHost wiring installs on the loop - GetSteeringMessages, ShouldStopAfterTurn, the parked reply and the
    /// ack of the wake's ids): the steering/turn-completion contract of the attached-seat loop. A wake from the
    /// board arrives in the latch, the steering poll hands it to the turn, and when the seat's final reply text
    /// ends the turn the reply is posted exactly once - in the wake thread, verbatim, with nothing posted
    /// anywhere else - and the wake is acked, so the latch holds nothing. The uncovered half - driving the
    /// whole turn through the AgentLoop itself, including the follow-up poll and the AgentEnd path - is wi-78
    /// (FakeClock does not virtualize Task.Delay, so the idle follow-up poll loops on real wall clock in the
    /// harness). Synthetic and deterministic: LatchedChat, no model and no network.</summary>
    [Fact]
    public async Task AnAttachedSeatWakePostsItsFinalTextExactlyOnceInTheWakeThread()
    {
        const string wakeText = "land exactly once in the wake thread";
        const long wakeThread = 40700;
        const long wakeId = 40701;

        var chat = new LatchedChat();
        chat.Deliver(Item(wakeId, reason: "mention", thread: wakeThread, author: "team-lead-2", room: "board", body: "ship it"));
        using var handler = chat.Handler();
        using var client = Client(handler);
        var steering = new FactorySteering(client);

        // The turn's input: the steering poll hands the seat the wake that arrived in the latch.
        var arrived = await steering.GetSteeringMessagesAsync();
        Assert.Single(arrived);
        Assert.NotNull(steering.Current);
        Assert.Equal(wakeId, steering.Current.Message.Id);

        // The seat's final reply text ends the turn: the reply is posted and the wake's ids are acked. Should
        // Stop returns false by design - the loop keeps polling queues; the contract is that the turn
        // COMPLETES (post + ack) at turn end.
        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(Context([]), Assistant(wakeText))));

        // The delivery contract: the final text was posted exactly once, in the wake thread, verbatim, and
        // nothing was posted to any other thread or room.
        var post = Assert.Single(Posts(handler));
        Assert.Equal(wakeThread, post.Arguments["thread"]!.GetValue<long>());
        Assert.Equal(wakeText, post.Arguments["body"]!.GetValue<string>());
        Assert.DoesNotContain(Posts(handler), p => p.Arguments["thread"] is { } && p.Arguments["thread"]!.GetValue<long>() != wakeThread);

        // The wake is settled: its ids were acked and the latch holds nothing unhandled, so a redelivery of
        // the same item starts no second task and no second answer.
        Assert.Equal([wakeId], AckIds(Assert.Single(Acks(handler))));
        Assert.Empty(chat.Unhandled);
    }

    // ---------------------------------------------------------------- note deadline: transition-order

    [Fact]
    public async Task QuotaHandoverNoteFailureSkipsTransition()
    {
        var calls = new List<JsonObject>();
        using var handler = new FakeHandler(async request =>
        {
            var envelope = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!;
            var call = (JsonObject)envelope["params"]!.DeepClone();
            calls.Add(call);
            var tool = call["name"]!.GetValue<string>();
            var text = tool switch
            {
                "kb_list" => "{\"items\":[{\"slug\":\"wi-held\",\"status\":\"in_progress\"}]}",
                "kb_get" => "{\"kind\":\"work_item\",\"status\":\"in_progress\",\"links\":[{\"rel\":\"assigned_to\",\"slug\":\"coder-06-pi6\"},{\"rel\":\"<-has_role\",\"slug\":\"team-lead-2\",\"kind\":\"actor\"}]}",
                "kb_write" => "{\"error\":\"disk full\"}",
                _ => "{\"ok\":true}",
            };
            if (tool == "kb_write")
                return new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent(Rpc(text, isError: true), Encoding.UTF8, "application/json") };
            return new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(Rpc(text), Encoding.UTF8, "application/json") };
        });
        using var client = Client(handler);
        var clock = new FakeClock();
        var steering = new FactorySteering(client, clock) { Actor = "coder-06-pi6" };
        var quotaFailure = new AssistantMessage
        {
            Model = "grok", Provider = "xai", Content = [], StopReason = StopReason.Error,
            ProviderRequestFailed = true, ProviderHttpStatus = 429, RateLimitReason = "quota",
        };

        await steering.ObserveProviderResponseAsync(quotaFailure);

        // kb_write was attempted
        Assert.Contains(calls, c => c["name"]!.GetValue<string>() == "kb_write");
        // But kb_transition was NOT issued because the note failed
        Assert.DoesNotContain(calls, c => c["name"]!.GetValue<string>() == "kb_transition");
        // No HANDOVER posted
        Assert.DoesNotContain(calls, c => c["name"]!.GetValue<string>() == "chat_post");
    }

    [Fact]
    public async Task QuotaHandoverNoteSuccessPrecedesTransition()
    {
        var calls = new List<JsonObject>();
        using var handler = new FakeHandler(async request =>
        {
            var envelope = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!;
            var call = (JsonObject)envelope["params"]!.DeepClone();
            calls.Add(call);
            var tool = call["name"]!.GetValue<string>();
            var text = tool switch
            {
                "kb_list" => "{\"items\":[{\"slug\":\"wi-held\",\"status\":\"in_progress\"}]}",
                "kb_get" => "{\"kind\":\"work_item\",\"status\":\"in_progress\",\"links\":[{\"rel\":\"assigned_to\",\"slug\":\"coder-06-pi6\"},{\"rel\":\"<-has_role\",\"slug\":\"team-lead-2\",\"kind\":\"actor\"}]}",
                _ => "{\"ok\":true}",
            };
            return new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(Rpc(text), Encoding.UTF8, "application/json") };
        });
        using var client = Client(handler);
        var clock = new FakeClock();
        var steering = new FactorySteering(client, clock) { Actor = "coder-06-pi6" };
        var quotaFailure = new AssistantMessage
        {
            Model = "grok", Provider = "xai", Content = [], StopReason = StopReason.Error,
            ProviderRequestFailed = true, ProviderHttpStatus = 429, RateLimitReason = "quota",
        };

        await steering.ObserveProviderResponseAsync(quotaFailure);

        var writeIdx = calls.FindIndex(c => c["name"]!.GetValue<string>() == "kb_write");
        var transitionIdx = calls.FindIndex(c => c["name"]!.GetValue<string>() == "kb_transition");
        Assert.True(writeIdx >= 0, "kb_write was issued");
        Assert.True(transitionIdx >= 0, "kb_transition was issued");
        Assert.True(writeIdx < transitionIdx, $"note write (idx {writeIdx}) precedes transition (idx {transitionIdx})");
    }

    // ---------------------------------------------------------------- paused-rate-band fixtures (rate-bands-d)

    /// <summary>What the KB answers for the paused-band tests: the actor's rate band (kb_get on the actor slug),
    /// the held items (kb_list), each held item's current shape (kb_get on its slug), and note-write failures.</summary>
    private sealed class KbSim
    {
        public string? BandJson;                       // null: the sweep has not classified (no attrs)
        public List<string> Held = [];
        public string ItemStatus = "in_progress";
        public string AssignedTo = "coder-06-pi6";
        public bool FailNotes;
        public List<JsonObject> Calls = [];
    }

    private static FakeHandler KbHandler(KbSim sim) => new(async request =>
    {
        var envelope = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!;
        var call = (JsonObject)envelope["params"]!.DeepClone();
        sim.Calls.Add(call);
        var tool = call["name"]!.GetValue<string>();
        var slug = call["arguments"]?["slug"]?.GetValue<string>();
        var heldJson = string.Join(",", sim.Held.Select(s => $"{{\"slug\":\"{s}\",\"status\":\"in_progress\"}}"));
        var text = tool switch
        {
            "kb_list" => $"{{\"items\":[{heldJson}]}}",
            "kb_get" when slug == "coder-06-pi6" => sim.BandJson ?? "{}",
            // The team-lead role and its holder today: a handover goes to whoever holds it, never to a seat named in code.
            "kb_get" when slug == "team-lead" => "{\"kind\":\"role\",\"links\":[{\"rel\":\"<-has_role\",\"slug\":\"team-lead-2\",\"kind\":\"actor\"}]}",
            "kb_get" => $"{{\"kind\":\"work_item\",\"status\":\"{sim.ItemStatus}\",\"links\":[{{\"rel\":\"assigned_to\",\"slug\":\"{sim.AssignedTo}\"}}]}}",
            _ => sim.FailNotes && tool == "kb_write" ? "kb write failed" : "{\"ok\":true}",
        };
        var isError = sim.FailNotes && tool == "kb_write";
        return new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent(Rpc(text, isError), Encoding.UTF8, "application/json") };
    });

    private static string PausedBandJson(string since) =>
        $"{{\"attrs\":{{\"rate_band\":\"paused\",\"rate_band_since\":\"{since}\",\"rate_reset_at\":\"2026-09-14T13:00:00Z\"}}}}";

    /// <summary>One git command in <paramref name="dir"/>; throws with stderr on a nonzero exit. The tests use real
    /// local repositories (file-based origin) so the harness' push is verified against the ref it actually wrote.</summary>
    private static string Git(string dir, params string[] args)
    {
        var raw = GitRaw(dir, args);
        return raw.Trim();
    }

    /// <summary>Like <see cref="Git"/> but without trimming the stdout: the porcelain output's leading space is part
    /// of its format (" M" = worktree-dirty).</summary>
    private static string GitRaw(string dir, params string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("git failed to start");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(60_000)) { process.Kill(true); throw new InvalidOperationException("git timed out"); }
        if (process.ExitCode != 0) throw new InvalidOperationException($"git {string.Join(" ", args)} failed: {stderr.Trim()}");
        return stdout;
    }

    /// <summary>The seat's workArea for the paused-band tests: a temp directory holding one worktree per item slug,
    /// each with a file-based bare origin. Disposing removes the tree.</summary>
    private sealed class WorkArea : IDisposable
    {
        public WorkArea()
        {
            Root = Path.Combine(Path.GetTempPath(), "pisharp-paused-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }
        public string Root { get; }

        public void Dispose()
        {
            try { Directory.Delete(Root, true); } catch { /* best effort cleanup */ }
        }

        /// <summary>One item's worktree at <c>Root/&lt;slug&gt;</c>: base commit on main, pushed to the bare origin at
        /// <c>&lt;slug&gt;/origin.git</c>, then optionally a feature branch and a dirty file. Returns (worktree, bare origin).</summary>
        public (string Dir, string Origin) Repo(string slug, string? branch = "main", bool dirty = true, bool brokenOrigin = false)
        {
            var dir = Path.Combine(Root, slug);
            // The bare origin sits BESIDE the worktree: inside it, git would list it as untracked and the tree
            // would never be clean.
            var origin = Path.Combine(Root, slug + "-origin.git");
            Directory.CreateDirectory(dir);
            Git(dir, "init", "--bare", origin);
            Git(dir, "init", "-b", "main");
            Git(dir, "remote", "add", "origin", origin);
            Git(dir, "config", "user.email", "harness-test@example.com");
            Git(dir, "config", "user.name", "pisharp harness test");
            File.WriteAllText(Path.Combine(dir, "app.txt"), "base\n");
            Git(dir, "add", "-A");
            Git(dir, "commit", "-m", "base");
            Git(dir, "push", "-u", "origin", "main");
            Git(dir, "remote", "set-head", "origin", "main");
            if (branch is { Length: > 0 } and not "main")
                Git(dir, "checkout", "-b", branch);
            if (dirty)
                File.WriteAllText(Path.Combine(dir, "app.txt"), "dirty\n");
            if (brokenOrigin)
                Git(dir, "remote", "set-url", "origin", Path.Combine(Root, "gone-origin.git"));
            return (dir, origin);
        }
    }
}
