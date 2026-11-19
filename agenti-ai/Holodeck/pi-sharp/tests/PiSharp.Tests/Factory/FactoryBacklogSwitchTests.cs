// Backlog mode (settings factory.freshContext = "backlog"): the team lead and product owner seats keep one context
// per backlog. Same fixtures as the rest of FactorySteeringTests (LatchedChat, Item, Turn), so this is a partial.
using System.Text.Json.Nodes;
using PiSharp.Core.Agent;
using PiSharp.Core.Config;
using PiSharp.Core.Factory;
using PiSharp.Core.Messages;

namespace PiSharp.Tests.Factory;

public partial class FactorySteeringTests
{
    private static readonly FactoryBacklog[] TwoBacklogs =
    [
        new("pi-sharp", ["pi-sharp"], ["pi-sharp"]),
        new("ai-factory", ["ai-factory", "ai-factory-chat"], ["ai-factory"]),
    ];

    /// <summary>A KB where wi-99 belongs to ai-factory and wi-7 is part of feat-x, which belongs to pi-sharp.</summary>
    private static readonly Dictionary<string, string[]> KbGraph = new(StringComparer.Ordinal)
    {
        ["wi-99"] = ["ai-factory"],
        ["wi-7"] = ["feat-x"],
        ["feat-x"] = ["pi-sharp"],
        ["wi-lost"] = [],
    };

    private static FactorySteering BacklogSteering(FactoryClient client, List<string>? lines = null, List<string>? lookups = null) =>
        new(client)
        {
            Backlogs = TwoBacklogs,
            KbParents = (slug, _) =>
            {
                lock (KbGraph) lookups?.Add(slug);
                return Task.FromResult<IReadOnlyList<string>?>(KbGraph.TryGetValue(slug, out var parents) ? parents : null);
            },
            Diagnostics = lines is null ? null : lines.Add,
        };

    private static JsonObject About(JsonObject item, params string[] slugs)
    {
        item["message"]!["about"] = new JsonArray([.. slugs.Select(s => (JsonNode)JsonValue.Create(s))]);
        return item;
    }

    [Fact]
    public async Task BacklogModeClassifiesByAboutProductWorkItemAndRoom()
    {
        var chat = new LatchedChat();
        using var handler = chat.Handler();
        using var client = Client(handler);
        var lookups = new List<string>();
        var steering = BacklogSteering(client, lookups: lookups);

        chat.Deliver(Item(10, room: "pi-sharp", body: "task a"));
        var first = await steering.GetSteeringMessagesAsync();
        Assert.Equal("pi-sharp", steering.CurrentBacklog);
        Assert.Equal(2, first.Count);
        Assert.StartsWith("[backlog pi-sharp]", Text(first[0]));
        Assert.Contains("message 10", Text(first[1]));

        chat.Deliver(
            About(Item(11), "ai-factory-chat"),              // product slug
            About(Item(12), "wi-99"),                        // work item -> product
            Item(13, room: "ai-factory"),                    // room
            About(Item(14), "wi-7"),                         // work item -> feature -> pi-sharp (current)
            About(Item(15, room: "general"), "wi-lost"));    // nothing leads anywhere: unclassified
        var second = await steering.GetSteeringMessagesAsync();

        Assert.Equal(new long[] { 11, 12, 13 }, steering.QueuedIds);
        Assert.Equal(2, second.Count);
        Assert.Contains("message 14", Text(second[0]));
        Assert.Contains("message 15", Text(second[1]));
        Assert.Equal("pi-sharp", steering.CurrentBacklog);

        // The latch repeats the queued items; they are neither re-queued nor looked up again.
        Assert.Empty(await steering.GetSteeringMessagesAsync());
        Assert.Equal(new long[] { 11, 12, 13 }, steering.QueuedIds);
        Assert.Equal(lookups.Distinct().Count(), lookups.Count);
    }

