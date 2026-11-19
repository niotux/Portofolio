// The seat's side of the subscription broker's GET /instance: the model it is assigned, or its own local model,
// and the assignment revision, so an idle seat can tell when it must restart to load a new assignment.
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using PiSharp.Core.Config;
using PiSharp.Core.Json;
using PiSharp.Core.Models;
using PiSharp.Llm;

namespace PiSharp.Cli;

/// <summary>What the broker answered. <c>Model</c> null with <c>Local</c> true = run the seat's own model.</summary>
internal sealed record SeatAssignmentAnswer(bool Local, ModelConfig? Model, string Revision)
{
    /// <summary>A profile for the seat's own model: where it compacts and the most it writes per turn.</summary>
    public LocalProfile? Profile { get; init; }
    /// <summary>The reasoning effort the Subscriptions tab picked for this seat when it assigned the model; null = the
    /// seat's own default (settings.json defaultThinkingLevel, else the model's).</summary>
    public ThinkingLevel? Effort { get; init; }
    /// <summary>For a hybrid subscription seat: the share of turns to run on its own model now (broker headers).</summary>
    public HybridAnswer? Hybrid { get; init; }
}

/// <summary>A hybrid seat's route now: the % of its turns the broker sends to its own model in its rate
/// <paramref name="Band"/>, and the local model's profile, which it compacts by once the band is past ok.</summary>
internal sealed record HybridAnswer(int LocalShare, string Band, LocalProfile? Profile)
{
    /// <summary>The router model the local side runs, over the seat's own router connection; null = its own model.</summary>
    public string? LocalModel { get; init; }
    /// <summary>That model's window, when the broker's profile table knows it.</summary>
    public int? LocalWindow { get; init; }
    /// <summary>"turn-kind" for a local pair (digital-scribe, 2026-10-28: "hybrid with the two local models ... miniclaude
    /// for fast cheap calls and qwen3.8-flash-next for expensive smarter ones"): the assigned model is the smart side, the
    /// local model the fast one, and the kind of each turn decides (<see cref="TurnKinds"/>); null = the share decides.</summary>
    public string? Pick { get; init; }
    /// <summary>For a pair: the largest tool output (estimated tokens) a follow-up may read and still run fast.</summary>
    public int? FollowupMaxTokens { get; init; }
    public bool IsPair => Pick == SubscriptionAssignment.TurnKindPick;
    /// <summary>The broker's word on whether the subscription may take turns the local model fails (X-Pi-Hybrid-Fallback):
    /// it may while more than the connection's fallback floor is left, below its reserve too (digital-scribe, 2026-10-29:
    /// "if local models fail repeatedly... and the reserve of 15% is still safe, we can use openai sub down to 10% max").
    /// Null from a broker that does not say: then only while the pace still gives the subscription turns.</summary>
    public bool? Fallback { get; init; }

    /// <summary>Whether a turn the local model fails may run on the subscription now.</summary>
    public bool FallbackAllowed => Fallback ?? LocalShare < 100;

    /// <summary>The kind of each turn decides its side: a local pair, or a subscription seat whose cheap turns run locally
    /// (<see cref="SubscriptionAssignment.CheapLocalPick"/>).</summary>
    public bool PicksByKind => Pick is SubscriptionAssignment.TurnKindPick or SubscriptionAssignment.CheapLocalPick;
}

/// <summary>The Subscriptions tab's context/output profile for a seat on its own model.</summary>
internal sealed record LocalProfile(int CompactAtTokens, int MaxOutputTokens)
{
    /// <summary>The local model's smallest profile output (broker header; null from an older broker): kept in reserve
    /// when the seat's own window is too small, so a bigger profile never gets a smaller budget.</summary>
    public int? OutputFloor { get; init; }
}

internal static class SubscriptionAssignment
{
    public const string RevisionHeader = "X-Pi-Assignment-Revision";
    public const string LoadedHeader = "X-Pi-Assignment-Loaded";
    /// <summary>The revision of the built-in local connection, and of a seat with no assignment.</summary>
    public const string LocalRevision = "local";
    public const string ProfileCompactAtHeader = "X-Pi-Profile-Compact-At";
    public const string ProfileMaxOutputHeader = "X-Pi-Profile-Max-Output";
    public const string ProfileOutputFloorHeader = "X-Pi-Profile-Output-Floor";
    public const string EffortHeader = "X-Pi-Effort";
    public const string HybridShareHeader = "X-Pi-Hybrid-Local-Share";
    public const string HybridBandHeader = "X-Pi-Hybrid-Band";
    public const string HybridCompactAtHeader = "X-Pi-Hybrid-Compact-At";
    public const string HybridMaxOutputHeader = "X-Pi-Hybrid-Max-Output";
    public const string HybridOutputFloorHeader = "X-Pi-Hybrid-Output-Floor";
    public const string HybridModelHeader = "X-Pi-Hybrid-Local-Model";
    public const string HybridWindowHeader = "X-Pi-Hybrid-Local-Window";
    public const string HybridPickHeader = "X-Pi-Hybrid-Pick";
    public const string HybridFollowupHeader = "X-Pi-Hybrid-Followup-Max";
    public const string HybridFallbackHeader = "X-Pi-Hybrid-Fallback";
    public const string TurnKindPick = "turn-kind";
    /// <summary>A subscription seat's hybrid without pace shaping (digital-scribe, 2026-10-29: "no more rate shaping but we keep
    /// the hybrid models mix since cheap calls still can run on local models... at 15% we switch hard to just local
    /// models"): routine turns run on the local model, the rest on the subscription, every turn locally at a share of 100.</summary>
    public const string CheapLocalPick = "cheap-local";
    /// <summary>The broker's default follow-up limit, for a pair whose answer names none.</summary>
    public const int DefaultFollowupMaxTokens = 4000;

