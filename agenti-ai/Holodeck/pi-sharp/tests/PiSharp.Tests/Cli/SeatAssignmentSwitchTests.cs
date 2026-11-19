using PiSharp.Cli;
using PiSharp.Core.Config;
using PiSharp.Core.Models;
using PiSharp.Core.Messages;

namespace PiSharp.Tests.Cli;

public sealed class SeatAssignmentSwitchTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static readonly ModelConfig Codex = new()
    {
        Id = "gpt-6.1-sol", BaseUrl = "https://broker", Provider = "openai-codex", ContextWindow = 400000, MaxOutputTokens = 32000,
    };
    private static readonly ModelConfig Local = new()
    {
        Id = "miniclaude", BaseUrl = "http://192.168.1.10:8080/v1", Provider = "openai-completions", ContextWindow = 100000, MaxOutputTokens = 8192,
    };

    private static SeatAssignmentSwitch OnCodex(Clock clock, List<string?> loaded, Func<SeatAssignmentAnswer> answer, ModelConfig? local = null) =>
        new(new Uri("https://broker/"), "t", null, "rev-codex", true, Codex, local ?? Local, new Settings(), clock)
        {
            Fetch = (revision, _) =>
            {
                loaded.Add(revision);
                return Task.FromResult(answer());
            },
        };

    [Fact]
    public async Task AChangedAssignmentSwitchesInPlaceAndReportsTheNewRevision()
    {
        var clock = new Clock();
        var loaded = new List<string?>();
        var seat = OnCodex(clock, loaded, () => new SeatAssignmentAnswer(true, null, SubscriptionAssignment.LocalRevision));

        Assert.True(await seat.CheckAsync(force: true, CancellationToken.None));
        Assert.Same(Local, seat.Model);
        Assert.False(seat.RunsSubscription);
        Assert.Equal(SubscriptionAssignment.LocalRevision, seat.Revision);
        Assert.Equal(1, seat.Generation);
        // The check reported what it ran, then what it runs now, so the tab shows the change as applied.
        Assert.Equal(["rev-codex", "local"], loaded);
        // The local model's window sets the limits now: 100k less its 8k output, down to half.
        var limits = seat.CompactionFor(new Settings());
        Assert.Equal(91808, limits.CompactionTriggerTokens);
        Assert.Equal(45904, limits.CompactionTargetTokens);
    }

    [Fact]
    public async Task ARouterAssignmentRunsAsALocalStyleModelWithTheFactoryTokenAndTheAssignmentsLimits()
    {
        var clock = new Clock();
        var loaded = new List<string?>();
        var routed = new ModelConfig
        {
            Id = "codestral", BaseUrl = "http://192.168.8.81:8787/v1", Provider = "openai-completions", Connection = "router",
            ContextWindow = 65536, MaxOutputTokens = 8192, CompactAtTokens = 40000,
        };
        var answer = new SeatAssignmentAnswer(false, routed, "rev-router");
        var seat = OnCodex(clock, loaded, () => answer);

        Assert.True(await seat.CheckAsync(force: true, CancellationToken.None));
        Assert.Equal("codestral", seat.Model.Id);
        Assert.Equal("t", seat.Model.ApiKey); // the seat's factory token: the router knows callers by it
        Assert.False(seat.RunsSubscription); // so the seat may call an OpenAI-style endpoint itself
        var limits = seat.CompactionFor(new Settings { CompactionTriggerTokens = 50000 });
        Assert.Equal(40000, limits.CompactionTriggerTokens); // the assignment's, not settings.json
        Assert.Equal(20000, limits.CompactionTargetTokens);

        // Back to local: the seat's own model and its own key, never the router model.
        answer = new SeatAssignmentAnswer(true, null, SubscriptionAssignment.LocalRevision);
        Assert.True(await seat.CheckAsync(force: true, CancellationToken.None));
        Assert.Same(Local, seat.Model);
    }

    [Fact]
    public void ARouterRouteThatIsTheSeatsOwnModelKeepsItsTuning()
    {
        var own = Local with { BaseUrl = "http://192.168.8.81:8787/v1/", Reasoning = true, Compat = new() { ThinkingFormat = ThinkingFormat.QwenChatTemplate } };
        var routed = new ModelConfig
        {
            Id = "miniclaude", BaseUrl = "http://192.168.8.81:8787/v1", Connection = "router", ContextWindow = 65536, MaxOutputTokens = 4096,
        };
        var model = SubscriptionAssignment.ForRouter(routed, "tok", own);
        Assert.True(model.Reasoning);
        Assert.Equal(ThinkingFormat.QwenChatTemplate, model.Compat.ThinkingFormat);
        Assert.Equal(65536, model.ContextWindow);
        Assert.False(SubscriptionAssignment.ForRouter(routed with { Id = "codestral" }, "tok", own).Reasoning);
    }

    /// <summary>host-02, 2026-10-16: a moved seat reaches the router through its relay's forward; the broker's URL is refused.</summary>
    [Fact]
    public void ARouterAssignmentGoesTheWayTheSeatsOwnRouterConnectionDoes()
    {
        var own = Local with { Provider = "openai-completions", BaseUrl = "http://127.0.0.1:18787/v1" };
        var routed = new ModelConfig
        {
            Id = "qwen3-8-flash", Provider = "openai-completions", BaseUrl = "http://192.168.8.81:8787/v1", Connection = "router",
            ContextWindow = 65536, MaxOutputTokens = 4096,
        };
        Assert.Equal("http://127.0.0.1:18787/v1", SubscriptionAssignment.ForRouter(routed, "tok", own).BaseUrl);
        Assert.Equal("http://192.168.8.81:8787/v1", SubscriptionAssignment.ForRouter(routed, "tok", null).BaseUrl);
    }

    [Fact]
    public async Task ChecksAreThrottledUnlessForced()
    {
        var clock = new Clock();
        var loaded = new List<string?>();
        var seat = OnCodex(clock, loaded, () => new SeatAssignmentAnswer(false, Codex, "rev-codex"));

        Assert.False(await seat.CheckAsync(false, CancellationToken.None));
        Assert.Empty(loaded);
        clock.Now += TimeSpan.FromSeconds(61);
        Assert.False(await seat.CheckAsync(false, CancellationToken.None));
        Assert.False(await seat.CheckAsync(false, CancellationToken.None));
        Assert.Single(loaded);
        Assert.False(await seat.CheckAsync(true, CancellationToken.None));
        Assert.Equal(2, loaded.Count);
        Assert.Equal(0, seat.Generation);
    }

    [Fact]
    public async Task ABrokerOutageKeepsTheModelAndBacksOff()
    {
        var clock = new Clock();
        var loaded = new List<string?>();
        var seat = OnCodex(clock, loaded, () => throw new HttpRequestException("down"));

        Assert.False(await seat.CheckAsync(true, CancellationToken.None));
        Assert.Same(Codex, seat.Model);
        clock.Now += TimeSpan.FromSeconds(61);
        Assert.False(await seat.CheckAsync(false, CancellationToken.None));
        Assert.Single(loaded);
        clock.Now += TimeSpan.FromMinutes(5);
        Assert.False(await seat.CheckAsync(false, CancellationToken.None));
        Assert.Equal(2, loaded.Count);
    }

    [Fact]
    public async Task ASwitchToASubscriptionModelTakesItsCompactionLimits()
    {
        var clock = new Clock();
        var loaded = new List<string?>();
        var grok = Codex with { Id = "grok-4.7", Provider = "xai", ContextWindow = 200000 };
        var seat = new SeatAssignmentSwitch(new Uri("https://broker/"), "t", null, "local", false, Local, Local, new Settings(), clock)
        {
            Fetch = (revision, _) => { loaded.Add(revision); return Task.FromResult(new SeatAssignmentAnswer(false, grok, "rev-grok")); },
        };

        Assert.True(await seat.CheckAsync(true, CancellationToken.None));
        Assert.Same(grok, seat.Model);
        Assert.True(seat.RunsSubscription);
        Assert.Equal(168000, seat.CompactionFor(new Settings()).CompactionTriggerTokens);
    }

    [Fact]
    public async Task AProfileOrSubscriptionSetsTheOutputCapAndSettingsMaxTokensDoesNotOverrideIt()
    {
        var clock = new Clock();
        var settings = new Settings { MaxTokens = 8192 }; // what every seat's settings.json carried
        var answer = new SeatAssignmentAnswer(true, null, "local-efficient") { Profile = new LocalProfile(65536, 32768) };
        var seat = new SeatAssignmentSwitch(new Uri("https://broker/"), "t", null, "local", false, Local, Local, new Settings(), clock)
        {
            Fetch = (_, _) => Task.FromResult(answer),
        };

        // Own model, no profile yet: settings.json's cap still applies.
        Assert.Equal(8192, seat.MaxTokensFor(settings));

        // The Subscriptions tab gives the local model a profile: its output wins, the request carries no override.
        Assert.True(await seat.CheckAsync(true, CancellationToken.None));
        Assert.True(seat.Profiled);
        Assert.Equal(32768, seat.Model.MaxOutputTokens);
        Assert.Null(seat.MaxTokensFor(settings));

        // A subscription model: its own output, never settings.json's.
        answer = new SeatAssignmentAnswer(false, Codex, "rev-codex");
        Assert.True(await seat.CheckAsync(true, CancellationToken.None));
        Assert.True(seat.RunsSubscription);
        Assert.Null(seat.MaxTokensFor(settings));
    }

    [Fact]
    public async Task TheAssignmentsEffortIsWhatTheSeatRunsAndClearingItReturnsToTheSeatsOwn()
    {
        var clock = new Clock();
        var answer = new SeatAssignmentAnswer(true, null, "local-medium") { Effort = ThinkingLevel.Medium };
        var seat = new SeatAssignmentSwitch(new Uri("https://broker/"), "t", null, "local-xhigh", false, Local, Local, new Settings(), clock)
        {
            Fetch = (_, _) => Task.FromResult(answer),
            Effort = ThinkingLevel.XHigh, // picked at startup
            DefaultEffort = ThinkingLevel.High, // settings.json defaultThinkingLevel
        };
        Assert.Equal(ThinkingLevel.XHigh, seat.CurrentEffort);

        Assert.True(await seat.CheckAsync(true, CancellationToken.None));
        Assert.Equal(ThinkingLevel.Medium, seat.CurrentEffort);

        // Cleared on the tab: the seat's own default again, not the startup pick.
        answer = new SeatAssignmentAnswer(true, null, "local");
        Assert.True(await seat.CheckAsync(true, CancellationToken.None));
        Assert.Equal(ThinkingLevel.High, seat.CurrentEffort);
    }

    [Fact]
    public async Task AnUnsupportedProviderOrAMissingLocalModelLeavesTheSeatWhereItIs()
    {
        var clock = new Clock();
        var loaded = new List<string?>();
        var odd = new SeatAssignmentSwitch(new Uri("https://broker/"), "t", null, "local", false, Local, Local, new Settings(), clock)
        {
            Fetch = (_, _) => Task.FromResult(new SeatAssignmentAnswer(false, Codex with { Provider = "nope" }, "rev-odd")),
        };
        Assert.False(await odd.CheckAsync(true, CancellationToken.None));
        Assert.Same(Local, odd.Model);
        Assert.Equal("local", odd.Revision);

        var noLocal = new SeatAssignmentSwitch(new Uri("https://broker/"), "t", null, "rev-codex", true, Codex, null, new Settings(), clock)
        {
            Fetch = (_, _) => Task.FromResult(new SeatAssignmentAnswer(true, null, "local")),
        };
        Assert.False(await noLocal.CheckAsync(true, CancellationToken.None));
        Assert.Same(Codex, noLocal.Model);
        Assert.Equal("rev-codex", noLocal.Revision);
    }

    // Hybrid routing (digital-scribe, 2026-10-16): a subscription seat that also runs its own model, a share of its
    // turns by the broker's share (no light-turn gear since 2026-10-20). Sized at the tab's window like every other
    // seat (2026-10-20), for the local side only when every turn runs there.

    /// <summary>A 180k assignment from the tab, as the seat runs it.</summary>
    private static readonly ModelConfig Tab = SubscriptionAssignment.WindowIsBudget(Codex with { ContextWindow = 180000 });

    private static SeatAssignmentSwitch Hybrid(Clock clock, HybridAnswer route, Func<SeatAssignmentAnswer>? answer = null) =>
        new(new Uri("https://broker/"), "t", null, "rev-codex", true, Tab, Local, new Settings(), clock)
        {
            Hybrid = route,
            Fetch = (_, _) => Task.FromResult(answer?.Invoke() ?? new SeatAssignmentAnswer(false, Tab, "rev-codex") { Hybrid = route }),
        };

    [Fact]
    public void AHybridSeatRunsItsBandsShareLocallyInRunsOfTurns()
    {
        var seat = Hybrid(new Clock(), new HybridAnswer(50, "low", new LocalProfile(60000, 8192)));
        var sides = new List<bool>();
        for (var i = 0; i < 12; i++) { seat.PickTurn(20000); sides.Add(!seat.RunsSubscription); }
        Assert.Equal(6, seat.LocalTurns);
        Assert.Equal(6, seat.SubscriptionTurns);
        // Runs of three turns, not every other turn: each switch cools both prompt caches.
        Assert.Equal([false, false, false, true, true, true, false, false, false, true, true, true], sides);
        Assert.Equal("miniclaude", seat.Model.Id);
        // The local side compacts where the subscription does, lower only as its own window requires: 100k less 8k.
        Assert.Equal(91808, seat.Model.CompactAtTokens);

        var seventy = Hybrid(new Clock(), new HybridAnswer(70, "critical", null));
        for (var i = 0; i < 30; i++) seventy.PickTurn(20000);
        Assert.Equal(21, seventy.LocalTurns);
    }

    [Fact]
    public void AProviderFailureRunsAHybridSeatsTurnsOnItsOwnModelForAWhile_ThenTheSubscriptionAgain()
    {
        // 2026-10-25: Grok "at capacity due to high demand" for over twenty minutes; four hybrid seats waited on it.
        var clock = new Clock();
        var log = new List<string>();
        var seat = new SeatAssignmentSwitch(new Uri("https://broker/"), "t", null, "rev-codex", true, Tab, Local, new Settings(), clock)
        {
            Hybrid = new HybridAnswer(0, "ok", null),
            Log = log.Add,
        };
        Assert.True(seat.CanHandBack);
        Assert.False(seat.TakeHandBack());

        seat.NoteProviderTrouble("server_error");
        Assert.True(seat.ProviderTroubled);
        Assert.Contains("failed a turn (server_error); turns run on the seat's own model until 09:05Z", log[^1]);
        Assert.True(seat.TakeHandBack());
        Assert.False(seat.TakeHandBack()); // once per hand-back: a failure of the seat's own model is a plain model error
        Assert.True(seat.PickTurn(20000));
        Assert.False(seat.RunsSubscription);
        Assert.False(seat.CanHandBack); // on its own model there is nowhere else to go
        Assert.False(seat.PickTurn(20000));
        Assert.True(seat.PickTurn(95000)); // too big for its own model: back to the subscription, troubled or not
        Assert.True(seat.RunsSubscription);

        clock.Now = clock.Now.AddMinutes(6);
        Assert.False(seat.ProviderTroubled);
        seat.PickTurn(20000);
        Assert.True(seat.RunsSubscription);
        Assert.Contains("trying openai-codex gpt-6.1-sol again", log[^1]);
    }

    /// <summary>digital-scribe, 2026-10-29: "no more rate shaping but we keep the hybrid models mix since cheap calls still
    /// can run on local models... at 15% we switch hard to just local models".</summary>
    [Fact]
    public void ACheapLocalSeatRunsItsRoutineTurnsLocally_EveryOtherTurnOnTheSubscription_AndAllLocallyAtTheReserve()
    {
        var cheap = new HybridAnswer(0, "ok", null) { Pick = SubscriptionAssignment.CheapLocalPick, FollowupMaxTokens = 4000 };
        Assert.True(cheap.PicksByKind);
        Assert.False(cheap.IsPair);
        var seat = Hybrid(new Clock(), cheap);
        seat.PickTurn(20000, kind: TurnKind.TaskStart);
        Assert.True(seat.RunsSubscription); // a task's first turn: the subscription
        Assert.True(seat.PickTurn(20000, kind: TurnKind.Routine));
        Assert.False(seat.RunsSubscription);
        Assert.Equal(Local.Id, seat.Model.Id);
        Assert.True(seat.PickTurn(20000, kind: TurnKind.Work)); // a file changed: the subscription
        Assert.True(seat.RunsSubscription);
        seat.PickTurn(20000, kind: TurnKind.Failure);
        Assert.True(seat.RunsSubscription);
        seat.PickTurn(20000, kind: TurnKind.Unknown);
        Assert.True(seat.RunsSubscription);
        Assert.True(seat.PickTurn(20000, watch: true)); // the digest: local
        Assert.False(seat.RunsSubscription);
        seat.PickTurn(95000, kind: TurnKind.Routine); // too big for the local side
        Assert.True(seat.RunsSubscription);
        Assert.Equal(2, seat.LocalTurns);
        Assert.Equal(5, seat.SubscriptionTurns);

        // Only the reserve left: the broker sends a share of 100 and every turn that fits runs locally, work too.
        var spent = Hybrid(new Clock(), cheap with { LocalShare = 100 });
        spent.PickTurn(20000, kind: TurnKind.Work);
        Assert.False(spent.RunsSubscription);
    }

    /// <summary>digital-scribe, 2026-10-29: "as a fallback... if local models fail repeatedly... and the reserve of 15% is still
    /// safe, we can use openai sub down to 10% max". team-lead-2's routine turns went to a one-slot local model and waited on
    /// it for forty minutes.</summary>
    [Fact]
    public void ALocalModelThatFailsTwiceInARowGivesItsTurnsToTheSubscriptionForTenMinutes_BelowTheReserveTooButNotPastTheFloor()
    {
        var clock = new Clock();
        var log = new List<string>();
        var fellBack = new List<(string Local, string Code, DateTimeOffset Until)>();
        var cheap = new HybridAnswer(0, "ok", null) { Pick = SubscriptionAssignment.CheapLocalPick, FollowupMaxTokens = 4000 };
        var seat = new SeatAssignmentSwitch(new Uri("https://broker/"), "t", null, "rev-codex", true, Tab, Local, new Settings(), clock)
        {
            Hybrid = cheap, Log = log.Add, OnFallback = (local, code, until) => fellBack.Add((local, code, until)),
        };
        seat.PickTurn(20000, kind: TurnKind.Routine);
        Assert.False(seat.RunsSubscription);
        Assert.True(seat.CanFailOver);

        seat.NoteLocalTrouble("http_503");
        Assert.False(seat.LocalTroubled); // once can be a hiccup
        Assert.False(seat.TakeFallback());
        Assert.Contains("failed a turn (http_503); 1 more in a row", log[^1]);
        seat.NoteLocalAnswer();
        seat.NoteLocalTrouble("http_503");
        Assert.False(seat.LocalTroubled); // an answer in between starts the count over

        seat.NoteLocalTrouble("stream_broke_off");
        Assert.True(seat.LocalTroubled);
        Assert.Contains("failed 2 turns in a row (stream_broke_off); turns run on openai-codex gpt-6.1-sol until 09:10Z", log[^1]);
        Assert.Equal((Local.Id, "stream_broke_off", clock.Now.AddMinutes(10)), Assert.Single(fellBack));
        Assert.True(seat.TakeFallback());
        Assert.False(seat.TakeFallback());
        Assert.True(seat.PickTurn(20000, kind: TurnKind.Routine)); // a routine turn: the subscription now
        Assert.True(seat.RunsSubscription);
        Assert.False(seat.CanFailOver); // on the subscription there is nothing to fail over from

        clock.Now = clock.Now.AddMinutes(11);
        Assert.False(seat.LocalTroubled);
        Assert.True(seat.PickTurn(20000, kind: TurnKind.Routine));
        Assert.False(seat.RunsSubscription);
        Assert.Contains("trying the local model miniclaude again", log[^1]);

        // Only the reserve left (share 100): the subscription still takes the failed local model's turns while the broker
        // says it is above its fallback floor, and never once it says it is not.
        var reserve = new SeatAssignmentSwitch(new Uri("https://broker/"), "t", null, "rev-codex", true, Tab, Local, new Settings(), clock)
        {
            Hybrid = cheap with { LocalShare = 100, Fallback = true },
        };
        reserve.PickTurn(20000, kind: TurnKind.Work);
        Assert.False(reserve.RunsSubscription);
        Assert.True(reserve.CanFailOver);
        reserve.NoteLocalTrouble("http_503");
        reserve.NoteLocalTrouble("http_503");
        reserve.PickTurn(20000, kind: TurnKind.Work);
        Assert.True(reserve.RunsSubscription);

        var floor = new SeatAssignmentSwitch(new Uri("https://broker/"), "t", null, "rev-codex", true, Tab, Local, new Settings(), clock)
        {
            Hybrid = cheap with { LocalShare = 100, Fallback = false },
        };
        floor.PickTurn(20000, kind: TurnKind.Work);
        Assert.False(floor.CanFailOver);
        floor.NoteLocalTrouble("http_503");
        floor.NoteLocalTrouble("http_503");
        floor.PickTurn(20000, kind: TurnKind.Work);
        Assert.False(floor.RunsSubscription);

        // A broker that does not say: only while the pace still gives the subscription turns.
        Assert.True((cheap with { Fallback = null }).FallbackAllowed);
        Assert.False((cheap with { LocalShare = 100, Fallback = null }).FallbackAllowed);
        // A local pair has no subscription to go to.
        var pair = new HybridAnswer(0, "pair", null) { Pick = SubscriptionAssignment.TurnKindPick, LocalModel = "miniclaude" };
        Assert.True(pair.IsPair);
    }

    [Fact]
    public void TheBrokersFallbackWordIsRead()
    {
        static HybridAnswer? Of(string? word)
        {
            using var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
            response.Headers.Add(SubscriptionAssignment.HybridShareHeader, "100");
            if (word is not null) response.Headers.Add(SubscriptionAssignment.HybridFallbackHeader, word);
            return SubscriptionAssignment.HybridOf(response);
        }
        Assert.True(Of("on")!.Fallback);
        Assert.False(Of("off")!.Fallback);
        Assert.Null(Of(null)!.Fallback);
        Assert.Null(Of("maybe")!.Fallback);
    }

    [Fact]
    public void AWatchTurnRunsOnTheSeatsOwnModelOutsideTheShare_AndActingGoesBackToTheShare()
    {
        // Operating-model plan, item 11: the hourly digest is read on the seat's own model; acting moves it to the paid one.
        var seat = Hybrid(new Clock(), new HybridAnswer(0, "ok", null));
        Assert.True(seat.PickTurn(20000, watch: true));
        Assert.False(seat.RunsSubscription);
        Assert.True(seat.PickTurn(20000));
        Assert.True(seat.RunsSubscription);
        Assert.False(seat.PickTurn(95000, watch: true)); // too big for the local side: stays on the subscription
        Assert.True(seat.RunsSubscription);

        Assert.True(CliHost.IsDigest([new UserMessage("[cubicle steering from @factory-admin] DIGEST for team-lead-2, 2026-10-24 14:00Z: what needs you this hour.")]));
        Assert.False(CliHost.IsDigest([new UserMessage("FREE coder-06-pi4 ...")]));
    }

    [Fact]
    public void TheShareAloneDecidesALightTurnNoLongerGoesLocalAndPausedRunsEverythingLocally()
    {
        // digital-scribe, 2026-10-20: "why aren't the agents use more GPT?!" - coordination turns went local whatever the share.
        var seat = Hybrid(new Clock(), new HybridAnswer(0, "ok", null));
        for (var i = 0; i < 9; i++) Assert.False(seat.PickTurn(20000));
        Assert.True(seat.RunsSubscription);
        Assert.Same(Tab, seat.Model);
        Assert.Equal(0, seat.LocalTurns);

        // Too big for the local model (its 100k window less its 8k output): a local run's turn stays on the subscription.
        var half = Hybrid(new Clock(), new HybridAnswer(50, "low", null));
        for (var i = 0; i < 3; i++) half.PickTurn(20000);
        Assert.True(half.RunsSubscription);
        Assert.False(half.PickTurn(95000));
        Assert.True(half.RunsSubscription);

        // The local side's wall is its compaction point (100k less its 8k output): a turn above it stays on the
        // subscription. The subscription's larger output no longer narrows it (it did to 68k until 2026-10-20): each
        // request asks for no more than its window leaves, and the reserve stays inside the room above that point, so the
        // session never compacts a local turn for the local model's sake (pi3 compacted at ~67k, 2026-10-20).
        var run = Hybrid(new Clock(), new HybridAnswer(50, "low", null));
        for (var i = 0; i < 3; i++) run.PickTurn(20000);   // a subscription run
        Assert.False(run.PickTurn(92000));                 // a local run, but 92k does not fit
        Assert.True(run.RunsSubscription);
        Assert.True(run.PickTurn(91000));                  // 91k does
        Assert.False(run.RunsSubscription);
        var limits = run.CompactionFor(new Settings()).ToCompactionSettings();
        Assert.Equal(180000, limits.TriggerTokens);
        Assert.False(PiSharp.Core.Compaction.Compactor.ShouldCompact(91000, run.Model, limits));

        var paused = Hybrid(new Clock(), new HybridAnswer(100, "paused", null));
        Assert.True(paused.PickTurn(95000));
        Assert.False(paused.RunsSubscription);
        Assert.Equal(1, paused.LocalTurns);
    }

    [Fact]
    public async Task AHybridSeatSizesAtTheTabsWindowUntilEveryTurnRunsLocallyAndTheBandArrivesWithoutANewRevision()
    {
        var route = new HybridAnswer(0, "ok", new LocalProfile(60000, 8192));
        var seat = Hybrid(new Clock(), route, () => new SeatAssignmentAnswer(false, Tab, "rev-codex") { Hybrid = route });
        // 180k down to 90k, like every other seat; the local profile's 60k budget no longer sizes it.
        Assert.Equal(180000, seat.CompactionFor(new Settings()).CompactionTriggerTokens);

        route = new HybridAnswer(50, "low", new LocalProfile(60000, 8192));
        Assert.True(await seat.CheckAsync(force: true, CancellationToken.None));
        Assert.Equal("low", seat.Hybrid!.Band);
        Assert.Equal("rev-codex", seat.Revision);
        var limits = seat.CompactionFor(new Settings());
        Assert.Equal(180000, limits.CompactionTriggerTokens);
        Assert.Equal(90000, limits.CompactionTargetTokens);
        Assert.Same(Tab, seat.Model);

        // Every turn local: the seat sizes for the local side, which must hold every turn (100k less its 8k output).
        route = new HybridAnswer(100, "paused", new LocalProfile(60000, 8192));
        Assert.True(await seat.CheckAsync(force: true, CancellationToken.None));
        Assert.Equal(91808, seat.CompactionFor(new Settings()).CompactionTriggerTokens);
    }

    // pi13, 2026-10-27: the seat started on xai with no hybrid part, its Grok allowance was spent, and every turn went to xai
    // again; the hybrid route the broker sent afterwards (same revision) was ignored because the seat had none to compare.
    [Fact]
    public async Task ASubscriptionSeatThatStartedWithoutItsHybridRouteTakesItWhenItArrives()
    {
        var route = new HybridAnswer(100, "paused", null);
        var seat = new SeatAssignmentSwitch(new Uri("https://broker/"), "t", null, "rev-xai", true, Tab, Local, new Settings(), new Clock())
        {
            Fetch = (_, _) => Task.FromResult(new SeatAssignmentAnswer(false, Tab, "rev-xai") { Hybrid = route }),
        };
        Assert.False(seat.CanHandBack);
        Assert.False(seat.PickTurn(50000));
        Assert.True(seat.RunsSubscription);

        Assert.True(await seat.CheckAsync(force: true, CancellationToken.None));
        Assert.Equal("paused", seat.Hybrid!.Band);
        Assert.True(seat.PickTurn(50000));
        Assert.False(seat.RunsSubscription);
        Assert.Equal(1, seat.LocalTurns);
    }

    [Fact]
    public async Task ASeatRunningLocallyResizesWhenTheBrokerSendsTheLocalModelsRealWindow()
    {
        // 2026-10-20: every turn local on miniclaude, sized by the seat's installed 100k: 100,000 - 32,768 = 67,232. The
        // broker then sends the window the router serves it with (200k); same band, same share, and the seat re-sizes.
        // The local profile writes 16k (the broker's efficient local row).
        var route = new HybridAnswer(100, "critical", new LocalProfile(65536, 16384)) { LocalModel = "miniclaude" };
        var seat = Hybrid(new Clock(), route, () => new SeatAssignmentAnswer(false, Tab, "rev-codex") { Hybrid = route });
        Assert.Equal(100000 - 16384, seat.CompactionFor(new Settings()).CompactionTriggerTokens);

        route = new HybridAnswer(100, "critical", new LocalProfile(65536, 16384)) { LocalModel = "miniclaude", LocalWindow = 200000 };
        var generation = seat.Generation;
        Assert.True(await seat.CheckAsync(force: true, CancellationToken.None));
        Assert.Equal(generation + 1, seat.Generation);
        Assert.Equal(180000, seat.CompactionFor(new Settings()).CompactionTriggerTokens); // the tab's 180k, as every seat
    }

    /// <summary>digital-scribe, 2026-10-20: the compaction point comes from the Subscriptions tab (180k down to 90k for
    /// every seat, local turns included), the window from the model server, the output from the profile; each request
    /// asks for no more output than its window leaves. A 200k local model writing up to 131,072 compacts at 180k.</summary>
    [Fact]
    public void AHybridSeatRunningEveryTurnLocallyOnALargeWindowCompactsAtTheTabsPoint()
    {
        var seat = Hybrid(new Clock(), new HybridAnswer(100, "paused", new LocalProfile(65536, 131072)) { LocalModel = "miniclaude", LocalWindow = 200000 });
        Assert.True(seat.PickTurn(170000));
        Assert.Equal(200000, seat.Model.ContextWindow);
        Assert.Equal(131072, seat.Model.MaxOutputTokens);
        var settings = seat.CompactionFor(new Settings());
        Assert.Equal(180000, settings.CompactionTriggerTokens);
        Assert.Equal(90000, settings.CompactionTargetTokens);
        var compaction = settings.ToCompactionSettings();
        Assert.False(PiSharp.Core.Compaction.Compactor.ShouldCompact(180000, seat.Model, compaction));
        Assert.True(PiSharp.Core.Compaction.Compactor.ShouldCompact(180001, seat.Model, compaction));
    }

    [Fact]
    public void AHybridSeatSizesForTheTabsWindowWhicheverSideRunsTheTurn()
    {
        var ok = Hybrid(new Clock(), new HybridAnswer(0, "ok", new LocalProfile(60000, 8192)));
        Assert.Equal("gpt-6.1-sol", ok.SizingModel!.Id);
        Assert.Equal(180000, ok.SizingModel.CompactAtTokens); // the gauge shows the tab's 180k as the window
        Assert.Equal(180000, ok.CompactionFor(new Settings()).CompactionTriggerTokens);
        Assert.False(ok.PickTurn(20000));                      // share 0: the subscription takes the turn
        Assert.True(ok.RunsSubscription);
        Assert.Equal(180000, ok.CompactionFor(new Settings()).CompactionTriggerTokens);
        Assert.Null(OnCodex(new Clock(), [], () => new SeatAssignmentAnswer(false, Codex, "rev-codex")).SizingModel);
    }

    [Fact]
    public void APickedLocalModelRunsOverTheSeatsOwnRouterConnection()
    {
        var seat = Hybrid(new Clock(), new HybridAnswer(100, "paused", new LocalProfile(40000, 8192)) { LocalModel = "qwen3-8-flash", LocalWindow = 65536 });
        Assert.True(seat.PickTurn(10000));
        Assert.Equal("qwen3-8-flash", seat.Model.Id);
        Assert.Equal(Local.BaseUrl, seat.Model.BaseUrl);   // the seat's own router base (and key), only the model changes
        Assert.Equal(65536, seat.Model.ContextWindow);
        // Every turn local, on a 64k model: it compacts where that window still holds the 8k output.
        Assert.Equal(65536 - 8192, seat.CompactionFor(new Settings()).CompactionTriggerTokens);
    }

    [Fact]
    public async Task ASeatSwitchedToItsOwnModelWithAProfileAppliesItClampedToItsWindow()
    {
        var local = new ModelConfig { Id = "qwen", BaseUrl = "http://local/v1", Provider = "openai-completions", ContextWindow = 131_072, MaxOutputTokens = 8192 };
        var codex = new ModelConfig { Id = "gpt-6.1-sol", BaseUrl = "https://broker", Provider = "openai-codex", ContextWindow = 400_000, MaxOutputTokens = 32_000 };
        var handSet = new Settings { CompactionTriggerTokens = 50_000 };
        var answer = new SeatAssignmentAnswer(true, null, "local0123456789ab") { Profile = new(262_144, 65_536) };
        var seat = new SeatAssignmentSwitch(new Uri("https://broker/"), "t", null, "rev", true, codex, local, handSet)
        { Fetch = (_, _) => Task.FromResult(answer) };
        Assert.True(await seat.CheckAsync(force: true, CancellationToken.None));
        Assert.False(seat.RunsSubscription);
        Assert.Equal(65_536, seat.Model.MaxOutputTokens);
        Assert.Equal(131_072 - 65_536, seat.Model.CompactAtTokens);
        // The profile wins over settings.json's hand-set trigger.
        Assert.Equal(65_536, seat.CompactionFor(handSet).CompactionTriggerTokens);

        // Back to plain local: the seat's own model and its own settings again.
        answer = new SeatAssignmentAnswer(true, null, "local");
        Assert.True(await seat.CheckAsync(force: true, CancellationToken.None));
        Assert.Same(local, seat.Model);
        Assert.Equal(50_000, seat.CompactionFor(handSet).CompactionTriggerTokens);
    }
}