    [Fact]
    public async Task BacklogModeUnclassifiedItemsJoinTheCurrentBacklogAndKeepTheContext()
    {
        var chat = new LatchedChat();
        using var handler = chat.Handler();
        using var client = Client(handler);
        var steering = BacklogSteering(client);
        var context = Context([]);

        chat.Deliver(Item(10, room: "ai-factory", body: "task a"));
        Assert.Equal(2, (await steering.GetSteeringMessagesAsync()).Count);
        Assert.Null(await steering.PrepareNextTurnAsync(Turn(context, Assistant("on it"))));
        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(context, Assistant("task a delivered"))));

        chat.Deliver(Item(20, reason: "dm", room: "dm", author: "digital-scribe", body: "how is it going?"));
        var arrival = await steering.GetSteeringMessagesAsync();

        Assert.Single(arrival);
        Assert.Contains("message 20", Text(arrival[0]));
        Assert.Empty(steering.QueuedIds);
        Assert.Equal("ai-factory", steering.CurrentBacklog);
        // Same backlog: the arrival joins the context, however finished the previous task was.
        Assert.Null(await steering.PrepareNextTurnAsync(Turn(context, Assistant("task a delivered"))));
    }

    [Fact]
    public async Task BacklogModeDrainsTheCurrentBacklogBeforeItSwitches()
    {
        var chat = new LatchedChat();
        using var handler = chat.Handler();
        using var client = Client(handler);
        var lines = new List<string>();
        var steering = BacklogSteering(client, lines);
        var context = Context([]);

        chat.Deliver(Item(10, room: "pi-sharp", body: "task a"));
        Assert.Equal(2, (await steering.GetSteeringMessagesAsync()).Count);

        // Another backlog's item arrives mid-task: it waits, unacked.
        chat.Deliver(Item(20, room: "ai-factory", body: "task b"));
        Assert.Empty(await steering.GetSteeringMessagesAsync());
        Assert.Equal(new long[] { 20 }, steering.QueuedIds);
        Assert.Contains(lines, l => l.StartsWith("factory: message 20 for backlog ai-factory queued while on backlog pi-sharp", StringComparison.Ordinal));

        // Still calling tools: no switch.
        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(context, Assistant("working", toolCall: true))));
        Assert.Empty(await steering.GetSteeringMessagesAsync());

        // More work on the current backlog is delivered first, even though it arrived later.
        chat.Deliver(Item(11, room: "pi-sharp", body: "task a2"));
        var more = await steering.GetSteeringMessagesAsync();
        Assert.Single(more);
        Assert.Contains("message 11", Text(more[0]));

        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(context, Assistant("a and a2 done"))));
        Assert.DoesNotContain(20L, Acks(handler).SelectMany(AckIds));
        Assert.Contains(20L, chat.Unhandled);

        // Delivered and idle: one save turn first, the queued item still waiting.
        var save = await steering.GetSteeringMessagesAsync();
        Assert.Single(save);
        Assert.StartsWith("[backlog switch]", Text(save[0]));
        Assert.Contains("id=backlog:pi-sharp", Text(save[0]));
        Assert.Contains("NO_REPLY", Text(save[0]));
        Assert.Equal(new long[] { 20 }, steering.QueuedIds);
        Assert.Equal("pi-sharp", steering.CurrentBacklog);
    }

    [Fact]
    public async Task BacklogModeNeverSwitchesWhileTheSaveTurnIsStillCallingTools()
    {
        var chat = new LatchedChat();
        using var handler = chat.Handler();
        using var client = Client(handler);
        var steering = BacklogSteering(client);
        var context = Context([]);

        chat.Deliver(Item(10, room: "pi-sharp"));
        await steering.GetSteeringMessagesAsync();
        chat.Deliver(Item(20, room: "ai-factory"));
        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(context, Assistant("done"))));
        Assert.StartsWith("[backlog switch]", Text(Assert.Single(await steering.GetSteeringMessagesAsync())));

        // The save turn writes the notebook: a tool call, so nothing is released and nothing is cleared.
        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(context, Assistant("saving", toolCall: true))));
        Assert.Empty(await steering.GetSteeringMessagesAsync());
        Assert.Null(await steering.PrepareNextTurnAsync(Turn(context, Assistant("saving", toolCall: true))));
        Assert.Equal("pi-sharp", steering.CurrentBacklog);
        Assert.Equal(new long[] { 20 }, steering.QueuedIds);
    }

    [Fact]
    public async Task BacklogModeSavesClearsAndRestoresOnSwitch()
    {
        using var disk = new PiSharp.Tests.Tools.TempWorkspace();
        using var memory = new PiSharp.Core.Notebook.NotebookStore(disk.Path, Self);
        memory.Put("backlog:ai-factory", "note", "wi-12 waits on the architect", "backlog:ai-factory", "open", 0);
        var chat = new LatchedChat();
        using var handler = chat.Handler();
        using var client = Client(handler);
        var lines = new List<string>();
        var steering = BacklogSteering(client, lines);
        steering.Notebook = memory;
        var tools = new List<IAgentTool>();
        var context = Context(tools);

        chat.Deliver(Item(10, room: "pi-sharp", body: "task a"));
        var first = await steering.GetSteeringMessagesAsync();
        Assert.Contains("no saved notes", Text(first[0]));
        chat.Deliver(Item(20, room: "ai-factory", body: "task b"), About(Item(21, thread: 20, body: "task b, more"), "wi-99"));
        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(context, Assistant("task a delivered"))));
        var postsBefore = Posts(handler).Count;

        // Save turn: the model writes its notes, then ends with NO_REPLY, which posts nothing.
        Assert.StartsWith("[backlog switch]", Text(Assert.Single(await steering.GetSteeringMessagesAsync())));
        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(context, Assistant("", toolCall: true))));
        memory.Put("backlog:pi-sharp", "note", "task a shipped; wi-3 next", "backlog:pi-sharp", "open", 0);
        Assert.Empty(await steering.GetSteeringMessagesAsync());
        var saved = Assistant("NO_REPLY");
        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(context, saved)));
        Assert.Equal(postsBefore, Posts(handler).Count);

        // Restore: the arrival note with the saved notes first, then the queued items, as one arrival.
        var restore = await steering.GetSteeringMessagesAsync();
        Assert.Equal(3, restore.Count);
        Assert.StartsWith("[backlog ai-factory]", Text(restore[0]));
        Assert.Contains("wi-12 waits on the architect", Text(restore[0]));
        Assert.Contains("left backlog pi-sharp", Text(restore[0]));
        Assert.Contains("message 20", Text(restore[1]));
        Assert.Contains("message 21", Text(restore[2]));
        Assert.Equal("ai-factory", steering.CurrentBacklog);
        Assert.Empty(steering.QueuedIds);

        // Clear: same system prompt and tools, no messages.
        var update = await steering.PrepareNextTurnAsync(Turn(context, saved));
        Assert.NotNull(update?.Context);
        Assert.Empty(update.Context.Messages);
        Assert.Same(tools, update.Context.Tools);
        Assert.Equal(context.SystemPrompt, update.Context.SystemPrompt);
        Assert.Contains(lines, l => l.StartsWith("factory: fresh context for backlog ai-factory (switch 1, left pi-sharp; 2 queued item(s) delivered)", StringComparison.Ordinal));

        // The released items are a task like any other: answered once, at the waking thread, then acked.
        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(update.Context, Assistant("task b delivered"))));
        Assert.Contains(Acks(handler).SelectMany(AckIds), id => id == 20);
        Assert.Contains(Acks(handler).SelectMany(AckIds), id => id == 21);

        // And back: the pi-sharp notes come home with the next pi-sharp item.
        chat.Deliver(Item(30, room: "pi-sharp", body: "task c"));
        var saveAgain = Text(Assert.Single(await steering.GetSteeringMessagesAsync()));
        Assert.StartsWith("[backlog switch]", saveAgain);
        Assert.Contains("id=backlog:ai-factory", saveAgain);
        Assert.False(await steering.ShouldStopAfterTurnAsync(Turn(context, Assistant("NO_REPLY"))));
        var back = await steering.GetSteeringMessagesAsync();
        Assert.StartsWith("[backlog pi-sharp]", Text(back[0]));
        Assert.Contains("task a shipped; wi-3 next", Text(back[0]));
        Assert.Contains("message 30", Text(back[1]));
    }

    [Fact]
    public async Task KbParentsReadsBelongsToAndPartOfLinks()
    {
        using var handler = ByTool(tool => tool == "kb_get"
            ? """{"slug":"wi-12","kind":"work_item","links":[{"rel":"belongs_to","slug":"ai-factory","kind":"product"},{"rel":"<-blocks","slug":"wi-13"},{"rel":"part_of","slug":"epic-3"},{"rel":"assigned_to","slug":"coder-06-pi5"}]}"""
            : "{}");
        using var client = Client(handler);

        Assert.Equal(new[] { "ai-factory", "epic-3" }, await client.KbParentsAsync("wi-12"));
        var request = Assert.Single(Snapshot(handler));
        Assert.Equal("kb_get", request.Tool);
    }

    [Fact]
    public void BacklogSettingsParse()
    {
        var s = SettingsLoader.Parse("""
            {"factory":{"freshContext":"backlog","backlogs":[{"name":"pi-sharp","products":["pi-sharp"],"rooms":["pi-sharp"]},{"name":"ai-factory","products":["ai-factory","ai-factory-chat"],"rooms":["ai-factory"]},{"products":["nameless"]}]}}
            """);
        var backlogs = s.Factory!.Backlogs!;
        Assert.Equal("backlog", s.Factory.FreshContext);
        Assert.Equal(new[] { "pi-sharp", "ai-factory" }, backlogs.Select(b => b.Name));
        Assert.Equal(new[] { "ai-factory", "ai-factory-chat" }, backlogs[1].Products);
        Assert.Equal(new[] { "ai-factory" }, backlogs[1].Rooms);
        Assert.Equal(backlogs, FactorySettings.Merge(s.Factory, new FactorySettings { Actor = "team-lead-2" })!.Backlogs);
    }
}