    /// <summary>A hybrid answer from the broker's headers, or null for a seat that is not hybrid.</summary>
    internal static HybridAnswer? HybridOf(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues(HybridShareHeader, out var shares)
            || !int.TryParse(shares.FirstOrDefault(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var share)
            || share is < 0 or > 100) return null;
        var band = response.Headers.TryGetValues(HybridBandHeader, out var bands) && bands.FirstOrDefault() is { Length: > 0 and <= 16 } named ? named : "ok";
        var profile = Header(response, HybridCompactAtHeader) is int at && Header(response, HybridMaxOutputHeader) is int max
            ? new LocalProfile(at, max) { OutputFloor = Header(response, HybridOutputFloorHeader) } : null;
        var model = response.Headers.TryGetValues(HybridModelHeader, out var models) && models.FirstOrDefault() is { Length: > 0 and <= 100 } m ? m : null;
        var pick = response.Headers.TryGetValues(HybridPickHeader, out var picks) ? picks.FirstOrDefault() switch
        {
            TurnKindPick => TurnKindPick,
            CheapLocalPick => CheapLocalPick,
            _ => null,
        } : null;
        var fallback = response.Headers.TryGetValues(HybridFallbackHeader, out var fallbacks) ? fallbacks.FirstOrDefault() switch
        {
            "on" => true,
            "off" => false,
            _ => (bool?)null,
        } : null;
        return new(share, band, profile)
        {
            LocalModel = model, LocalWindow = Header(response, HybridWindowHeader),
            Pick = pick, FollowupMaxTokens = pick is null ? null : Header(response, HybridFollowupHeader),
            Fallback = fallback,
        };
    }

    private static int? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) && int.TryParse(values.FirstOrDefault(), System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var n) && n > 0 ? n : null;

    /// <summary>The seat's own model under a profile, in the window the model is configured with (the broker does not know
    /// it): it compacts at the profile's budget while the window is larger, else at the window less the model's smallest
    /// profile output (the broker's floor, the same for every profile); it writes the profile's output, never more than the
    /// window, and each request asks for no more than its window leaves beside its prompt (AgentLoop). digital-scribe,
    /// 2026-10-20: token budgets come "at worst from config files at best from Subscriptions settings"; until then a profile
    /// whose budget and output did not fit together was moved under the window by fixed 4,096-token margins.</summary>
    public static ModelConfig WithProfile(ModelConfig local, LocalProfile profile)
    {
        var window = local.ContextWindow;
        var reserve = Math.Clamp(profile.OutputFloor ?? profile.MaxOutputTokens, 1, Math.Max(1, window - 1));
        return local with
        {
            MaxOutputTokens = Math.Min(profile.MaxOutputTokens, window),
            CompactAtTokens = profile.CompactAtTokens < window ? profile.CompactAtTokens : window - reserve,
        };
    }

    /// <summary>Fetches the assignment. <paramref name="loaded"/> is the revision the seat runs (null at startup);
    /// the broker records it, which is how the Subscriptions tab shows whether a change was applied.</summary>
    public static async Task<SeatAssignmentAnswer> FetchAsync(Uri broker, string token, X509Certificate2? ca, string? loaded, CancellationToken ct)
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = false };
        if (ca is not null) SubscriptionBrokerTrust.Pin(handler, ca);
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(broker, "instance"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (loaded is not null) request.Headers.Add(LoadedHeader, loaded);
        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        var revision = response.Headers.TryGetValues(RevisionHeader, out var values) ? values.FirstOrDefault() : null;
        var effort = response.Headers.TryGetValues(EffortHeader, out var efforts) && efforts.FirstOrDefault() is { } named
            && ArgsParser.TryParseThinking(named, out var level) ? level : (ThinkingLevel?)null;
        if (response.StatusCode == HttpStatusCode.NoContent)
            return new(true, null, revision ?? LocalRevision) { Effort = effort, Profile = Header(response, ProfileCompactAtHeader) is int at
                && Header(response, ProfileMaxOutputHeader) is int max ? new(at, max) { OutputFloor = Header(response, ProfileOutputFloorHeader) } : null };
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("No authorized subscription assignment is available for this instance.");
        var assigned = (JsonSerializer.Deserialize(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false), CoreJsonContext.Default.ModelConfig)
            ?? throw new InvalidOperationException("Invalid subscription assignment.")).RequireBudgets("subscription assignment");
        // A broker older than the revision header: fall back to a digest of what matters, so a change still shows.
        return new(false, WindowIsBudget(assigned), revision ?? $"{assigned.Provider}-{assigned.Connection}-{assigned.Id}-{assigned.ContextWindow}-{assigned.MaxOutputTokens}".GetHashCode().ToString("x8"))
            { Effort = effort, Hybrid = HybridOf(response) };
    }

    /// <summary>
    /// An assignment as the seat runs it: the window the Subscriptions tab shows is where the seat compacts, down to half
    /// of it (digital-scribe, 2026-10-20: "it's 180k at which we compact down to 90k at this point across the board";
    /// "page is source of truth"; "use the settings one sets in the UI"). A request then carries up to that much context
    /// plus its output, so the model's window becomes the two together; the subscription models' own windows (500k to
    /// 1.05M) hold it. A profile's budget, when the tab picked one, stays where it compacts.
    /// </summary>
    public static ModelConfig WindowIsBudget(ModelConfig assigned)
    {
        var at = assigned.CompactAtTokens ?? assigned.ContextWindow;
        return assigned with { CompactAtTokens = at, ContextWindow = Math.Max(assigned.ContextWindow, at + assigned.MaxOutputTokens) };
    }

    /// <summary>The broker's built-in router connection: the seat calls the factory model router itself, OpenAI-style.</summary>
    public const string RouterConnection = "router";

    public static bool IsRouter(ModelConfig assigned) =>
        assigned.Provider == "openai-completions" && assigned.Connection == RouterConnection;

    /// <summary>A router assignment as the seat runs it: the broker hands no key, the router knows every caller by its
    /// factory token. When the seat's own model is the same route on the same router, its tuning (compat, reasoning,
    /// inputs) carries over; the assignment's limits always win. The base is the seat's own router connection's.</summary>
    public static ModelConfig ForRouter(ModelConfig assigned, string factoryToken, ModelConfig? local)
    {
        // The router is reached the way the seat's box reaches it: the seat's own router connection, which a move to an
        // AI-TLT box rewrote to the relay's llm forward. The broker's URL is how the broker's box reaches the router; a seat
        // on host-02 calling it directly was refused (2026-10-16: "HTTP 403 caller address is not allowed").
        var routed = assigned with
        {
            ApiKey = factoryToken, ApiKeyEnv = null,
            BaseUrl = local is { Provider: "openai-completions" } own && own.BaseUrl.Length > 0 ? own.BaseUrl : assigned.BaseUrl,
        };
        return local is not null && local.Id == assigned.Id
            ? routed with { Compat = local.Compat, Reasoning = local.Reasoning, DefaultThinkingLevel = local.DefaultThinkingLevel,
                Input = local.Input, MaxTools = local.MaxTools, Name = local.Name }
            : routed;
    }

    /// <summary>Where a seat compacts: its budget (the Subscriptions tab's window, see <see cref="WindowIsBudget"/>, or a
    /// profile's) while its window is larger, else as much as its window holds beside the maximum output. Until 2026-10-20 a
    /// seat without a budget compacted at 65% of its window, 117k of a 180k assignment, while the tab said 180k down to 90k;
    /// and a budget was cut to the window less the whole output cap, until each request asked for no more output than its
    /// window leaves (AgentLoop), so a 200k local model writing up to 131,072 compacted at 68,928, not the tab's 180k.</summary>
    public static int CompactAt(ModelConfig assigned) =>
        assigned.CompactAtTokens is int at && at < assigned.ContextWindow
            ? at
            : Math.Max(1, assigned.ContextWindow - assigned.MaxOutputTokens);

    /// <summary>A model sets the compaction limits: it compacts at <see cref="CompactAt"/> and down to half of that
    /// (@digital-scribe, 2026-09-26: "it should always compact at 50%"; 2026-10-20: "180k at which we compact down to
    /// 90k"). The window check keeps the output in reserve, never more than the window holds above the compaction point:
    /// the output is cut per request to what the window leaves, so it must not pull the compaction below the budget.</summary>
    public static Settings WithCompaction(Settings settings, ModelConfig assigned) => settings with
    {
        CompactionTriggerTokens = CompactAt(assigned),
        CompactionReserveTokens = RoomAboveCompaction(assigned),
        CompactionTargetTokens = CompactAt(assigned) / 2,
    };

    /// <summary>The reserve the window check keeps for <paramref name="model"/>: its output, at most what its window holds
    /// above its compaction point.</summary>
    internal static int RoomAboveCompaction(ModelConfig model) =>
        Math.Min(model.MaxOutputTokens, Math.Max(1, model.ContextWindow - CompactAt(model)));

    /// <summary>A local model compacts by the same rule unless settings.json names its own trigger, rather than a
    /// hand-set 50k/25k that no other seat shares (@digital-scribe, 2026-09-28: "let's align local LLM with Luna's
    /// settings").</summary>
    public static Settings WithDefaultCompaction(Settings settings, ModelConfig local) =>
        settings.CompactionTriggerTokens is null ? WithCompaction(settings, local) : settings;
}

/// <summary>A seat's assignment, followed while it runs. Until 2026-10-01 a changed assignment was loaded only by
/// a restart, and only once the seat was idle with its last task delivered; a busy seat, or one parked on a failing
/// model, ran its old model for hours after the Subscriptions tab changed it (team-lead-2 kept calling a spent Codex
/// account after it was moved to local). Now the seat asks between turns and swaps the model in place, keeping its
/// session (@digital-scribe, 2026-10-01: "can't we restart and simply restore the session so we can avoid waiting
/// for idle points?").</summary>
internal sealed class SeatAssignmentSwitch
{
    private readonly Uri _broker;
    private readonly string _token;
    private readonly X509Certificate2? _ca;
    private readonly ModelConfig? _local;
    private readonly Settings _baseSettings;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _nextCheck;

    public SeatAssignmentSwitch(Uri broker, string token, X509Certificate2? ca, string revision, bool runsSubscription,
        ModelConfig current, ModelConfig? local, Settings baseSettings, TimeProvider? time = null)
    {
        _broker = broker;
        _token = token;
        _ca = ca;
        Revision = revision;
        RunsSubscription = runsSubscription;
        Model = current;
        _local = local;
        _baseSettings = baseSettings;
        _time = time ?? TimeProvider.System;
        _nextCheck = _time.GetUtcNow() + Interval;
    }

    public TimeSpan Interval { get; init; } = TimeSpan.FromSeconds(60);
    /// <summary>After a failed check: a broker outage must not add a 20-second wait to every turn.</summary>
    public TimeSpan FailureBackoff { get; init; } = TimeSpan.FromMinutes(5);
    /// <summary>For tests: the broker call.</summary>
    internal Func<string, CancellationToken, Task<SeatAssignmentAnswer>>? Fetch { get; init; }

    public string Revision { get; private set; }
    public bool RunsSubscription { get; private set; }
    /// <summary>True when the seat runs its own model under a profile from the Subscriptions tab, which then sets
    /// its compaction rather than settings.json.</summary>
    public bool Profiled { get => _profiled; init => _profiled = value; }
    private bool _profiled;
    public ModelConfig Model { get; private set; }
    /// <summary>The effort the assignment picked (null = none picked); set at startup, then by every switch.</summary>
    public ThinkingLevel? Effort { get; init; }
    private ThinkingLevel? _effort;
    /// <summary>The seat's own default effort, for an assignment that picks none.</summary>
    public ThinkingLevel DefaultEffort { get; init; } = ThinkingLevel.Off;
    /// <summary>The effort the seat runs now: the assignment's pick, else its own default.</summary>
    public ThinkingLevel CurrentEffort => (_effortSet ? _effort : Effort) ?? DefaultEffort;
    private bool _effortSet;
    /// <summary>Counts switches, so a turn hook can tell whether the model changed since it last looked.</summary>
    public int Generation { get; private set; }
    public Action<string>? Log { get; init; }

    /// <summary>The subscription model of a hybrid seat (the one the broker assigned), whichever model runs now.</summary>
    private ModelConfig? _subscriptionModel;
    /// <summary>A hybrid seat's route, from the broker; null = not hybrid. Updated on every check, also without a switch.</summary>
    public HybridAnswer? Hybrid { get => _hybrid; init { _hybrid = value; if (value is not null) _subscriptionModel = Model; } }
    private HybridAnswer? _hybrid;
    /// <summary>Turns per run before a hybrid seat may change sides again: every switch cools both prompt caches.</summary>
    public int RunTurns { get; init; } = 3;
    private int _runLeft;
    private bool _runLocal;
    private int _owed;
    /// <summary>How long a hybrid seat runs its turns on its own model after its provider failed a turn (2026-10-25: Grok
    /// "at capacity due to high demand ... try again in a few minutes"). The next turn after it tries the subscription again.</summary>
    public TimeSpan ProviderTroubleCooldown { get; init; } = TimeSpan.FromMinutes(5);
    private DateTimeOffset _troubleUntil = DateTimeOffset.MinValue;
    /// <summary>Whether the seat has said it tries the subscription again since the trouble window ended.</summary>
    private bool _troubleOver = true;

    /// <summary>True while a failed turn can run elsewhere: a hybrid seat on its subscription side with its own model.</summary>
    public bool CanHandBack => RunsSubscription && _hybrid is not null && HybridLocal is not null;

    /// <summary>True while the provider's trouble keeps this hybrid seat's turns on its own model.</summary>
    public bool ProviderTroubled => _hybrid is not null && _time.GetUtcNow() < _troubleUntil;

    private volatile bool _handedBack;

    /// <summary>True once per turn the subscription client handed back: the run that ended on it resumes at once on the seat's
    /// own model. A failure of the seat's own model is a plain model error (resumed after a backoff), never a loop here.</summary>
    public bool TakeHandBack()
    {
        var handed = _handedBack;
        _handedBack = false;
        return handed && ProviderTroubled;
    }

    /// <summary>The provider failed a turn this seat can run elsewhere: its turns run on its own model for
    /// <see cref="ProviderTroubleCooldown"/>.</summary>
    public void NoteProviderTrouble(string code)
    {
        _troubleUntil = _time.GetUtcNow() + ProviderTroubleCooldown;
        _troubleOver = false;
        _handedBack = true;
        Log?.Invoke(FormattableString.Invariant(
            $"subscription: {_subscriptionModel?.Provider ?? Model.Provider} failed a turn ({code}); turns run on the seat's own model until {_troubleUntil.UtcDateTime:HH:mm}Z, then the subscription again"));
    }

    /// <summary>Local failures in a row before a hybrid seat runs its turns on the subscription (the owner's "fail
    /// repeatedly"): one failed turn can be a hiccup; the turn after it already runs on the subscription by its kind.</summary>
    public int LocalFailuresBeforeFallback { get; init; } = 2;

    /// <summary>How long a hybrid seat runs its turns on the subscription after its local model failed repeatedly; then it
    /// tries the local model again.</summary>
    public TimeSpan LocalTroubleCooldown { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>Told when the seat's turns move to the subscription because its local model failed repeatedly: the local
    /// model, the failure's code, and until when (seat telemetry).</summary>
    public Action<string, string, DateTimeOffset>? OnFallback { get; init; }

    private int _localFailures;
    private DateTimeOffset _localTroubleUntil = DateTimeOffset.MinValue;
    private bool _localTroubleOver = true;
    private volatile bool _fellBack;

    /// <summary>True while a failed local turn can run elsewhere: a hybrid seat (not a local pair) on its local side whose
    /// subscription may take it (<see cref="HybridAnswer.FallbackAllowed"/>).</summary>
    public bool CanFailOver => !RunsSubscription && _hybrid is { IsPair: false } h && h.FallbackAllowed && _subscriptionModel is not null
        && HybridLocal is not null;

    /// <summary>True while the local model's failures keep this hybrid seat's turns on the subscription.</summary>
    public bool LocalTroubled => _hybrid is not null && _time.GetUtcNow() < _localTroubleUntil;

    /// <summary>A local turn failed before any content on a seat that can fail over. The <see cref="LocalFailuresBeforeFallback"/>th
    /// in a row moves the seat's turns to the subscription for <see cref="LocalTroubleCooldown"/>.</summary>
    public void NoteLocalTrouble(string code)
    {
        var local = Model.Id;
        var failures = Interlocked.Increment(ref _localFailures);
        if (failures < LocalFailuresBeforeFallback)
        {
            Log?.Invoke(FormattableString.Invariant(
                $"subscription: the local model {local} failed a turn ({code}); {LocalFailuresBeforeFallback - failures} more in a row and turns run on {_subscriptionModel?.Id ?? "the subscription"} for {LocalTroubleCooldown.TotalMinutes:0} min"));
            return;
        }
        Interlocked.Exchange(ref _localFailures, 0);
        _localTroubleUntil = _time.GetUtcNow() + LocalTroubleCooldown;
        _localTroubleOver = false;
        _fellBack = true;
        Log?.Invoke(FormattableString.Invariant(
            $"subscription: the local model {local} failed {failures} turns in a row ({code}); turns run on {_subscriptionModel?.Provider} {_subscriptionModel?.Id} until {_localTroubleUntil.UtcDateTime:HH:mm}Z, then the local model again"));
        try { OnFallback?.Invoke(local, code, _localTroubleUntil); }
        catch (Exception) { }
    }

    /// <summary>A local turn ended with an answer: the failures in a row start over.</summary>
    public void NoteLocalAnswer() => Interlocked.Exchange(ref _localFailures, 0);

    /// <summary>True once after the local model's failures moved the seat's turns: the run that ended on it resumes at once,
    /// on the subscription.</summary>
    public bool TakeFallback()
    {
        var fell = _fellBack;
        _fellBack = false;
        return fell && LocalTroubled;
    }

    /// <summary>The subscription model a hybrid seat falls back on, for its telemetry.</summary>
    public string? SubscriptionModelId => _subscriptionModel?.Id;

    /// <summary>Counts the turns a hybrid seat ran on each side, for its log and tests.</summary>
    public int LocalTurns { get; private set; }
    public int SubscriptionTurns { get; private set; }

    /// <summary>The local side of a hybrid seat, or null when it is not hybrid or has no own model.</summary>
    public ModelConfig? HybridLocal => _hybrid is { } h && _local is { } own && _subscriptionModel is { } subscription
        ? LocalSide(h.LocalModel is { } model ? own with { Id = model, Name = model, ContextWindow = h.LocalWindow ?? own.ContextWindow } : own, h, subscription)
        : null;

    /// <summary>The picked local model (digital-scribe, 2026-10-16: "can the local model also be configured at the same time
    /// as the subscription model?") runs over the seat's own router connection, whose base already reaches the router the way
    /// its box does (relay 1.3.5+ rewrote it on an AI-TLT box): only the model id and its window change. It writes the
    /// local profile's output (else its own) and compacts where the subscription side does (digital-scribe, 2026-10-20: "64k
    /// is not the local model limit, it should be at 180 like all the others"); only a window not larger than that point
    /// moves it to the window less the profile's smallest output. Each request asks for no more output than its window
    /// leaves beside its prompt (AgentLoop), so the output no longer cuts the compaction point: until 2026-10-20 a 200k
    /// model writing 131,072 compacted at 68,928. The profile's budget no longer sizes the seat.</summary>
    private static ModelConfig LocalSide(ModelConfig local, HybridAnswer h, ModelConfig subscription)
    {
        var output = h.Profile?.MaxOutputTokens ?? local.MaxOutputTokens;
        var at = SubscriptionAssignment.CompactAt(subscription);
        return local with
        {
            MaxOutputTokens = output,
            CompactAtTokens = at < local.ContextWindow ? at : Math.Max(1, local.ContextWindow - (h.Profile?.OutputFloor ?? output)),
        };
    }

    /// <summary>
    /// Picks the side of a hybrid seat's next turn (digital-scribe, 2026-10-16: per turn). The broker's share decides, a
    /// run of <see cref="RunTurns"/> turns at a time, so the share holds over the session without changing sides every
    /// turn; a share of 100% runs every turn locally. A turn whose context does not fit the local model stays on the
    /// subscription unless every turn runs locally. Until 2026-10-20 a light turn (only chat, KB, notebook) went local
    /// whatever the share: GPT seats ran 26-67% of their calls locally against a 1-2% share (digital-scribe: "why aren't
    /// the agents use more GPT?!"), so the share alone decides now. Returns true when the model changed.
    /// </summary>
    /// <remarks>A watch turn (<paramref name="watch"/>: the seat reads the hub's hourly digest and has not acted yet) runs on
    /// the seat's own model whenever its context fits there, outside the share: reading costs no subscription.</remarks>
    public bool PickTurn(int contextTokens, bool watch = false, TurnKind kind = TurnKind.Unknown)
    {
        if (_hybrid is not { } hybrid || _subscriptionModel is not { } subscription || HybridLocal is not { } local) return false;
        if (hybrid.IsPair) return PickPairTurn(contextTokens, watch ? TurnKind.Watch : kind, subscription, local);
        // The next request, as the session measures it, must fit the local side below its compaction point, or the session
        // compacts for the local model's sake: a hybrid seat sized at 180k compacted at ~67k on its local turns (2026-10-20).
        // Too big for it, the turn runs on the subscription. The output no longer narrows it: each request asks for no more
        // than the window leaves beside its prompt (AgentLoop), and CompactionFor keeps the reserve inside that room.
        var fits = contextTokens <= SubscriptionAssignment.CompactAt(local);
        bool wantLocal;
        if (!_troubleOver && !ProviderTroubled)
        {
            _troubleOver = true;
            Log?.Invoke($"subscription: trying {subscription.Provider} {subscription.Id} again after its failed turn");
        }
        if (!_localTroubleOver && !LocalTroubled)
        {
            _localTroubleOver = true;
            Log?.Invoke($"subscription: trying the local model {local.Id} again after its failures");
        }
        if (ProviderTroubled && fits) wantLocal = true;
        // The local model failed repeatedly: its turns run on the subscription while the broker says it may take them, below
        // the reserve too, never below the fallback floor.
        else if (LocalTroubled && hybrid.FallbackAllowed) wantLocal = false;
        else if (hybrid.LocalShare >= 100 || watch && fits) wantLocal = true;
        else if (hybrid.Pick == SubscriptionAssignment.CheapLocalPick)
        {
            // No pace: a routine follow-up runs locally while it fits, every other turn on the subscription. A turn at the
            // compaction point stays on the subscription, which writes the summary.
            var compactionDue = SizingModel is { } sizing && contextTokens >= SubscriptionAssignment.CompactAt(sizing);
            wantLocal = fits && !compactionDue && kind == TurnKind.Routine;
        }
        else
        {
            if (_runLeft <= 0)
            {
                _owed += hybrid.LocalShare;
                _runLocal = _owed >= 100;
                if (_runLocal) _owed -= 100;
                _runLeft = RunTurns;
            }
            _runLeft--;
            wantLocal = fits && _runLocal;
        }
        if (wantLocal) LocalTurns++; else SubscriptionTurns++;
        var next = wantLocal ? local : subscription;
        if (next == Model && RunsSubscription == !wantLocal) return false;
        Model = next;
        RunsSubscription = !wantLocal;
        _profiled = wantLocal;
        Generation++;
        return true;
    }

    /// <summary>
    /// A local pair's turn (<see cref="HybridAnswer.IsPair"/>): fast for a watch turn and a routine follow-up, smart for
    /// everything else, the run's first turn on a task, the turn after a failure and work turns included. A turn whose
    /// context is at the compaction point goes smart too: the compaction summary is written on that turn's model. Both sides
    /// are router models over the seat's own connection, so the subscription client is never involved
    /// (<see cref="RunsSubscription"/> stays false).
    /// </summary>
    private bool PickPairTurn(int contextTokens, TurnKind kind, ModelConfig smart, ModelConfig fast)
    {
        var compactionDue = SizingModel is { } sizing && contextTokens >= SubscriptionAssignment.CompactAt(sizing);
        var fits = contextTokens <= SubscriptionAssignment.CompactAt(fast);
        var wantFast = fits && !compactionDue && kind is TurnKind.Watch or TurnKind.Routine;
        if (wantFast) LocalTurns++; else SubscriptionTurns++;
        var next = wantFast ? fast : smart;
        Log?.Invoke(FormattableString.Invariant(
            $"pair: {TurnKinds.Name(kind)} turn on {(wantFast ? "fast" : "smart")} {next.Id} (fast {LocalTurns}, smart {SubscriptionTurns})"));
        if (next == Model) return false;
        Model = next;
        Generation++;
        return true;
    }

    /// <summary>The output cap for a request: none (the model's own, set by the assignment or its profile) while the
    /// broker decides the model, settings.json's maxTokens otherwise. Every seat's settings.json carried maxTokens 8192,
    /// and as a request option it overrode the profile, so no seat ever wrote more than 8192 tokens per turn whatever
    /// the Subscriptions tab said (2026-10-14: pi10 cut at 8192 twice after its local profile was raised to 32768).</summary>
    public int? MaxTokensFor(Settings settings) => RunsSubscription || _profiled ? null : settings.MaxTokens;

    /// <summary>The model a hybrid seat sizes its context for, whichever side runs the turn: the subscription side, at the
    /// tab's window like every other seat (digital-scribe, 2026-10-20: "64k is not the local model limit, it should be at
    /// 180 like all the others"; this replaces 2026-10-16's "in hybrid we always use the local model profile"). A turn goes
    /// local only while the context fits the local side (<see cref="PickTurn"/>). When every turn runs locally (a share of
    /// 100%) the seat sizes for the local side, which then must hold every turn. Null when the seat is not hybrid: it sizes
    /// for the model it runs.</summary>
    public ModelConfig? SizingModel => _hybrid is not { } h || _subscriptionModel is null ? null
        : h.IsPair ? (HybridLocal is { } fast && SubscriptionAssignment.CompactAt(fast) < SubscriptionAssignment.CompactAt(_subscriptionModel) ? fast : _subscriptionModel)
        : h.LocalShare >= 100 && HybridLocal is { } local ? local : _subscriptionModel;

    /// <summary>The compaction limits for the current model, on top of the seat's own settings.</summary>

    public Settings CompactionFor(Settings settings)
    {
        // A hybrid seat sizes its context for the subscription side on both sides, unless every turn runs locally (SizingModel).
        var limits = SizingModel is { } sizing ? SubscriptionAssignment.WithCompaction(_baseSettings, sizing)
            : RunsSubscription || _profiled ? SubscriptionAssignment.WithCompaction(_baseSettings, Model)
            : SubscriptionAssignment.WithDefaultCompaction(_baseSettings, Model);
        // Its local turns run under the same reserve: never more than the local side's window holds above its compaction
        // point, or the window check compacts a turn PickTurn sent there because it fits (pi3 at ~67k, 2026-10-20).
        var reserve = SizingModel is not null && HybridLocal is { } local && limits.CompactionReserveTokens is int sized
            ? Math.Min(sized, SubscriptionAssignment.RoomAboveCompaction(local))
            : limits.CompactionReserveTokens;
        return settings with
        {
            CompactionTriggerTokens = limits.CompactionTriggerTokens,
            CompactionReserveTokens = reserve,
            CompactionTargetTokens = limits.CompactionTargetTokens,
        };
    }

    /// <summary>Asks the broker (at most once per <see cref="Interval"/> unless <paramref name="force"/>) and
    /// returns true when the seat now runs a different model. Never throws: a failed check keeps the model.</summary>
    public async Task<bool> CheckAsync(bool force, CancellationToken ct)
    {
        if (!force && _time.GetUtcNow() < _nextCheck) return false;
        if (!await _gate.WaitAsync(0, ct).ConfigureAwait(false)) return false;
        try
        {
            SeatAssignmentAnswer answer;
            try { answer = await FetchAsync(Revision, ct).ConfigureAwait(false); }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _nextCheck = _time.GetUtcNow() + FailureBackoff;
                Log?.Invoke($"assignment check failed ({ex.Message}); staying on {Model.Id}");
                return false;
            }
            _nextCheck = _time.GetUtcNow() + Interval;
            if (answer.Revision == Revision)
            {
                // The band moves without a new revision: a hybrid seat takes its new share and sizing at once. So does a seat
                // whose start answer carried no hybrid part (pi13, 2026-10-27: it started on xai alone, every turn went to a
                // spent allowance, and the hybrid route the broker sent afterwards was ignored).
                if (answer.Hybrid is { } now && now != _hybrid && _local is not null && (_hybrid is not null || RunsSubscription || now.IsPair && _profiled))
                {
                    var was = _hybrid;
                    _subscriptionModel ??= Model;
                    _hybrid = now;
                    Log?.Invoke($"hybrid: band {now.Band}, {now.LocalShare}% of turns local");
                    // A new band, a share that now sends every turn local (or no longer does), or a local side that is now
                    // another model, window or profile resizes the context. The window alone did not until 2026-10-20, so
                    // seats already running locally kept compacting at 67,232 after the broker sent their real window.
                    if (was is null || was.Band != now.Band || (was.LocalShare >= 100) != (now.LocalShare >= 100)
                        || was.LocalWindow != now.LocalWindow || was.LocalModel != now.LocalModel || was.Profile != now.Profile)
                    { Generation++; return true; }
                }
                return false;
            }
            if (answer.Model is { } routed && SubscriptionAssignment.IsRouter(routed))
            {
                // The router: an OpenAI-style model the seat calls itself, with the assignment's limits.
                Model = SubscriptionAssignment.ForRouter(routed, _token, _local);
                RunsSubscription = false;
                _profiled = true;
                // A router seat mixes only as a local pair: the routed model is its smart side.
                _hybrid = _local is not null && answer.Hybrid is { IsPair: true } pair ? pair : null;
                _subscriptionModel = _hybrid is null ? null : Model;
                if (_hybrid is { } p) Log?.Invoke($"pair: smart {Model.Id}, fast {p.LocalModel ?? _local!.Id}, picked by turn kind");
            }
            else if (answer.Model is { } assigned)
            {
                try { _ = PiSharp.Llm.Responses.SubscriptionResponsesClient.Provider(assigned.Provider); }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException)
                {
                    Log?.Invoke($"assignment {answer.Revision} names an unsupported provider ({ex.Message}); staying on {Model.Id}");
                    return false;
                }
                Model = assigned;
                RunsSubscription = true;
                _profiled = false;
                _subscriptionModel = assigned;
                _hybrid = _local is null ? null : answer.Hybrid;
                if (_hybrid is { } h) Log?.Invoke($"hybrid: band {h.Band}, {h.LocalShare}% of turns local");
            }
            else if (_local is { } local)
            {
                Model = answer.Profile is { } profile ? SubscriptionAssignment.WithProfile(local, profile) : local;
                RunsSubscription = false;
                _profiled = answer.Profile is not null;
                _hybrid = null;
            }
            else
            {
                Log?.Invoke($"assigned the local model (revision {answer.Revision}), but this seat has none; staying on {Model.Id}");
                return false;
            }
            Revision = answer.Revision;
            _effort = answer.Effort;
            _effortSet = true;
            Generation++;
            Log?.Invoke($"assignment changed: now {Model.Provider} {Model.Id} (revision {Revision}), switched in place");
            // Report the new revision at once, so the Subscriptions tab shows the change as applied.
            try { await FetchAsync(Revision, ct).ConfigureAwait(false); }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested) { }
            return true;
        }
        finally { _gate.Release(); }
    }

    private Task<SeatAssignmentAnswer> FetchAsync(string loaded, CancellationToken ct) =>
        Fetch is { } fetch ? fetch(loaded, ct) : SubscriptionAssignment.FetchAsync(_broker, _token, _ca, loaded, ct);
}
