// CLI host: REPL / print / JSON-event modes. Wires settings, models, tools, session, system prompt.
using System.Reflection;
using System.Text.Json.Nodes;
using PiSharp.Core.Agent;
using PiSharp.Core.Checkpoints;
using PiSharp.Core.Config;
using PiSharp.Core.Factory;
using PiSharp.Core.Guard;
using PiSharp.Core.Mcp;
using PiSharp.Core.Harness;
using PiSharp.Core.Messages;
using PiSharp.Core.Models;
using PiSharp.Core.Notebook;
using PiSharp.Core.Session;
using PiSharp.Core.Verification;
using PiSharp.Llm;
using PiSharp.Llm.Responses;
using PiSharp.Core.Json;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using PiSharp.Tools;
using PromptTemplate = PiSharp.Core.Skills.PromptTemplate;
using Skill = PiSharp.Core.Skills.Skill;

namespace PiSharp.Cli;

public sealed record CliState
{
    public required string Cwd { get; init; }
    public required ModelConfig Model { get; set; }
    public CubicleSteering? Steering { get; init; }
    /// <summary>AI Factory attached mode: inbox steering, replies and the task-boundary reset. Null = local run.</summary>
    public FactorySteering? Factory { get; init; }
    /// <summary>The actor slug the factory token was verified as; null unless attached mode passed its preflight.</summary>
    public string? FactoryActor { get; init; }
    /// <summary>The next turn reads the hub's hourly digest and has not acted yet: a hybrid seat runs it on its own model
    /// (operating-model plan, item 11: "the watch turn only reads, so it runs on the local model, and acting moves it to the
    /// paid one"). Set when the cubicle delivers a DIGEST, cleared by the first turn that calls a tool.</summary>
    public bool WatchTurn { get; set; }
    /// <summary>The MCP catalog refresh of the run in progress; a follow-up task calls it first (WithMessageSources).</summary>
    public Func<CancellationToken, bool, Task>? RefreshMcp { get; set; }
    /// <summary>An attached seat's role texts read again from the KB (<see cref="SeatStanding"/>, at most every two minutes),
    /// at a new task and between turns; a changed text rebuilds the system prompt. Null for a local run.</summary>
    public Func<CancellationToken, Task>? RefreshStanding { get; set; }
    /// <summary>Cache-stats tracker for the seat's prompt cache (adr-pi-sharp-cache-stats). Null = tracing off.</summary>
    public CacheStats? CacheStats { get; init; }
    /// <summary>Notebook-share tracker (pi-sharp-memory-curation-1): the notebook's share of each request. Null = no notebook.</summary>
    public NotebookShareTracker? NotebookShare { get; init; }
    /// <summary>Seat id for the cache_miss line; the factory actor slug, else the PI_SEAT environment variable.</summary>
    public string? CacheSeatId => FactoryActor ?? Environment.GetEnvironmentVariable("PI_SEAT");
    public ThinkingLevel Thinking { get; set; }
    public required IReadOnlyList<ModelConfig> Models { get; init; }
    public required IReadOnlyList<Skill> Skills { get; set; }
    public required IReadOnlyList<PromptTemplate> Templates { get; init; }
    public string? ConfigDir { get; init; }
    public IReadOnlyList<PiSharp.Core.Skills.PromptTemplate> McpTemplates { get; set; } = [];
    public McpManager Mcp { get; init; } = new();
    /// <summary>codeintel epic: the diagnostics hook for the code-intel service; null when feedback is off
    /// or the <c>code</c> MCP connection is unavailable. Never null-checks the client: fail open.</summary>
    public PiSharp.Core.CodeIntelFeedback.CodeIntelFeedback? CodeIntelFeedback { get; init; }
    public required AgentSession Agent { get; set; }
    public required AgentContext Context { get; init; }
    public required Settings Settings { get; set; }
    /// <summary>The broker assignment this seat follows while it runs; null without a broker.</summary>
    internal SeatAssignmentSwitch? Assignment { get; init; }
    /// <summary>The seat's own update from the Artifactory (null outside an attached seat, or with PI_SHARP_SELF_UPDATE=0).</summary>
    internal SelfUpdater? SelfUpdate { get; init; }
    /// <summary>Local hooks combined once at startup with opted-in active KB rule hooks.</summary>
    public IReadOnlyList<CommandHookSetting> EffectiveCommandHooks { get; init; } = [];
    /// <summary>The seat's guard over every tool call (Codex rules: forbid, ask, warn), built at start from the built-in
    /// rules, the fleet file and the active KB rules, for the seat's roles. Null = no guard.</summary>
    public PiSharp.Core.Guard.ToolGuard? SeatGuard { get; init; }
    /// <summary>Decides the calls a rule marks Ask (Codex auto-review).</summary>
    public PiSharp.Core.Guard.IActionReviewer? ActionReviewer { get; init; }
    /// <summary>Where an attached seat's events go (the hub's /seat/events). Null = local run or telemetry off.</summary>
    public SeatTelemetry? Telemetry { get; init; }
    public Action<string, Usage>? RecordReviewUsage { get; set; }
    public required StreamFunction Stream { get; init; }
    public string? SessionName { get; set; }
    public string? SessionDir { get; init; }
    public string? SessionId { get; init; }
    public bool Persist { get; init; }
    public NotebookStore? Notebook { get; init; }
    public NotebookSync? NotebookSync { get; init; }
    public ArtifactStore? Artifacts { get; init; }
    /// <summary>CP1 (pi-sharp-checkpoints-1): the single shadow checkpoint store for this run, or null when checkpoints are off / no session dir. Shared by the snapshot hook and by CP2's RewindTool so both use one store over one GitDir rather than constructing a second.</summary>
    public CheckpointStore? Checkpoints { get; init; }
    /// <summary>CP3 (pi-sharp-checkpoints-3): the green-marker + last-green-hint state machine, or null
    /// when checkpoints are off. Fed by the built-in bash tool's command-exit sink and drained by the
    /// AfterToolBatch hook.</summary>
    public CheckpointGreenTracker? CheckpointGreen { get; set; }
}

public sealed class CliHost
{
    /// <summary>The release version the builders stamp (-p:InformationalVersion); the csproj value in a local build.</summary>
    public static readonly string Version =
        typeof(CliHost).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? "0.0.0";

    /// <summary>
    /// Exit code for a run that died on a context overflow it could not recover from. Distinct
    /// from the generic failure code (1) so a supervisor never mistakes an overflow death for a
    /// handled turn (wi-8): the run ended on its own cause, the prompt did not fit, not after
    /// producing an answer. Outside the range any tool or the agent loop already returns.
    /// </summary>
    public const int ExitCodeContextOverflow = 4;

    /// <summary>
    /// Exit code for an idle, delivered seat whose subscription-broker assignment changed. The seat unit restarts it
    /// and the new process loads the new assignment. Distinct from 5 (task budget exhausted).
    /// </summary>
    public const int ExitCodeAssignmentChanged = 6;

    /// <summary>Exit code for an authorized operator restart request; unit restarts the process on the updated binary.</summary>
    public const int ExitCodeOperatorRestart = 8;

    /// <summary>A run that ended on a model or provider error. Attached mode retries it instead of exiting;
    /// everywhere else it leaves the process as the generic failure (1).</summary>
    private const int RunModelError = 7;

    /// <summary>Attached mode: how many times a task that stopped on a model error is resumed before the seat
    /// waits for its next message, and the backoff unit between them (1x, 2x, 3x).</summary>
    public int ModelErrorResumes { get; init; } = 3;
    public TimeSpan ModelErrorBackoff { get; init; } = TimeSpan.FromSeconds(30);
    /// <summary>A model error this long after the last resume starts a new streak of <see cref="ModelErrorResumes"/>.</summary>
    public TimeSpan ModelErrorStreak { get; init; } = TimeSpan.FromMinutes(30);

    public TextWriter Out { get; init; } = Console.Out;
    public TextWriter Err { get; init; } = Console.Error;
    private static TextWriter LogErr => Console.Error;
    public TextReader In { get; init; } = Console.In;
    public bool? Color { get; init; }
    public string? ConfigDir { get; init; }
    public Func<ModelConfig, StreamFunction>? StreamFactory { get; init; }
    public string? SubscriptionBrokerUrl { get; init; }
    /// <summary>Test hook: PEM path for <see cref="SubscriptionBrokerTrust.Variable"/>. Null reads the environment.</summary>
    public string? SubscriptionBrokerCaPath { get; init; }
    public Func<string, bool, IReadOnlyList<IAgentTool>>? ToolsFactory { get; init; }
    /// <summary>Test hook for supplying an in-process MCP manager.</summary>
    public Func<McpConfig, CancellationToken, Task<McpManager>>? McpFactory { get; init; }
    public bool HookCancelKey { get; init; } = true;
    /// <summary>Test hook: transport for the cubicle client (null = default HttpClient handler).</summary>
    public HttpMessageHandler? CubicleHandler { get; init; }
    /// <summary>Test hook: transport for the factory client (null = default HttpClient handler).</summary>
    public HttpMessageHandler? FactoryHandler { get; init; }
    /// <summary>Test hook: transport for the startup context-ceiling verification (null = default HttpClient handler).</summary>
    public HttpMessageHandler? CeilingHandler { get; init; }

    public static Task<int> RunAsync(string[] args) => new CliHost().RunAsync(args, CancellationToken.None);

    public async Task<int> RunAsync(IReadOnlyList<string> argv, CancellationToken cancellationToken = default)
    {
        var args = ArgsParser.Parse(argv);
        var configDir = args.ConfigDir ?? ConfigDir;
        foreach (var diagnostic in args.Diagnostics)
            Err.WriteLine(diagnostic);
        if (args.Help)
        {
            Out.WriteLine(ArgsParser.HelpText());
            return 0;
        }
        if (args.Version)
        {
            Out.WriteLine($"pi-sharp {Version}");
            return 0;
        }

        string cwd;
        try
        {
            cwd = string.IsNullOrWhiteSpace(args.Cwd)
                ? Directory.GetCurrentDirectory()
                : Path.GetFullPath(ConfigPaths.ExpandHome(args.Cwd));
            if (!Directory.Exists(cwd))
            {
                Err.WriteLine($"Working directory not found: {cwd}");
                return 2;
            }
        }
        catch (Exception ex)
        {
            Err.WriteLine(ex.Message);
            return 2;
        }

        var settings = SettingsLoader.Load(cwd, configDir);
        if (args.NoContextFiles) settings = settings with { NoContextFiles = true };

        // A models.json entry without its window or output is a load error naming both, never a guessed budget (2026-10-20).
        List<ModelConfig> models;
        try { models = ModelsFile.Load(cwd, configDir).ToList(); }
        catch (InvalidOperationException ex)
        {
            Err.WriteLine(ex.Message);
            return 2;
        }
        // The seat's own models and settings, kept for an assignment that moves it back to local while it runs.
        var localModels = models.ToList();
        var baseSettings = settings;
        X509Certificate2? loadedBrokerCa = null;
        var brokerCaSetting = SubscriptionBrokerCaPath ?? Environment.GetEnvironmentVariable(SubscriptionBrokerTrust.Variable);
        if (!string.IsNullOrWhiteSpace(brokerCaSetting))
        {
            try { loadedBrokerCa = SubscriptionBrokerTrust.Load(brokerCaSetting.Trim()); }
            catch (InvalidOperationException ex)
            {
                Err.WriteLine(ex.Message);
                return 2;
            }
        }
        using var brokerCa = loadedBrokerCa;

        // With a broker, the seat runs what it is assigned there: a subscription model, or the built-in local
        // connection (its own models.json), which is also what a seat with no assignment runs. The revision it
        // loaded is checked again between turns and while idle, and a change is switched in place (SeatAssignmentSwitch).
        Uri? subscriptionBroker = null;
        string? brokerToken = null;
        string? loadedRevision = null;
        // Only a seat that got a subscription model talks to the broker for inference. A local answer (204) or a
        // broker outage leaves it on its own model, so the router must treat it as a local seat.
        var runsSubscription = false;
        // A seat assigned the broker's router connection calls the factory model router itself (OpenAI-style, with
        // its factory token): not a subscription seat, but its limits come from the assignment.
        var runsRouter = false;
        LocalProfile? localProfile = null;
        ThinkingLevel? assignedEffort = null;
        HybridAnswer? assignedHybrid = null;
        var subscriptionBrokerSetting = SubscriptionBrokerUrl ?? Environment.GetEnvironmentVariable("PI_SHARP_SUBSCRIPTION_BROKER");
        if (!string.IsNullOrWhiteSpace(subscriptionBrokerSetting))
        {
            try
            {
                subscriptionBroker = new Uri(subscriptionBrokerSetting.TrimEnd('/') + "/");
                SubscriptionResponsesClient.ValidateBroker(subscriptionBroker);
                brokerToken = settings.Factory?.ResolveToken();
                if (string.IsNullOrWhiteSpace(brokerToken)) throw new InvalidOperationException("Factory identity is required for subscription seats.");
                var answer = await SubscriptionAssignment.FetchAsync(subscriptionBroker, brokerToken, brokerCa, null, cancellationToken).ConfigureAwait(false);
                if (answer.Model is { } routed && SubscriptionAssignment.IsRouter(routed))
                {
                    var own = ResolveModel(localModels, null, baseSettings.DefaultModel);
                    var onRouter = SubscriptionAssignment.ForRouter(routed, brokerToken, own);
                    models = [onRouter];
                    runsRouter = true;
                    settings = SubscriptionAssignment.WithCompaction(settings, onRouter);
                    // A local pair: the routed model is the smart side, the seat's own (or the picked) model the fast one.
                    if (answer.Hybrid is { IsPair: true } pair)
                    {
                        assignedHybrid = pair;
                        Err.WriteLine($"subscription: local pair, smart {onRouter.Id}, fast {pair.LocalModel ?? own?.Id}, picked by turn kind");
                    }
                    Err.WriteLine($"subscription: assigned the router, route {onRouter.Id} (revision {answer.Revision}); compaction at {settings.CompactionTriggerTokens} tokens down to {settings.CompactionTargetTokens}");
                }
                else if (answer.Model is { } assigned)
                {
                    _ = SubscriptionResponsesClient.Provider(assigned.Provider);
                    models = [assigned];
                    runsSubscription = true;
                    settings = SubscriptionAssignment.WithCompaction(settings, assigned);
                    assignedHybrid = answer.Hybrid;
                    if (assignedHybrid is { } h) Err.WriteLine($"subscription: hybrid, band {h.Band}, {h.LocalShare}% of turns on the seat's own model");
                    Err.WriteLine($"subscription: assigned {assigned.Provider} {assigned.Id} (revision {answer.Revision}); compaction at {settings.CompactionTriggerTokens} tokens down to {settings.CompactionTargetTokens}");
                }
                else
                {
                    localProfile = answer.Profile;
                    Err.WriteLine($"subscription: assigned the local model (revision {answer.Revision})"
                        + (localProfile is { } p ? $"; profile: compact at {p.CompactAtTokens}, output {p.MaxOutputTokens}" : ""));
                }
                loadedRevision = answer.Revision;
                assignedEffort = answer.Effort;
                if (assignedEffort is { } effort) Err.WriteLine($"subscription: effort {effort.ToString().ToLowerInvariant()} (picked on the assignment)");
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // A seat that has a local model keeps working on it; one that has none cannot run at all.
                if (models.Count == 0 || subscriptionBroker is null || string.IsNullOrWhiteSpace(brokerToken))
                {
                    Err.WriteLine("Subscription setup failed: " + ex.Message);
                    return 2;
                }
                Err.WriteLine("subscription: broker unavailable (" + ex.Message + "); running the local model until it answers");
                loadedRevision = SubscriptionAssignment.LocalRevision;
            }
        }
        if (models.Count == 0)
        {
            try
            {
                var envModel = ModelsFile.FromEnvironment();
                if (envModel is not null) models.Add(envModel);
            }
            catch (InvalidOperationException ex)
            {
                Err.WriteLine(ex.Message);
                return 2;
            }
        }

        if (args.ListModels)
        {
            if (models.Count == 0)
            {
                Err.WriteLine("No models configured. Create ~/.config/pi-sharp/models.json");
                return 2;
            }
            foreach (var m in models)
                Out.WriteLine($"{m.Id}\t{m.BaseUrl}\tctx={m.ContextWindow}\tmax={m.MaxOutputTokens}");
            return 0;
        }

        if (!string.IsNullOrWhiteSpace(args.Model) && ModelsFile.Find(models, args.Model) is null && models.Count > 0)
        {
            Err.WriteLine($"Unknown model: {args.Model}");
            return 2;
        }
        var model = ResolveModel(models, args.Model, settings.DefaultModel);
        if (model is null)
        {
            Err.WriteLine("""
                No model configured. Add ~/.config/pi-sharp/models.json, for example (contextWindow and maxOutputTokens
                are required; the numbers below are only an example: use your model server's window and output limit):

                {
                  "models": [
                    {
                      "id": "maxiclaude",
                      "baseUrl": "http://127.0.0.1:8080/v1",
                      "apiKey": "dummy",
                      "input": ["text", "image"],
                      "contextWindow": 32768,
                      "maxOutputTokens": 8192,
                      "compat": { "thinkingFormat": "qwen-chat-template" }
                    }
                  ]
                }
                """);
            return 2;
        }
        if (!string.IsNullOrEmpty(args.ApiKey))
        {
            if (model.Provider != "openai-completions")
            {
                Err.WriteLine("Subscription connections do not accept API-key overrides.");
                return 2;
            }
            model = model with { ApiKey = args.ApiKey };
        }
        var ownModel = model;
        if (!runsSubscription && localProfile is { } profile)
        {
            model = SubscriptionAssignment.WithProfile(model, profile);
            settings = SubscriptionAssignment.WithCompaction(settings, model);
            Err.WriteLine($"subscription: profile on {model.Id}: compaction at {settings.CompactionTriggerTokens} tokens down to {settings.CompactionTargetTokens}, output {model.MaxOutputTokens}");
        }
        else if (!runsSubscription && !runsRouter) settings = SubscriptionAssignment.WithDefaultCompaction(settings, model);
        // The seat's own effort: settings.json, else the model's. An effort picked on the assignment wins over it.
        var ownThinking = settings.DefaultThinkingLevel ?? model.DefaultThinkingLevel;
        SeatAssignmentSwitch? assignment = null;
        // Set once the seat's telemetry is open: a fallback to the subscription is a telemetry event (seat events, kind fallback).
        Action<string, string, DateTimeOffset>? recordFallback = null;
        if (subscriptionBroker is not null && brokerToken is not null && loadedRevision is not null && StreamFactory is null)
        {
            try
            {
                if (localModels.Count == 0 && ModelsFile.FromEnvironment() is { } envLocal) localModels.Add(envLocal);
            }
            catch (InvalidOperationException ex)
            {
                Err.WriteLine(ex.Message);
                return 2;
            }
            var localModel = runsSubscription || runsRouter ? ResolveModel(localModels, null, baseSettings.DefaultModel) : ownModel;
            assignment = new SeatAssignmentSwitch(subscriptionBroker, brokerToken, brokerCa, loadedRevision, runsSubscription,
                model, localModel, baseSettings)
            {
                Profiled = runsRouter || (!runsSubscription && localProfile is not null), Log = line => Err.WriteLine("subscription: " + line),
                Effort = assignedEffort, DefaultEffort = ownThinking,
                Hybrid = localModel is not null && (runsSubscription || runsRouter && assignedHybrid is { IsPair: true }) ? assignedHybrid : null,
                OnFallback = (local, code, until) => recordFallback?.Invoke(local, code, until),
            };
            // A hybrid seat whose every turn runs locally sizes its context for the local side from the start.
            if (assignment.Hybrid is not null) settings = assignment.CompactionFor(settings);
        }

        // Verify the compaction trigger against the provider's real per-request context limit (adr-pi-sharp-ctx-
        // ceiling-check). A trigger at/above the limit means compaction can never fire, so fail fast before the
        // session starts. Unreachability is a network blip: it must not affect the run, so its warnings are
        // informational and never fatal; only a genuine contradiction exits 2. JSON stdout stays parseable.
        var ceiling = model.Provider != "openai-completions"
            ? ContextCeilingVerifier.Evaluate(model.ContextWindow, settings.ToCompactionSettings().ReserveFor(model), null)
            : await ContextCeilingVerifier.VerifyAsync(
                model, settings.ToCompactionSettings().ReserveFor(model), CeilingHandler, cancellationToken)
            .ConfigureAwait(false);
        if (!ceiling.Ok)
        {
            Err.WriteLine($"ceiling: {ceiling.Error}");
            return 2;
        }
        var ceilingOutput = args.Mode == CliMode.Json ? Err : Out;
        foreach (var warning in ceiling.Warnings)
            ceilingOutput.WriteLine($"ceiling: {warning}");
        if (ceiling.Verified)
        {
            var policy = settings.ToCompactionSettings();
            var effectiveTrigger = Math.Min(ceiling.TriggerTokens, policy.TriggerTokens ?? int.MaxValue);
            ceilingOutput.WriteLine($"context ceiling calibrated: trigger={effectiveTrigger}{(policy.KeepRecentTokens is int keep ? $" keep={keep}" : "")}{(policy.TargetTokens is int target ? $" target={target}" : "")} hardInputLimit={ceiling.TriggerTokens} providerLimit={ceiling.ProviderLimit}");
        }

        // The steps of a start with their times in the seat log: team-lead-2's start has spun a core for 30-60 minutes after
        // the ceiling line with nothing logged (pi-sharp-team-lead-startup-cpu-spin, 2026-10-22..24); the last step shown is
        // the one that spins.
        // A normal start logs nothing; one still going after a minute says, once a minute, which step it is in.
        var startClock = System.Diagnostics.Stopwatch.StartNew();
        var startStep = "connecting MCP servers";
        var startErr = Err;
        using var startWatch = new Timer(_ => startErr.WriteLine(FormattableString.Invariant(
            $"startup: still in '{Volatile.Read(ref startStep)}' after {startClock.Elapsed.TotalSeconds:0}s")), null, StartWatchEvery, StartWatchEvery);
        void StartStep(string step)
        {
            if (step == "ready; first run") startWatch.Change(Timeout.Infinite, Timeout.Infinite);
            Volatile.Write(ref startStep, step);
        }
        StartStep("connecting MCP servers");
        await using var mcp = await ConnectMcpAsync(cwd, configDir, settings, cancellationToken).ConfigureAwait(false);
        StartStep("MCP servers connected; loading skills and prompt templates");

        // guard-rules-4: declared early so BuildTools' closure can read it (assigned below at state creation).
        CliState? state = null;
        // pi-sharp-hh-6: the budget-reminder hook and the context_remaining tool share the task's budget
        // (assigned when RunPromptAsync wraps the stream).
        var taskBudget = BudgetRef;
        // pi-sharp-hh-11: the explore tool's observer slot (assigned in the notebook block, wired when the
        // tracer is created below), declared here so both blocks share the scope.
        Action<AgentEvent>[]? exploreToolObserver = null;

        // The effort picked on the Subscriptions tab assignment wins over settings.json; a command-line flag over both.
        var thinking = args.Thinking ?? assignedEffort ?? ownThinking;
        var localSkillResult = args.NoSkills
            ? new PiSharp.Core.Skills.LoadSkillsResult([], [])
            : PiSharp.Core.Skills.SkillLoader.Load(cwd, configDir, settings.Skills, includeDefaults: true);
        foreach (var diagnostic in localSkillResult.Diagnostics.Where(d => d.Type is "collision" or "warning"))
            Err.WriteLine($"skill {diagnostic.Type}: {diagnostic.Message}");
        var localSkills = (IReadOnlyList<Skill>)localSkillResult.Skills;
        var remoteSkills = args.NoSkills
            ? (IReadOnlyList<PiSharp.Core.Skills.Skill>)[]
            : PiSharp.Core.Skills.SkillLoader.FromMcpResources(mcp.SkillResources());
        var skills = new PiSharp.Core.Skills.SkillIndex(localSkills, remoteSkills).Skills;
        var templates = args.NoPromptTemplates
            ? (IReadOnlyList<PromptTemplate>)[]
            : PiSharp.Core.Skills.PromptTemplateLoader.LoadLocal(cwd, configDir, settings.Prompts, includeDefaults: true);
        var mcpTemplates = args.NoPromptTemplates
            ? (IReadOnlyList<PiSharp.Core.Skills.PromptTemplate>)[]
            : PiSharp.Core.Skills.PromptTemplateLoader.LoadAll(cwd, mcp, configDir, settings.Prompts, includeDefaults: true)
                .Where(t => t.Source == PiSharp.Core.Skills.PromptSourceKind.Mcp)
                .ToList();
        StartStep("skills and templates loaded; building tools");
        var contextFiles = settings.SkipContextFiles
            ? (IReadOnlyList<ContextFile>)[]
            : ContextFiles.Load(cwd, configDir);
        // A factory seat reads no AGENTS.md/CLAUDE.md: its standing text comes from the KB once its identity is verified
        // (below, after the preflight), because shared texts live in the KB, where one edit reaches every seat
        // (digital-scribe, 2026-10-13: "no more .md files on disk").
        string? seatStanding = null;
        if (settings.Factory is { Attached: true }) contextFiles = [];

        var checkpointGreenTracker = new CheckpointGreenTracker(settings.CheckpointGreenCommands)
        {
            HintFailures = settings.CheckpointHintFailures ?? CheckpointGreenTracker.DefaultHintFailures,
        };
        Func<string, bool, IReadOnlyList<IAgentTool>> defaultTools = (dir, images) => BuiltinTools.Create(
            dir, images, mcp,
            commandExitSink: checkpointGreenTracker is null ? null : (command, exit) => checkpointGreenTracker.OnCommandExit(command, exit));
        var localTools = (ToolsFactory ?? defaultTools)
            (cwd, model.SupportsImages);
        foreach (var readTool in localTools.OfType<ReadTool>())
        {
            readTool.WarningPixels = Math.Max(0, settings.ImageWarnPixels ?? ImageWarnings.DefaultWarnPixels);
            readTool.ContextWindow = model.ContextWindow;
            readTool.WarningSink = warning => Err.WriteLine(warning);
        }
        var artifacts = settings.NotebookDirectory is { Length: > 0 } artifactRoot
            ? new ArtifactStore(Path.Combine(ConfigPaths.ExpandHome(artifactRoot), "artifacts")) : null;
        // adr-visual-toolkits: the desktop toolkit, offered only where a DISPLAY and its binaries are (the demo seat).
        // Captures published from desktop__capture go to the factory KB under the seat's own identity (adr-visual-toolkits section 6).
        var capturePublisher = settings.Factory is { Attached: true, KbUrl: { Length: > 0 } captureKb } captureSettings
            && (captureSettings.ResolveToken() ?? settings.Cubicle?.ResolveToken()) is { Length: > 0 } captureToken
            ? new PiSharp.Tools.Desktop.HttpCapturePublisher(captureKb, captureToken) : null;
        // plan-pi-sharp-codex item 20: the seat's requests, tool calls, guard decisions and turn diffs, to the hub.
        await using var telemetry = settings.Telemetry != false && settings.Factory is { Attached: true, KbUrl: { Length: > 0 } telemetryKb } telemetrySettings
            && telemetrySettings.ResolveToken() is { Length: > 0 } telemetryToken
            ? SeatTelemetry.ForHub(telemetryKb, telemetryToken).Start() : null;
        if (telemetry is not null)
            recordFallback = (local, code, until) => telemetry.Record(new SeatEvent(DateTime.UtcNow, SeatEvent.Fallback, local, false,
                Detail: new System.Text.Json.Nodes.JsonObject
                {
                    ["code"] = code, ["to"] = assignment?.SubscriptionModelId,
                    ["until"] = until.UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                }));
        if (ToolsFactory is null)
        {
            var desktop = PiSharp.Tools.Desktop.DesktopToolkit.Create(artifacts, model.SupportsImages, publisher: capturePublisher);
            // A seat whose captures are published takes its screen shots through desktop__capture only.
            if (desktop.Count > 0 && capturePublisher is not null)
                foreach (var shell in localTools.OfType<BashTool>()) shell.CommandRefusal = PiSharp.Tools.Desktop.DesktopToolkit.ScreenGrabRefusal;
            localTools = [.. localTools, .. desktop];
        }
        IReadOnlyList<IAgentTool> executable = [];
        NotebookStore? notebook = null;
        var discovery = artifacts is null ? null : new ToolDiscovery(() => executable);
        if (artifacts is not null) localTools = [.. localTools, new ArtifactTool(artifacts), discovery!];
        // The plan tool on an attached seat follows the factory switch plan-tool (the Engine Room page), read at start and at
        // each new task; the seat setting planTool forces it. Off, the tool is left out of the list the model sees.
        var planToolOn = settings.PlanTool == true;
        Func<CancellationToken, Task<bool>>? planToolSwitch = null;
        IReadOnlyList<IAgentTool> BuildTools()
        {
            // guard-rules-4: the completion-rule gate wraps kb_transition when the settings require a harness test run before
            // review/done. (The older VerifiedTransitionTool is gone: it read "status" and "slug", which the argument check
            // renames to to_status and work_item, so it never fired; brought back to life it would have refused every seat's
            // hand-in for want of a check record no seat writes. Proof is the KB's: attrs.proof and proof_evidence.)
            bool guardTransition = notebook is not null && settings.GuardRequireTestRunBeforeReview == true;
            executable = SelectTools(localTools.Where(t => planToolOn || t is not PlanTool).Concat(mcp.Tools()).ToList(), args).Select(t =>
            {
                IAgentTool selected = t;
                // plan-pi-sharp-codex items 14-15: an epic is handed in only with each criterion's evidence, and blocked on a
                // person only after an audit, each while its switch is on (the Engine Room page) or the seat's goalAudits forces it.
                if (settings.GoalAudits != false && t is IRemoteMcpAgentTool { RemoteName: "kb_transition" })
                {
                    Func<FactorySteering?> handInFactory = () => state?.Factory;
                    var forced = settings.GoalAudits;
                    selected = new HandInGate(selected, (slug, ct) => handInFactory()?.KbHandInAsync(slug, ct) ?? Task.FromResult<FactoryHandIn?>(null),
                        enabled: (name, ct) => forced is { } force ? Task.FromResult(force)
                            : handInFactory()?.SwitchOnAsync(name, ct) ?? Task.FromResult(false));
                }
                // Status goes out on a state change only (2026-10-20).
                if (t is IRemoteMcpAgentTool { RemoteName: "chat_manage" }) selected = new PiSharp.Core.Factory.StatusGateTool(selected);
                if (guardTransition && t is IRemoteMcpAgentTool { RemoteName: "kb_transition" })
                {
                    var store = notebook!;
                    Func<FactorySteering?> factoryRef = () => state?.Factory;
                    Func<string?> sessionIdRef = () => state?.SessionId;
                    selected = new PiSharp.Core.Guard.GuardTransitionGate(selected, store,
                        () => new PiSharp.Core.Guard.GuardCompletionRule("test-run-before-review"),
                        () => factoryRef()?.CurrentTaskKey ?? sessionIdRef() ?? "session");
                }
                return artifacts is null || selected is ToolDiscovery or ArtifactTool or NotebookTool
                    ? selected : (IAgentTool)new BoundedTool(selected, artifacts);
            }).ToArray();
            return executable;
        }
        var tools = BuildTools();
        string BuildSystemPrompt(IReadOnlyList<IAgentTool> selectedTools, IReadOnlyList<Skill> selectedSkills)
        {
            var appended = seatStanding is null ? args.AppendSystemPrompt : [.. args.AppendSystemPrompt, seatStanding];
            var append = appended.Count == 0 ? null : string.Join("\n\n", appended);
            return SystemPrompt.Build(new SystemPromptOptions
            {
                Cwd = cwd,
                CustomPrompt = args.SystemPrompt,
                AppendSystemPrompt = append,
                SelectedTools = selectedTools.Select(t => t.Name).ToList(),
                ToolSnippets = SystemPrompt.SnippetsFrom(selectedTools),
                ContextFiles = contextFiles,
                Skills = selectedSkills,
                RemoteSkillsPromptCap = Math.Max(0, settings.RemoteSkillsPromptCap ?? 20),
                NotebookMemory = settings.NotebookDirectory is { Length: > 0 },
                ModelFamily = ModelFamilyOf(model),
                ProjectDocMaxBytes = settings.ProjectDocMaxBytes is > 0 ? settings.ProjectDocMaxBytes.Value : 32 * 1024,
            });
        }

        StartStep("tools built; system prompt");
        var systemPrompt = BuildSystemPrompt(tools, skills);

        var context = new AgentContext
        {
            SystemPrompt = systemPrompt,
            Tools = tools.ToList(),
        };

        var persist = !args.NoSession;
        var sessionDir = string.IsNullOrWhiteSpace(args.SessionDir)
            ? ConfigPaths.SessionDir(cwd, configDir)
            : Path.GetFullPath(ConfigPaths.ExpandHome(args.SessionDir), cwd);
        // Seat migration (kb 1.0.85): before any session opens, an attached seat says which release it runs and packs or
        // resumes what the KB has for it. A packed seat starts as usual (its items have moved); a resuming one opens the
        // packet's session instead of a new one.
        using var migrator = persist && string.IsNullOrWhiteSpace(args.Session) && settings.Factory is { Attached: true, KbUrl: { Length: > 0 } migrationKb } migrationSettings
            && migrationSettings.ResolveToken() is { Length: > 0 } migrationToken
            ? new SeatMigrator(migrationKb, migrationToken, SeatRelease.PiSharp(Version))
            {
                Actor = migrationSettings.Actor!, WorkDir = cwd, SessionDir = sessionDir,
                NotebookDir = settings.NotebookDirectory is { Length: > 0 } migrationNotebook ? ConfigPaths.ExpandHome(migrationNotebook) : null,
                Log = line => Err.WriteLine(line),
            }
            : null;
        StartStep("seat migration check");
        var migration = migrator is null ? MigrationStart.None : await migrator.StartAsync(cancellationToken).ConfigureAwait(false);
        SessionManager? migratedMgr = null;
        if (migration.SessionFile is { } migratedSession)
        {
            try { migratedMgr = SessionManager.Open(migratedSession, cwd); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or ArgumentException or InvalidOperationException)
            {
                // The target cannot use it: the KB gives the items and the packet back to the source.
                await migrator!.RefuseAsync(migration.ResumedId!.Value, "could not open the session: " + ex.Message, cancellationToken).ConfigureAwait(false);
                migration = MigrationStart.None;
            }
        }
        SessionManager sessionMgr;
        try
        {
            StartStep("opening the session");
            sessionMgr = migratedMgr ?? OpenSession(args, cwd, sessionDir, persist);
            StartStep(FormattableString.Invariant($"session open ({sessionMgr.BuildContextEntries().Count} context entries)"));
        }
        catch (Exception ex)
        {
            Err.WriteLine(ex.Message);
            return 2;
        }
        if (migration.ResumedId is { } resumedId) await migrator!.ResumedAsync(resumedId, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(args.Name))
            sessionMgr.AppendSessionInfo(args.Name);

        var readTracker = localTools.OfType<ReadTool>().Select(tool => tool.ReadTracker).FirstOrDefault(tracker => tracker is not null);

        using var router = StreamFactory is null
            ? assignment is { } followed
                // A seat that follows its assignment keeps the broker for a switch to a subscription model, and may
                // call its local model only while that is what it is assigned.
                ? new SubscriptionRouter(configDir, subscriptionBroker, settings.Factory?.ResolveToken(), brokerCa, () => followed.RunsSubscription)
                {
                    OnModelHealth = e => _ = state?.Factory?.NoteModelHealthAsync(e.Healthy, "openai-completions", e.Since, e.Detail),
                    // A hybrid seat whose provider fails a turn runs it on its own model instead of waiting (2026-10-25).
                    HandBackOnProviderTrouble = () => followed.CanHandBack,
                    OnProviderTrouble = followed.NoteProviderTrouble,
                    // And the other way: a local model that fails repeatedly gives its turns to the subscription (2026-10-29).
                    FailOverOnLocalTrouble = () => followed.CanFailOver,
                    OnLocalTrouble = followed.NoteLocalTrouble,
                    OnLocalAnswer = followed.NoteLocalAnswer,
                    OnWait = w => _ = state?.Factory?.NoteModelWaitAsync(w.Line),
                }
                : new SubscriptionRouter(configDir, runsSubscription ? subscriptionBroker : null, settings.Factory?.ResolveToken(), runsSubscription ? brokerCa : null)
                {
                    OnModelHealth = e => _ = state?.Factory?.NoteModelHealthAsync(e.Healthy, "openai-completions", e.Since, e.Detail),
                    OnWait = w => _ = state?.Factory?.NoteModelWaitAsync(w.Line),
                }
            : null;
        var stream = StreamFactory is { } streamFactory ? streamFactory(model) : router!.Stream;
        var agent = new AgentSession(sessionMgr, context, settings.ToCompactionSettings(), OverflowDetectorFor(model))
        {
            Notice = line => Err.WriteLine("[compaction] " + line),
        };

        var last = sessionMgr.BuildSessionContext();
        if (last.ModelProvider != model.Provider || last.ModelId is null || !string.Equals(last.ModelId, model.Id, StringComparison.Ordinal))
            sessionMgr.AppendModelChange(model.Provider, model.Id);

        // Seats start in ~/work, not in a checkout, so no store is opened here; sweep the checkpoints directory
        // anyway: stores of deleted checkouts and stores with an LFS copy go at start (strata, 2026-10-22).
        if (persist && sessionDir is { Length: > 0 })
        {
            try { CheckpointStore.PruneOrphans(CheckpointStore.DefaultDir(sessionDir)); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }

        state = new CliState
        {
            Cwd = cwd,
            Model = model,
            Thinking = thinking,
            Models = models,
            Skills = skills,
            Templates = templates,
            ConfigDir = configDir,
            McpTemplates = mcpTemplates,
            Mcp = mcp,
            // codeintel epic: the after-edit diagnostics hook, wired only when feedback is on and the
            // 'code' server connected. Composed outermost in WithMessageSources (PrepareNextTurn).
            CodeIntelFeedback = CreateCodeIntelFeedback(cwd, mcp, settings),
            Agent = agent,
            Context = context,
            Settings = settings,
            Assignment = assignment,
            EffectiveCommandHooks = settings.CommandHooks ?? [],
            Telemetry = telemetry,
            Stream = stream,
            SessionName = args.Name,
            SessionDir = persist ? sessionDir : null,
            SessionId = sessionMgr.SessionId,
            Persist = persist,
            // CP1: one shared store for the whole run (snapshot hook + CP2 RewindTool), null when
            // checkpoints are off or there is no session dir to anchor it.
            Checkpoints = settings.CheckpointsEnabled == true && (persist ? sessionDir : null) is { Length: > 0 } cpDir
                && CheckpointStore.ResolveWorkTreeRoot(cwd) is { } initialRoot
                ? new CheckpointStore(initialRoot, CheckpointStore.DefaultDir(cpDir))
                : null,
        };
        // CP3: wire the green-marker tracker into the store + session. Only active when checkpoints
        // are on (state.Checkpoints non-null); otherwise the tracker stays inert (no MarkGreen, no
        // AfterToolBatch hook) and the command-exit sink is harmless.
        if (settings.CheckpointsEnabled == true && state.SessionDir is { Length: > 0 })
        {
            checkpointGreenTracker.MarkGreen = _ => MarkLatestCheckpointGreen(state, state.Checkpoints, checkpointGreenTracker);
            state.CheckpointGreen = checkpointGreenTracker;
        }

        // Register against the exact store instance used by the snapshot hook; a second instance
        // would split the checkpoint session's shadow GitDir and active store state.
        if (ToolsFactory is null && readTracker is not null && settings.CheckpointsEnabled == true && state.SessionDir is { Length: > 0 } rewindSessionDir
            && CheckpointStore.ResolveWorkTreeRoot(cwd) is { } listRoot)
        {
            var listStore = new CheckpointStore(listRoot, CheckpointStore.DefaultDir(rewindSessionDir));
            localTools = [.. localTools,
                new RewindTool(cwd, CheckpointStore.DefaultDir(rewindSessionDir), sessionMgr, readTracker),
                // CP6 (pi-sharp-cp6): the read-only list surface, anchored on the current work-tree root.
                new CheckpointListTool(cwd, listStore, sessionMgr)];
            tools = BuildTools();
            context.Tools = tools.ToList();
            context.SystemPrompt = BuildSystemPrompt(tools, skills);
        }

        var mcpRefreshPending = 0;
        void MarkMcpRefreshPending() => Interlocked.Exchange(ref mcpRefreshPending, 1);
        Action<string> mcpChanged = _ => MarkMcpRefreshPending();
        mcp.ListsChanged += mcpChanged;

        async Task RefreshMcpAsync(CancellationToken refreshCancellationToken, bool force = false)
        {
            // A new task reads the seat's role texts again first (at most every two minutes; SeatStanding).
            if (state.RefreshStanding is { } refreshStanding) await refreshStanding(refreshCancellationToken).ConfigureAwait(false);
            // ...and the plan-tool switch (at most every two minutes): a flip rebuilds the tool list below.
            if (planToolSwitch is { } readPlanSwitch && await readPlanSwitch(refreshCancellationToken).ConfigureAwait(false) != planToolOn)
            {
                planToolOn = !planToolOn;
                MarkMcpRefreshPending();
                Err.WriteLine($"factory: the plan tool is {(planToolOn ? "on" : "off")} (switch plan-tool)");
            }
            // A stateless server never says its list changed: ask (at most once a minute); a change marks the refresh pending.
            await mcp.PollToolsAsync(DateTime.UtcNow, refreshCancellationToken).ConfigureAwait(false);
            if (!force && Interlocked.Exchange(ref mcpRefreshPending, 0) == 0) return;
            refreshCancellationToken.ThrowIfCancellationRequested();

            var refreshedRemoteSkills = args.NoSkills
                ? (IReadOnlyList<PiSharp.Core.Skills.Skill>)[]
                : PiSharp.Core.Skills.SkillLoader.FromMcpResources(mcp.SkillResources());
            var refreshedSkills = new PiSharp.Core.Skills.SkillIndex(localSkills, refreshedRemoteSkills).Skills;
            var refreshedMcpTemplates = args.NoPromptTemplates
                ? (IReadOnlyList<PiSharp.Core.Skills.PromptTemplate>)[]
                : PiSharp.Core.Skills.PromptTemplateLoader.LoadAll(cwd, mcp, configDir, settings.Prompts, includeDefaults: true)
                    .Where(t => t.Source == PiSharp.Core.Skills.PromptSourceKind.Mcp)
                    .ToList();
            var refreshedTools = BuildTools();

            state.Skills = refreshedSkills;
            state.McpTemplates = refreshedMcpTemplates;
            state.Context.Tools = refreshedTools.ToList();
            state.Context.SystemPrompt = BuildSystemPrompt(refreshedTools, refreshedSkills);
        }

        ProcessedFiles? files = null;
        if (args.FileArgs.Count > 0)
        {
            try
            {
                files = Attachments.Process(
                    args.FileArgs,
                    cwd,
                    model.ContextWindow,
                    Math.Max(0, settings.ImageWarnPixels ?? ImageWarnings.DefaultWarnPixels),
                    warning => Err.WriteLine(warning),
                    model.SupportsImages);
            }
            catch (Exception ex)
            {
                Err.WriteLine(ex.Message);
                return 2;
            }
        }

        var prompt = string.Join(' ', args.Messages);
        if (migration.Note is { } migrationNote) prompt = migrationNote + "\n" + prompt;
        if (args.Mode != CliMode.Repl && prompt.Length == 0 && files is null)
        {
            var stdin = await In.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            prompt = stdin.TrimEnd();
        }

        var ansi = new Ansi(Color);
        IEventSink sink = args.Mode == CliMode.Json
            ? new JsonEventSink(Out, Err)
            : new ConsoleRenderer(
                Out,
                Err,
                ansi,
                settings.HideThinkingBlock,
                printFinalOnly: args.Mode == CliMode.Print,
                interactive: args.Mode == CliMode.Repl);

        CubicleClient? cubicleClient = null;
        CubicleTracer? tracer = null;
        var cubicleUrl = args.CubicleUrl ?? settings.Cubicle?.Url;
        if (!string.IsNullOrWhiteSpace(cubicleUrl))
        {
            var tokenEnv = args.CubicleTokenEnv ?? CubicleSettings.TokenEnv;
            var token = args.CubicleTokenEnv is { } namedTokenEnv
                ? Environment.GetEnvironmentVariable(namedTokenEnv)
                : settings.Cubicle?.ResolveToken() ?? Environment.GetEnvironmentVariable(CubicleSettings.TokenEnv);
            if (string.IsNullOrWhiteSpace(token))
            {
                Err.WriteLine($"cubicle: environment variable {tokenEnv} is empty; cubicle disabled.");
            }
            else
            {
                cubicleClient = new CubicleClient(cubicleUrl, token, CubicleHandler);
                var cacheStats = new CacheStats();
                tracer = new CubicleTracer(
                    cubicleClient,
                    settings.Cubicle?.About,
                    cacheMissLine: () =>
                    {
                        var seat = state.CacheSeatId ?? "local";
                        return cacheStats.CurrentLine?.FormatLine(seat, HostSuffix()) ?? "";
                    },
                    taskKey: () => state.Factory?.CurrentTaskKey,
                    workItem: () => state.Factory?.CurrentWorkItem,
                    context: () =>
                    {
                        // A hybrid seat shows the window it is sized for, not the side the turn happens to run on
                        // (digital-scribe, 2026-10-16: "fix the web-ui display of the context in this hybrid configuration"),
                        // and a seat with a budget shows the budget as its window, as the Subscriptions tab does (2026-10-20).
                        var sized = state.Assignment?.SizingModel ?? state.Model;
                        var window = sized.CompactAtTokens ?? sized.ContextWindow;
                        var compaction = state.Agent.CompactionSettings;
                        // Where it really compacts: the trigger, or the physical window less the reserve when that is lower.
                        var compactAt = compaction.Enabled
                            ? Math.Min(sized.ContextWindow - compaction.ReserveFor(sized), compaction.TriggerTokens ?? int.MaxValue)
                            : 0;
                        return new ContextGauge(state.Agent.EstimatedContextTokens(), window, Math.Max(0, compactAt));
                    });
                if (exploreToolObserver is { } exploreSlot)
                    exploreSlot[0] = evt => { try { tracer!.OnEvent(evt); } catch { /* tracing never fails the run */ } };
                state = state with
                {
                    Steering = new CubicleSteering(cubicleClient) { FollowUpWaitSeconds = settings.Cubicle?.FollowUpWaitSeconds ?? 0 },
                    CacheStats = cacheStats,
                };
                sink = new TracingSink(sink, tracer);
            }
        }

        FactoryClient? factoryClient = null;
        if (settings.Factory is { Attached: true } factorySettings)
        {
            var preflight = await PreflightFactoryAsync(
                factorySettings,
                settings.Cubicle,
                args.Mode,
                cancellationToken).ConfigureAwait(false);
            factoryClient = preflight.Client;
            if (preflight.Exit is { } factoryExit)
            {
                factoryClient?.Dispose();
                return factoryExit;
            }
            if (factoryClient is not null)
                foreach (var bashTool in localTools.OfType<BashTool>())
                    bashTool.NameHint = factoryClient.KbResolveAsync;
            // Only after the identity check: the KB is never asked anything on behalf of an unverified seat.
            if (factoryClient is not null && preflight.Steering is not null && preflight.Actor is { Length: > 0 } standingActor)
            {
                var standingClient = factoryClient;
                // A cap of its own (SeatStanding.DefaultMaxBytes, 96 KiB): a long job description cannot crowd out the work;
                // the seat reads the rest from the KB.
                var standingTexts = new SeatStanding(async (whole, ct) =>
                    await standingClient.KbSeatStandingTextAsync(standingActor, ct, whole).ConfigureAwait(false) is { } standing
                        ? SystemPrompt.CapInstructions(standing, settings.SeatStandingMaxBytes is > 0 ? settings.SeatStandingMaxBytes.Value : SeatStanding.DefaultMaxBytes,
                            $"read the rest with kb_get {standingActor}")
                        : null);
                if (await standingTexts.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    seatStanding = standingTexts.Current;
                    state.Context.SystemPrompt = BuildSystemPrompt(tools, skills);
                }
                // Read again at a new task and between turns (SeatStanding): a role text edited in the KB is the seat's from its
                // next request, without a restart.
                state.RefreshStanding = async ct =>
                {
                    if (!await standingTexts.RefreshAsync(ct).ConfigureAwait(false)) return;
                    seatStanding = standingTexts.Current;
                    state.Context.SystemPrompt = BuildSystemPrompt(state.Context.Tools, state.Skills);
                    Err.WriteLine($"factory: role texts changed in the KB; read again for {standingActor} ({seatStanding?.Length ?? 0} characters)");
                };
            }
            if (preflight.Steering is not null)
            {
                // Idle, the seat still asks; the switch itself is applied when the next turn starts (FollowAssignment).
                if (assignment is { } idleAssignment)
                {
                    preflight.Steering.CheckAssignmentWhileIdle = ct => idleAssignment.CheckAsync(false, ct);
                    // A hybrid seat whose allowance is spent runs on its own model: it keeps its work (rule rate-limit-handover v10).
                    preflight.Steering.RunsElsewhereOnQuota = () => idleAssignment.Hybrid is not null && idleAssignment.HybridLocal is not null;
                }
                state = state with { Factory = preflight.Steering, FactoryActor = preflight.Actor, SelfUpdate = SelfUpdateFor(settings, preflight.Steering) };
                state.Agent.Hooks = FactoryCompactionHooks(preflight.Steering, preflight.Actor, cwd);
                // Compactions per seat go to the hub with the rest of its telemetry (the context epic's "compaction count").
                if (state.Telemetry is { } compactions)
                    state.Agent.Compacted = entry => compactions.Record(new SeatEvent(DateTime.UtcNow, SeatEvent.Compaction, entry.Trigger, true,
                        TokensIn: entry.TokensBefore, Detail: new System.Text.Json.Nodes.JsonObject
                        {
                            ["tokens_after"] = entry.TokensAfter, ["save_before_compact"] = entry.SaveBeforeCompact, ["open_tasks"] = entry.OpenTasks,
                        }));
            }
        }

        if (settings.CommandHooksFromKb == true && state.Factory is { } hookFactory)
        {
            try
            {
                var ruleDocuments = await hookFactory.KbActiveRuleDocumentsAsync(cancellationToken).ConfigureAwait(false);
                if (ruleDocuments is not null)
                    state = state with { EffectiveCommandHooks = [.. CommandHookRuleParser.ParseDocuments(ruleDocuments), .. state.EffectiveCommandHooks] };
                else Err.WriteLine("[hook] active KB rule hooks unavailable; using local hook settings only");
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                Err.WriteLine("[hook] active KB rule hooks unavailable; using local hook settings only: " + ex.Message);
            }
        }

        // The guard runs on every seat's main loop (plan-pi-sharp-codex item 1): until 2026-10-25 only the reviewer's sub-run
        // had one, and the seats' rules only logged what they would have blocked.
        var guardMode = settings.GuardMode?.ToLowerInvariant() switch
        {
            "off" => PiSharp.Core.Guard.GuardMode.Off,
            "shadow" => PiSharp.Core.Guard.GuardMode.Shadow,
            "enforce" => PiSharp.Core.Guard.GuardMode.Enforce,
            _ => state.Factory is not null ? PiSharp.Core.Guard.GuardMode.Enforce : PiSharp.Core.Guard.GuardMode.Shadow,
        };
        // Helper roles a librarian defined in the active KB rules (fenced helper-role blocks), read with the guard rules.
        IReadOnlyDictionary<string, TaskTool.Role> helperRoles = new Dictionary<string, TaskTool.Role>();
        if (guardMode != PiSharp.Core.Guard.GuardMode.Off)
        {
            StartStep("loading the guard rules");
            IReadOnlyList<string> guardDocuments = [];
            IReadOnlyCollection<string>? roles = null;
            if (state.Factory is { } guardFactory)
            {
                try
                {
                    guardDocuments = await guardFactory.KbActiveRuleDocumentsAsync(cancellationToken).ConfigureAwait(false) ?? [];
                    helperRoles = TaskTool.ParseRoles(guardDocuments);
                    roles = await guardFactory.KbActorRolesAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                { Err.WriteLine("[guard] KB rules or roles unavailable; built-in rules only: " + ex.Message); }
            }
            string? localRules = null;
            if (settings.GuardRulesFile is { Length: > 0 } rulesFile && File.Exists(ConfigPaths.ExpandHome(rulesFile)))
                localRules = File.ReadAllText(ConfigPaths.ExpandHome(rulesFile));
            try
            {
                var rules = PiSharp.Core.Guard.ToolGuard.RulesFromSources(localRules, guardDocuments, roles, line => Err.WriteLine("[guard] " + line));
                var guard = new PiSharp.Core.Guard.ToolGuard(rules, guardMode, line => Err.WriteLine("[guard] " + line));
                state = state with
                {
                    SeatGuard = guard,
                    // The seat's model at review time: the state variable, not this copy (the model follows the assignment).
                    ActionReviewer = new PiSharp.Core.Guard.ModelActionReviewer(ReviewStream(state, "action-review"), () => state!.Model) { Log = Err.WriteLine },
                };
                if (state.Factory is not null)
                    Err.WriteLine($"[guard] {guardMode.ToString().ToLowerInvariant()}: {guard.Rules.Count} rule(s) for roles {(roles is null ? "unknown" : string.Join(",", roles))}");
            }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or ArgumentException)
            { Err.WriteLine("[guard] rules do not load; no guard: " + ex.Message); }
        }

        // What the model's commands see of the seat's environment (plan-pi-sharp-codex item 3). Process-wide (every command and
        // job), put back when this run ends.
        var previousShellPolicy = PiSharp.Tools.BashTool.EnvironmentPolicy;
        PiSharp.Tools.BashTool.EnvironmentPolicy = new PiSharp.Tools.ShellEnvironmentPolicy(
            settings.ShellEnvironmentExclude ?? (state.Factory is not null ? PiSharp.Tools.ShellEnvironmentPolicy.SecretNames : []),
            settings.ShellEnvironmentSet ?? new Dictionary<string, string>());
        if (state.Factory is not null && PiSharp.Tools.BashTool.EnvironmentPolicy.Exclude.Count > 0)
        {
            var withheld = Environment.GetEnvironmentVariables().Keys.OfType<string>().Where(PiSharp.Tools.BashTool.EnvironmentPolicy.Excludes).Order().ToList();
            Err.WriteLine($"[shell] commands run without {(withheld.Count == 0 ? "any withheld variable (none set)" : string.Join(", ", withheld))}");
        }

        StartStep("opening the notebook");
        using var notebookLease = notebook = settings.NotebookDirectory is { Length: > 0 } notebookDir
            ? new NotebookStore(ConfigPaths.ExpandHome(notebookDir), state.FactoryActor ?? "local") : null;
        using var embeddings = notebook is not null && settings.NotebookEmbeddingUrl is { Length: > 0 } embeddingUrl
            && settings.NotebookEmbeddingModel is { Length: > 0 } embeddingModel
            ? new PiSharp.Llm.NotebookEmbeddingsClient(embeddingUrl, embeddingModel,
                settings.NotebookEmbeddingApiKeyEnv is { Length: > 0 } keyEnv ? Environment.GetEnvironmentVariable(keyEnv) : null) : null;
        if (notebook is not null)
        {
            notebook.RecallScope = NotebookLessons.RepositoryScope(cwd);
            if (embeddings is not null) notebook.SemanticIndex = new NotebookSemanticIndex(notebook.DirectoryPath, notebook.Owner, embeddings);
        }
        StartStep("notebook open; syncing it");
        using var notebookSync = notebook is not null && state.FactoryActor is not null && settings.Factory?.ResolveToken() is { Length: > 0 } notebookToken
            ? new NotebookSync(settings.Factory.KbUrl ?? "http://192.168.8.81:8081/mcp", notebookToken) : null;
        if (notebookSync is not null)
        {
            try { await notebookSync.FlushAsync(notebook!, cancellationToken).ConfigureAwait(false); }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
            {
                Err.WriteLine("[notebook] startup reconciliation unavailable: " + ex.Message);
                if (notebook!.List().Count == 0) return 2; // never pretend a fresh cache proves there are no obligations
                Err.WriteLine("[notebook] continuing with durable local state; remote operations remain pending.");
            }
        }
        if (notebook is not null)
        {
            var now = DateTimeOffset.UtcNow;
            if (NotebookSources.ExpireStale(notebook, now, report: Err.WriteLine) is { Count: > 0 } expired)
                Err.WriteLine($"[notebook] cancelled {expired.Count} open sources older than 48 h");
            var archived = notebook.ArchiveUnused(now);
            if (archived.Count > 0) Err.WriteLine($"[notebook] archived {archived.Count} unused notes/decisions");
        }
        await using var jobs = notebook is null ? null : new JobTool(cwd, notebook) { MaxActive = settings.MaxJobs is > 0 ? settings.MaxJobs.Value : JobTool.DefaultMaxActiveJobs };
        TaskTool? helperTasks = null;
        if (jobs is not null)
        {
            // pi-sharp-hh-2: a bash command still running at its yield window is adopted by the job
            // registry (same cap and evidence rows), still running; null (registry full) makes bash stop it.
            foreach (var bashTool in localTools.OfType<BashTool>())
            {
                bashTool.YieldSink = running => Task.FromResult(
                    jobs!.Adopt(running, state.Factory?.CurrentTaskKey ?? state.Agent.Session.SessionId));
            }
        }
        if (notebook is not null)
        {
            state = state with { Notebook = notebook, NotebookSync = notebookSync, Artifacts = artifacts,
                NotebookShare = state.NotebookShare is { } keep ? keep : new NotebookShareTracker() };
            // write_stdin (Codex): type into a yielded command's session, read what it printed since.
            localTools = [.. localTools, new NotebookTool(notebook, () => state.Factory?.CurrentTaskKey ?? state.Agent.Session.SessionId), jobs!, new WriteStdinTool(jobs!)];
            // pi-sharp-hh-11: the read-only explore sub-run tool. Its own context + its own TaskBudget row
            // keep codebase questions out of the main context; one sub-run at a time per seat; the sub-run's
            // events go to the tracer when one exists (picked up lazily via the observer, because the tracer
            // is created later when the cubicle URL resolves).
            if (settings.ExploreTool != false)
            {
                var exploreObserver = new Action<AgentEvent>[1];
                localTools = [.. localTools, new ExploreTool(
                    cwd,
                    () => (state?.Model ?? model, state?.Stream ?? stream),
                    () => state?.Factory?.CurrentTaskKey ?? state?.Agent.Session.SessionId ?? "session",
                    notebook,
                    evt => exploreObserver[0]?.Invoke(evt))];
                exploreToolObserver = exploreObserver;
            }
            // plan-pi-sharp-codex items 16 and 17: helpers (Codex subagents) and the branch review (Codex /review).
            if (settings.TaskTool != false)
                localTools = [.. localTools, helperTasks = new TaskTool(cwd, () => (state?.Model ?? model, state?.Stream ?? stream),
                    () => state?.Factory?.CurrentTaskKey ?? state?.Agent.Session.SessionId ?? "session", notebook,
                    state.SeatGuard, state.ActionReviewer)
                {
                    // SubagentStart/SubagentStop around each helper, and the seat's other hooks inside it.
                    Hooks = () => state is null ? null : HookRunnerFor(state.EffectiveCommandHooks),
                    ExtraRoles = helperRoles,
                }];
            if (settings.ReviewTool != false)
                localTools = [.. localTools, new ReviewTool(cwd, () => (state?.Model ?? model, state is null ? stream : ReviewStream(state, "branch-review")),
                    () => state?.Factory?.CurrentTaskKey ?? state?.Agent.Session.SessionId ?? "session",
                    (entity, title, body, ct) => state?.Factory is { } kb ? kb.KbWriteNoteAsync(entity, title, body, ct) : Task.FromResult(false))];
            if (settings.PlanTool == true || settings.PlanTool is null && state.Factory is not null)
            {
                var verifier = new Verifier(cwd);
                // Check-gated done (PT3): run the harness verifier for the step's check, record the verify-
                // evidence, and only complete the step on a pass. Inline file:/grep: predicates parse directly;
                // an id reference looks up the task's declared checks from the spec or .pi/done-checks.json.
                async Task<(bool Passed, string? Error, string? VerifyRecordId)> runCheck(
                    PiSharp.Core.Verification.DoneCheck check, string task, NotebookStore store, CancellationToken ct)
                {
                    var one = await verifier.RunOneAsync(check, null, ct).ConfigureAwait(false);
                    var report = new PiSharp.Core.Verification.VerificationReport(task, [one], one.Passed, null);
                    var verifyId = PiSharp.Core.Verification.VerificationRecords.Record(store, report);
                    return (one.Passed, one.Passed ? null : (one.OutputTail.Length > 0 ? one.OutputTail : $"check '{check.Id}' failed"), verifyId);
                }
                localTools = [.. localTools, new PlanTool(notebook, runCheck)
                {
                    // Plan steps on the epic the seat holds go into the epic's doc plan (FactorySteering.KbWritePlanStepsAsync).
                    Changed = (task, steps, why, ct) => state?.Factory is { } planFactory
                        ? planFactory.KbWritePlanStepsAsync(task, PiSharp.Core.Factory.PlanDoc.Render(steps, DateTimeOffset.UtcNow, why), ct)
                        : Task.CompletedTask,
                }];
                if (settings.PlanTool is null && state.Factory is { } switchFactory)
                {
                    planToolSwitch = ct => switchFactory.SwitchOnAsync("plan-tool", ct);
                    planToolOn = await planToolSwitch(cancellationToken).ConfigureAwait(false);
                    if (planToolOn) Err.WriteLine("factory: the plan tool is on (switch plan-tool)");
                }
            }
            // pi-sharp-hh-6: read-only budget + context view. The estimate is the same one the compaction
            // gate measures (AgentSession.EstimatedContextTokens), so the tool and the gate agree by construction.
            localTools = [.. localTools, new ContextRemainingTool(
                () => _taskBudget?.Snapshot(state?.Factory?.CurrentTaskKey ?? state?.Agent.Session.SessionId ?? "session"),
                () => _taskBudget?.Limits,
                () => state?.Agent.EstimatedContextTokens() ?? 0,
                model.ContextWindow)];
            // guard-rules-4: the completion-rule gate on kb_transition is wired in BuildTools when
            // settings.guardRequireTestRunBeforeReview is on; the stop half (nudges) is wired in
            // WithMessageSources below.
            state.Context.Tools = BuildTools().ToList();
            discovery?.Restore(notebook);
            state.Agent.NotebookState = notebook.StateReference;
            if (state.Factory is { } factoryWithMemory) factoryWithMemory.Notebook = notebook;
            if (artifacts is not null) artifacts.Notebook = notebook;
            if (notebookSync is not null)
            {
                notebookSync.Artifacts = artifacts;
                notebookSync.SetAside = id => Err.WriteLine($"[notebook] artifact {id} set aside: its bytes no longer match its hash (cut short when written); not uploaded");
            }
            state.Agent.NotebookTokens = () => TokenEstimate.EstimateTextTokens(notebook.Briefing());
            state.Agent.WorkingMessages = artifacts is null ? null : artifacts.PrepareMessages;
            if (!state.Context.Tools.Any(t => t.Name == "notebook") || !state.Context.Tools.Any(t => t.Name == "artifact"))
                throw new InvalidOperationException("The notebook profile requires notebook and artifact retrieval tools; include them in --tools and do not exclude them.");
        }

        if (args.Mode == CliMode.Json && sessionMgr.Header is { } header)
        {
            Out.WriteLine(new System.Text.Json.Nodes.JsonObject
            {
                ["type"] = "session",
                ["id"] = header.Id,
                ["cwd"] = header.Cwd,
            }.ToJsonString());
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        CancellationTokenSource? currentRun = null;
        ConsoleCancelEventHandler? cancelHandler = null;
        if (HookCancelKey)
        {
            cancelHandler = (_, e) =>
            {
                e.Cancel = true;
                currentRun?.Cancel();
            };
            Console.CancelKeyPress += cancelHandler;
        }

        StartStep("ready; first run");
        CancellationToken StartRun()
        {
            currentRun?.Dispose();
            currentRun = CancellationTokenSource.CreateLinkedTokenSource(linked.Token);
            return currentRun.Token;
        }

        try
        {
            if (args.Mode == CliMode.Repl)
                return await RunReplAsync(state, sink, ansi, prompt, files, StartRun, RefreshMcpAsync).ConfigureAwait(false);
            if (prompt.Length == 0 && files is null)
            {
                Err.WriteLine("No prompt given. Pass a prompt, @files, or pipe stdin.");
                return 2;
            }
            if (state.Factory is { } startFactory) prompt = await startFactory.WithEpicMemoryAsync(prompt, linked.Token).ConfigureAwait(false);
            if (HookRunnerFor(state.EffectiveCommandHooks) is { } startHooks && startHooks.Has(CommandHookEvent.SessionStart))
            {
                // SessionStart (Codex): a hook may add context to the start of the run.
                var started = await startHooks.InvokeAsync(CommandHookEvent.SessionStart, new System.Text.Json.Nodes.JsonObject
                {
                    ["session_id"] = state.Agent.Session.SessionId,
                    ["cwd"] = state.Cwd,
                    ["actor"] = state.FactoryActor,
                }, linked.Token).ConfigureAwait(false);
                if (started.AdditionalContext is { Length: > 0 } added) prompt += "\n\n[hook] " + added;
            }
            var result = await RunPromptAsync(state, sink, prompt, files, StartRun(), RefreshMcpAsync).ConfigureAwait(false);
            var resumes = 0;
            var lastResume = DateTimeOffset.MinValue;
            while (result is 5 or RunModelError && state.Factory is { } pausedFactory && !linked.IsCancellationRequested)
            {
                // A model error long after the last resume starts a new streak: the count used to run over the seat's whole
                // life, so its fourth model error in days parked it until a message came (2026-10-25).
                if (result == RunModelError && DateTimeOffset.UtcNow - lastResume > ModelErrorStreak) resumes = 0;
                if (result == RunModelError && state.Assignment is { } troubled && troubled.TakeHandBack())
                {
                    // The provider failed a turn this hybrid seat can run on its own model: resume at once, there.
                    lastResume = DateTimeOffset.UtcNow;
                    if (troubled.PickTurn(state.Agent.EstimatedContextTokens(), state.WatchTurn)) FollowAssignment(state);
                    Err.WriteLine($"[model] task {pausedFactory.CurrentTaskKey ?? "-"} stopped on a provider failure; resuming on {state.Model.Id}.");
                    result = await RunPromptAsync(state, sink, [new UserMessage(
                        "[harness] The previous model request failed on the provider's side. Continue the current task where it stopped; do not repeat calls whose results are above.")],
                        StartRun(), RefreshMcpAsync).ConfigureAwait(false);
                    continue;
                }
                if (result == RunModelError && state.Assignment is { } fallen && fallen.TakeFallback())
                {
                    // The local model failed repeatedly: resume at once, on the subscription (2026-10-29).
                    lastResume = DateTimeOffset.UtcNow;
                    if (fallen.PickTurn(state.Agent.EstimatedContextTokens(), state.WatchTurn)) FollowAssignment(state);
                    Err.WriteLine($"[model] task {pausedFactory.CurrentTaskKey ?? "-"} stopped on the local model's failures; resuming on {state.Model.Id}.");
                    result = await RunPromptAsync(state, sink, [new UserMessage(
                        "[harness] The previous model request failed on the local model. Continue the current task where it stopped; do not repeat calls whose results are above.")],
                        StartRun(), RefreshMcpAsync).ConfigureAwait(false);
                    continue;
                }
                if (result == RunModelError && resumes < ModelErrorResumes)
                {
                    // A provider error used to end the process, and systemd restarted the seat mid-task with a cold
                    // context (pi7, 2026-09-27). The session still holds the task; resume it after a backoff.
                    resumes++;
                    lastResume = DateTimeOffset.UtcNow;
                    Err.WriteLine($"[model] task {pausedFactory.CurrentTaskKey ?? "-"} stopped on a model error; resuming ({resumes}/{ModelErrorResumes}).");
                    try { await Task.Delay(ModelErrorBackoff * resumes, linked.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { break; }
                    // A failing model is often a spent account the operator has just moved the seat off.
                    if (state.Assignment is { } failing) await failing.CheckAsync(true, linked.Token).ConfigureAwait(false);
                    result = await RunPromptAsync(state, sink, [new UserMessage(
                        "[harness] The previous model request failed. Continue the current task where it stopped; do not repeat calls whose results are above.")],
                        StartRun(), RefreshMcpAsync).ConfigureAwait(false);
                    continue;
                }
                if (result == 5)
                    // health-monitor-3 (OpsPacer) matches "[budget] task <key> exhausted" in the seat log and raises @agent-maker.
                    Err.WriteLine($"[budget] task {pausedFactory.CurrentTaskKey ?? "-"} exhausted. Worker remains attached with unfinished work. @digital-scribe or an agent-maker can send /resume-budget (a nudge may follow on the next lines) to grant another bounded attempt.");
                else
                {
                    Err.WriteLine($"[model] task {pausedFactory.CurrentTaskKey ?? "-"} still failing after {resumes} resumes ({GiveUpDetail(_lastModelFailure)}). Worker remains attached; the next message resumes it.");
                    // Giving up says so (2026-10-30: demo-03 gave up on demo #142 and acknowledged its task without a word; the demo
                    // read "running" until digital-scribe noticed). The status line reads "model unavailable" whatever the error was,
                    // so the KB marks the seat stale after its wait, STALE reaches the team lead and a demo it runs is queued again;
                    // the next answer that comes through clears it.
                    if (result == RunModelError)
                        await pausedFactory.NoteModelHealthAsync(false, state.Model.Provider, _modelFailingSince ?? DateTimeOffset.UtcNow,
                            GiveUpDetail(_lastModelFailure)).ConfigureAwait(false);
                }
                // Parked on a failing model, a new assignment is reason enough to resume: the task waits on the model,
                // not on a message (team-lead-2 sat parked on a spent Codex account after it was moved to local).
                var generation = state.Assignment?.Generation;
                pausedFactory.EndIdleOnAssignmentChange = result == RunModelError;
                IReadOnlyList<Message> followUp;
                try { followUp = await pausedFactory.GetFollowUpMessagesAsync(linked.Token).ConfigureAwait(false); }
                finally { pausedFactory.EndIdleOnAssignmentChange = false; }
                if (followUp.Count == 0 && result == RunModelError && state.Assignment is { } moved && moved.Generation != generation && !linked.IsCancellationRequested)
                {
                    Err.WriteLine($"[model] task {pausedFactory.CurrentTaskKey ?? "-"}: the seat was assigned {moved.Model.Id}; resuming on it.");
                    followUp = [new UserMessage(
                        "[harness] The previous model requests failed and the seat has been moved to another model. Continue the current task where it stopped; do not repeat calls whose results are above.")];
                }
                if (followUp.Count == 0) break;
                resumes = 0;
                result = await RunPromptAsync(state, sink, followUp, StartRun(), RefreshMcpAsync).ConfigureAwait(false);
            }
            if (result == RunModelError) result = 1;
            if (state.Factory is { OperatorRestartRequested: true })
            {
                Err.WriteLine($"[restart] operator-triggered clean exit seat=@{state.Factory.Actor ?? "unknown"} at={DateTimeOffset.UtcNow:O} exit={ExitCodeOperatorRestart}; systemd should restart this seat.");
                return ExitCodeOperatorRestart;
            }
            if (state.Factory is { RestartRequested: true })
            {
                Err.WriteLine($"[restart] {state.Factory.IdleRestartReason}; the last task is delivered. Exiting so the seat restarts on it.");
                return ExitCodeAssignmentChanged;
            }
            return result;
        }
        finally
        {
            if (helperTasks is not null) await helperTasks.DisposeAsync().ConfigureAwait(false);
            PiSharp.Tools.BashTool.EnvironmentPolicy = previousShellPolicy;
            mcp.ListsChanged -= mcpChanged;
            if (cancelHandler is not null) Console.CancelKeyPress -= cancelHandler;
            currentRun?.Dispose();
            if (tracer is not null) await tracer.DisposeAsync().ConfigureAwait(false);
            cubicleClient?.Dispose();
            factoryClient?.Dispose();
        }
    }

    /// <summary>Outcome of the attached-mode preflight. <c>Exit</c> is set only for the refusal (code 3); every
    /// other failure leaves <c>Steering</c> null so the run continues locally. <c>Client</c> is returned even in
    /// local mode so the caller disposes it.</summary>
    private sealed record FactoryPreflight(FactoryClient? Client, FactorySteering? Steering, string? Actor, int? Exit);

    /// <summary>
    /// The seat's host suffix for the cache_miss line (adr-pi-sharp-cache-stats: host=&lt;.101|.104&gt;):
    /// the last octet of a local IPv4 address, e.g. <c>.101</c>. <c>.local</c> when none is found.
    /// Computed once per process.
    /// </summary>
    internal static string HostSuffix()
    {
        if (_hostSuffix is { } cached) return cached;
        string suffix = ".local";
        try
        {
            var address = System.Net.Dns.GetHostAddresses(System.Net.Dns.GetHostName())
                .FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                    && !a.ToString().StartsWith("169.254."));
            if (address is { } addr)
            {
                var octet = addr.GetAddressBytes()[3];
                suffix = $".{octet}";
            }
        }
        catch (Exception ex) when (ex is System.Net.Sockets.SocketException)
        {
            // Keep .local; tracing must never disturb the agent.
        }
        _hostSuffix = suffix;
        return suffix;
    }

    private static string? _hostSuffix;

    /// <summary>Verifies the factory token before attaching: URLs present, whoami reachable and authorized, the
    /// identity it reports equal to the configured actor, and one egress probe against chat. Any failure but a
    /// slug mismatch prints one <c>factory:</c> line and runs locally; a mismatch refuses the run with code 3.
    /// The token itself is never printed.</summary>
    private async Task<FactoryPreflight> PreflightFactoryAsync(
        FactorySettings factory,
        CubicleSettings? cubicle,
        CliMode mode,
        CancellationToken cancellationToken)
    {
        var actor = factory.Actor!.Trim();
        if (string.IsNullOrWhiteSpace(factory.KbUrl) || string.IsNullOrWhiteSpace(factory.ChatUrl))
        {
            Err.WriteLine($"factory: actor @{actor} needs both kbUrl and chatUrl; running locally.");
            return new FactoryPreflight(null, null, null, null);
        }

        FactoryClient client;
        try
        {
            client = new FactoryClient(factory.KbUrl, factory.ChatUrl, factory.ResolveToken() ?? cubicle?.ResolveToken(), FactoryHandler);
        }
        catch (Exception ex) when (ex is UriFormatException or ArgumentException)
        {
            Err.WriteLine($"factory: kbUrl/chatUrl is not a usable address ({ex.Message}); running locally.");
            return new FactoryPreflight(null, null, null, null);
        }

        var who = await client.WhoAmIAsync(cancellationToken).ConfigureAwait(false);
        switch (who.Failure)
        {
            case FactoryFailure.Unauthorized:
                Err.WriteLine($"factory: token rejected by {client.WhoAmIEndpoint}; running locally.");
                return new FactoryPreflight(client, null, null, null);
            case FactoryFailure.None:
                break;
            default:
                Err.WriteLine($"factory: {client.WhoAmIEndpoint} did not answer; running locally.");
                return new FactoryPreflight(client, null, null, null);
        }

        var slug = who.Identity!.Slug;
        if (!string.Equals(slug, actor, StringComparison.Ordinal))
        {
            Err.WriteLine($"factory: token belongs to @{slug}, settings say actor @{actor}; refusing to run.");
            return new FactoryPreflight(client, null, null, 3);
        }

        // Egress probe: one read-only digest. It must not consume anything, so mark_read stays false.
        if (await client.ReadInboxAsync(0, markRead: false, cancellationToken).ConfigureAwait(false) is null)
        {
            Err.WriteLine($"factory: chat unreachable, running locally ({client.ChatEndpoint}).");
            return new FactoryPreflight(client, null, null, null);
        }

        var backlogMode = string.Equals(factory.FreshContext, "backlog", StringComparison.OrdinalIgnoreCase);
        var backlogs = backlogMode ? factory.Backlogs ?? [] : [];
        if (backlogMode && backlogs.Count == 0)
            Err.WriteLine("factory: freshContext \"backlog\" needs factory.backlogs; falling back to \"assignment\".");
        var steering = new FactorySteering(client)
        {
            Actor = slug,
            WorkingDirectory = Environment.CurrentDirectory,
            CorrectionAuthorities = new HashSet<string>(factory.CorrectionFrom ?? [], StringComparer.Ordinal),
            Backlogs = backlogs,
            KbParents = backlogs.Count > 0 ? client.KbParentsAsync : null,
            StampTime = true,
            // The REPL keeps its prompt: no long poll between turns. Print mode stays attached and waits.
            FollowUpWaitSeconds = mode == CliMode.Repl ? 0 : 30,
            IdleTimeout = factory.IdleExitSeconds is int idle ? TimeSpan.FromSeconds(idle) : null,
            WorkArea = factory.WorkArea,
            // An inbox item the parser refuses is never dropped silently: one stderr line per distinct drop, on
            // the first digest that carries it after attach (the latch repeats it; the repeats stay quiet). The
            // probe above may have refused some too; those are not printed here, because the steering's first
            // digest carries them again and reports them then.
            Diagnostics = line => Err.WriteLine(line),
            FreshContextOnAssignment = string.Equals(factory.FreshContext, "assignment", StringComparison.OrdinalIgnoreCase)
                || (backlogMode && backlogs.Count == 0),
            FreshTaskFrom = factory.FreshTaskFrom is { Count: > 0 } from
                ? new HashSet<string>(from, StringComparer.Ordinal)
                : FactorySteering.DefaultFreshTaskFrom,
        };
        return new FactoryPreflight(client, steering, slug, null);
    }

    private async Task<int> RunReplAsync(
        CliState state,
        IEventSink sink,
        Ansi ansi,
        string initialPrompt,
        ProcessedFiles? files,
        Func<CancellationToken> startRun,
        Func<CancellationToken, bool, Task> refreshMcp)
    {
        Out.WriteLine($"{ansi.Bold}pi-sharp {Version}{ansi.Reset}  model {state.Model.Id}  cwd {state.Cwd}");
        if (state.EffectiveCommandHooks is { Count: > 0 } hooks)
            Out.WriteLine("hooks: " + string.Join(", ", hooks.Select(h => $"{h.Event}@{h.Version}")));
        var sessionFile = state.Agent.Session.SessionFile;
        if (sessionFile is not null) Out.WriteLine(ansi.Dim + sessionFile + ansi.Reset);
        Out.WriteLine("Type /help for commands, /quit to exit.");

        if (initialPrompt.Length > 0 || files is not null)
            await RunPromptAsync(state, sink, initialPrompt, files, startRun(), refreshMcp).ConfigureAwait(false);

        while (true)
        {
            Out.Write(ansi.Cyan + "> " + ansi.Reset);
            string? line;
            try { line = await In.ReadLineAsync().ConfigureAwait(false); }
            catch (IOException) { return 0; }
            if (line is null) return 0;
            if (string.IsNullOrWhiteSpace(line)) continue;

            await refreshMcp(CancellationToken.None, false).ConfigureAwait(false);
            var result = SlashCommands.Handle(line, state);
            var code = await ApplySlashAsync(result, state, sink, startRun, refreshMcp).ConfigureAwait(false);
            if (code is { } exit) return exit;
        }
    }

    private async Task<int?> ApplySlashAsync(
        SlashResult result,
        CliState state,
        IEventSink sink,
        Func<CancellationToken> startRun,
        Func<CancellationToken, bool, Task> refreshMcp)
    {
        switch (result)
        {
            case SlashQuit q:
                return q.Code;
            case SlashMessage msg:
                sink.WriteLine(msg.Text);
                return null;
            case SlashUnknown unknown:
                sink.WriteError($"Unknown command: /{unknown.Name}. Try /help.");
                return null;
            case SlashPrompt prompt:
                await RunPromptAsync(state, sink, prompt.Text, null, startRun(), refreshMcp).ConfigureAwait(false);
                return null;
            case SlashMcpPrompt prompt:
                try
                {
                    var runToken = startRun();
                    var messages = await prompt.Template.ExpandAsync(prompt.Arguments, runToken).ConfigureAwait(false);
                    await RunPromptAsync(state, sink, messages, runToken, refreshMcp).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    sink.WriteError("MCP prompt failed: " + ex.Message);
                }
                return null;
            case SlashMcpReconnect reconnect:
                try
                {
                    var runToken = startRun();
                    await state.Mcp.ReconnectAsync(reconnect.Alias, runToken).ConfigureAwait(false);
                    await refreshMcp(runToken, true).ConfigureAwait(false);
                    sink.WriteLine(state.Mcp.FormatStatus());
                }
                catch (Exception ex)
                {
                    sink.WriteError("MCP reconnect failed: " + ex.Message);
                }
                return null;
            case SlashCompact:
                try
                {
                    var compacted = await state.Agent.CompactAsync(state.Model, state.Stream, cancellationToken: startRun())
                        .ConfigureAwait(false);
                    sink.WriteLine(compacted is null
                        ? "Nothing to compact."
                        : $"Compacted. Kept from entry {compacted.FirstKeptEntryId} ({compacted.TokensBefore} tokens before).");
                }
                catch (Exception ex)
                {
                    sink.WriteError("Compact failed: " + ex.Message);
                }
                return null;
            case SlashClear:
                state.Agent = NewSession(state);
                sink.WriteLine("New session started.");
                return null;
            case SlashModelChange change:
                state.Model = change.Model;
                state.Agent.Session.AppendModelChange(change.Model.Provider, change.Model.Id);
                var supportsImages = change.Model.SupportsImages;
                foreach (var tool in state.Context.Tools.OfType<ReadTool>())
                {
                    tool.SupportsImages = supportsImages;
                    tool.ContextWindow = change.Model.ContextWindow;
                }
                sink.WriteLine("Model set to " + change.Model.Id);
                return null;
            default:
                return null;
        }
    }

    private async Task<int> RunPromptAsync(
        CliState state,
        IEventSink sink,
        string prompt,
        ProcessedFiles? files,
        CancellationToken cancellationToken,
        Func<CancellationToken, bool, Task> refreshMcp)
    {
        var user = Attachments.ToUserMessage(prompt, files);
        return await RunPromptAsync(state, sink, [user], cancellationToken, refreshMcp).ConfigureAwait(false);
    }

    private static bool IsDeterministicImageFailure(AssistantMessage failure) =>
        failure.ProviderHttpStatus == 400 && (failure.ErrorCode is "invalid_image" or "invalid_image_url" or "image_parse_error"
        || new[] { "Failed to load image", "image file is truncated", "could not decode image", "cannot decode image", "unable to decode image", "invalid_image" }
            .Any(phrase => failure.ErrorMessage?.Contains(phrase, StringComparison.OrdinalIgnoreCase) == true));

    private static string ImageRequestReferences(IReadOnlyList<Message> messages)
    {
        var refs = new List<string>();
        for (var m = messages.Count - 1; m >= 0 && refs.Count < 8; m--)
        {
            var (parts, source) = messages[m] switch
            {
                ToolResultMessage t => (t.Content, $"tool call {t.ToolCallId}"),
                UserMessage u => (u.Content, $"user {u.Timestamp:O}"),
                _ => (null, ""),
            };
            if (parts is null) continue;
            for (var p = 0; p < parts.Count && refs.Count < 8; p++)
                if (parts[p] is ImageContent) refs.Add($"{source} part {p}");
        }
        return refs.Count == 0 ? "none retained in working context (inspect the preserved session)" : string.Join(", ", refs);
    }

    // pi-sharp-hh-6: shared between RunPromptAsync (which wraps the stream) and the context_remaining
    // tool's closure, which is built before any prompt runs.
    private TaskBudget? _taskBudget;
    /// <summary>The last model answer that ended in an error, and since when answers have kept failing (null once one came through).</summary>
    private AssistantMessage? _lastModelFailure;
    private DateTimeOffset? _modelFailingSince;

    /// <summary>What a give-up names on the log and the status line: the last error, one short line. The KB reads the status's
    /// first "since" as the start of the outage, so the error's own words never carry one.</summary>
    internal static string GiveUpDetail(AssistantMessage? failure)
    {
        var text = (failure?.ErrorMessage ?? failure?.ErrorCode ?? "unknown error").Split('\n', 2)[0].Trim();
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\bsince\b", "from", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return text.Length > 160 ? text[..160] + "..." : text;
    }
    private TaskBudget? BudgetRef => _taskBudget;

    /// <summary>Reviews have their own model requests, with usage attributed to the task that started the request.</summary>
    private static StreamFunction ReviewStream(CliState state, string category) => (model, context, options, ct) => ObserveReview(state, category, model, context, options, ct);

    private static async IAsyncEnumerable<AssistantMessageEvent> ObserveReview(CliState state, string category,
        ModelConfig model, LlmContext context, StreamOptions options,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var scope = state.Factory?.CurrentTaskKey ?? state.Agent.Session.SessionId;
        var recordUsage = state.RecordReviewUsage;
        var start = Environment.TickCount64;
        await foreach (var evt in state.Stream(model, context, options, ct).ConfigureAwait(false))
        {
            var response = evt switch { AssistantDone done => done.Message, AssistantError error => error.Error, _ => null };
            if (response is not null)
            {
                try { recordUsage?.Invoke(scope, response.Usage); }
                catch (Exception ex) when (ex is not OutOfMemoryException) { /* Accounting never fails a review. */ }
                state.Telemetry?.Record(new SeatEvent(DateTime.UtcNow, SeatEvent.Request, model.Id,
                    evt is AssistantDone, (int)Math.Min(int.MaxValue, Environment.TickCount64 - start),
                    response.Usage.Input, response.Usage.Output, response.Usage.CacheRead,
                    new JsonObject { ["category"] = category, ["parent_task"] = scope }));
            }
            yield return evt;
        }
    }

    private readonly string _processCacheKey = Guid.NewGuid().ToString("N");

    /// <summary>The family whose tuned instructions a model gets: <c>gpt</c> for OpenAI's (the Codex backend, or an id that starts
    /// with gpt-); null otherwise. A hybrid seat takes its subscription model's family: the prompt is built once, at start.</summary>
    internal static string? ModelFamilyOf(ModelConfig model) =>
        model.Provider == "openai-codex" || model.Id.StartsWith("gpt-", StringComparison.OrdinalIgnoreCase) ? "gpt" : null;

    /// <summary>Every configured handler, several per event in their order (Codex hooks), or null when there are none.</summary>
    private CommandHookRunner? HookRunnerFor(IReadOnlyList<CommandHookSetting> settings) => settings.Count == 0 ? null
        : new CommandHookRunner(settings.GroupBy(h => h.Event).ToDictionary(g => g.Key,
            g => (IReadOnlyList<CommandHook>)[.. g.Select(h => new CommandHook(h.Command, h.Version, h.Timeout, h.FailMode, h.Matcher))]),
            message => Err.WriteLine("[hook] " + message));

    /// <summary>The prompt-cache key of a session: its id when a provider accepts it, else a digest of it; a run
    /// without a session keeps one key for the process.</summary>
    internal string CacheKeyFor(string? sessionId) => string.IsNullOrEmpty(sessionId) ? _processCacheKey
        : PiSharp.Llm.Responses.SubscriptionResponsesClient.ValidCacheKey(sessionId) ? sessionId
        : Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(sessionId)))[..32].ToLowerInvariant();

    private async Task<int> RunPromptAsync(
        CliState state,
        IEventSink sink,
        IReadOnlyList<Message> prompts,
        CancellationToken cancellationToken,
        Func<CancellationToken, bool, Task> refreshMcp)
    {
        await refreshMcp(cancellationToken, false).ConfigureAwait(false);
        state.RefreshMcp = refreshMcp;
        // A hybrid seat picks the side of a run's first turn too. Only the turns after it were picked, so the first turn after
        // a start, and every resume after a model error, went to the subscription: pi12 and pi14 restarted on 0.5.142 at
        // 09:46Z on 2026-10-28 with Grok paused until 11-01 ("hybrid, band paused, 100% of turns on the seat's own model"),
        // sent four turns to xai (429) and stopped.
        state.Assignment?.PickTurn(state.Agent.EstimatedContextTokens(), state.WatchTurn, TurnKind.TaskStart);
        FollowAssignment(state);
        prompts = prompts.Select(m => m is UserMessage u ? (Message)(u with { TaskStart = u.TaskStart ?? true }) : m).ToArray();
        if (state.Notebook is { } inputMemory)
            NotebookContext.Capture(inputMemory, [.. state.Context.Messages, .. prompts]);
        var hookSettings = state.EffectiveCommandHooks;
        // plan-pi-sharp-codex items 19-20: this run's requests, tool calls and guard decisions, and its turn diff.
        var runTelemetry = new RunTelemetry(state.Telemetry, new TurnDiffTracker(state.Cwd));
        state.RecordReviewUsage = null;
        var helpers = state.Context.Tools.Select(t => t is BoundedTool b ? b.Inner : t).OfType<TaskTool>().ToArray();
        foreach (var helper in helpers)
            helper.UsageRecorded = (id, scope, usage) => state.Telemetry?.Record(new SeatEvent(DateTime.UtcNow,
                SeatEvent.Request, "helper", true, TokensIn: usage.Input, TokensOut: usage.Output, TokensCached: usage.CacheRead,
                Detail: new JsonObject { ["helper"] = id, ["parent_task"] = scope }));
        var config = new AgentLoopConfig
        {
            Model = state.Model,
            Reasoning = state.Thinking == ThinkingLevel.Off ? null : state.Thinking,
            Temperature = state.Settings.Temperature,
            MaxTokens = state.Assignment is { } assigned ? assigned.MaxTokensFor(state.Settings) : state.Settings.MaxTokens,
            ToolCallTimeout = TimeSpan.FromSeconds(Math.Max(1, state.Settings.ToolCallTimeoutSeconds ?? 900)),
            CommandHooks = HookRunnerFor(hookSettings),
            HookSessionId = state.Agent.Session.SessionId,
            HookTaskKey = state.Factory?.CurrentTaskKey,
            ToolGuard = state.SeatGuard,
            ActionReviewer = state.ActionReviewer,
            RecordGuard = state.Telemetry is null ? null : runTelemetry.Guard,
            FreeformTools = state.Settings.FreeformApplyPatch == true ? new HashSet<string>(StringComparer.Ordinal) { "apply_patch" } : null,
            // Just below the seat's own compaction point, so the provider's compaction comes first.
            ServerCompactThreshold = state.Settings.ServerCompaction == true && state.Settings.CompactionTriggerTokens is > 0 and var trigger
                ? trigger * 95 / 100 : null,
            CacheKey = CacheKeyFor(state.Agent.Session.SessionId),
            TransformContext = state.Notebook is { } memory
                ? async (messages, ct) =>
                {
                    if (state.NotebookSync is { } sync)
                    {
                        try { await sync.FlushAsync(memory, ct).ConfigureAwait(false); }
                        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException && !ct.IsCancellationRequested)
                        { Err.WriteLine("[notebook] remote sync unconfirmed; local operations remain pending: " + ex.Message); }
                    }
                    // Capture exact input sources before applying the working-context image/output view.
                    var recite = state.Settings.PlanReciteEveryTurns;
                    var stall = state.Settings.PlanStallTurns ?? (state.Settings.PlanTool == true && state.Settings.DefaultModel is not null && state.Settings.DefaultModel.Contains("miniclaude", StringComparison.OrdinalIgnoreCase) ? NotebookContext.DefaultStallTurnsMiniclaude : null);
                    NotebookContext.Capture(memory, messages);
                    var working = state.Artifacts?.PrepareMessages(messages) ?? messages;
                    if (NotebookContext.RecallQuery(working) is { } query) await memory.PrepareRecallAsync(query, ct).ConfigureAwait(false);
                    var baseCharacters = state.Context.SystemPrompt.Length + ToolCatalog.Present(state.Context.Tools, state.Model, messages)
                        .Sum(t => t.Name.Length + t.Description.Length + t.Parameters.ToJsonString().Length);
                    var prepared = NotebookContext.Prepare(memory, working, NotebookContext.Default() with { BaseCharacters = baseCharacters }, recite, stall, captureSources: false);
                    // pi-sharp-memory-curation-1: measure the notebook's share of this request (the
                    // harness-injected notebook messages against the rest of the context).
                    state.NotebookShare?.Record(NotebookShareTracker.Measure(prepared, state.Context.SystemPrompt, baseCharacters - state.Context.SystemPrompt.Length));
                    return prepared;
                } : null,
        };
        if (state.CacheStats is { } cacheStats)
        {
            config.RecordTurnUsage = (usage, contextTokens, modelId) =>
                cacheStats.Record(usage.CacheRead, contextTokens, modelId);
        }
        config = WithMessageSources(config, state, cancellationToken);
        var prepareHelpers = config.PrepareNextTurn;
        config = config with
        {
            PrepareNextTurn = async turn =>
            {
                var update = prepareHelpers is null ? null : await prepareHelpers(turn).ConfigureAwait(false);
                foreach (var helper in helpers) helper.StopOutsideScope();
                return update;
            },
        };
        if (state.CheckpointGreen is { } greenTrackerForHint && state.SessionDir is { Length: > 0 } checkpointSessionDir)
        {
            // pi-sharp-checkpoints-7: the before-batch and the after-reply hooks share one closure so the
            // after-reply hook only snapshots roots the before-batch hook already resolved this turn.
            var (checkpointBefore, checkpointAfter) = MakeCheckpointHooks(state.Cwd, CheckpointStore.DefaultDir(checkpointSessionDir));
            config = config with { BeforeMutatingBatch = checkpointBefore };
            if (state.Settings.CheckpointsEnabled == true && state.Settings.CheckpointsAutoOnTurnEnd == true)
            {
                // The hook is installed only when the project flag is on AND checkpoints are enabled.
                config = config with { AfterSuccessfulReply = checkpointAfter };
            }
            // CP3: after each tool batch, the green tracker may owe a one-shot last-green hint.
            config = config with { AfterToolBatch = MakeAfterToolBatchHook(greenTrackerForHint) };
        }
        // The turn diff reads each file a batch's file tools will write before they run, then the checkpoint (if any).
        var checkpointBeforeBatch = config.BeforeMutatingBatch;
        config = config with
        {
            BeforeMutatingBatch = async (calls, context, loopConfig, ct) =>
            {
                try { runTelemetry.Diffs.Capture(calls, context.Tools); }
                catch (Exception ex) when (ex is not OperationCanceledException) { Err.WriteLine("[turn] diff baseline not taken: " + ex.Message); }
                return checkpointBeforeBatch is null ? null : await checkpointBeforeBatch(calls, context, loopConfig, ct).ConfigureAwait(false);
            },
        };
        // plan-pi-sharp-codex item 14: the held epic's budget, with notices at 50/80/100% (switch epic-budget, or goalAudits): it
        // informs, never stops.
        if (state.Settings.GoalAudits != false && state.Factory is { } budgetFactory && state.Notebook is { } epicNotebook
            && (state.Settings.GoalAudits == true || await budgetFactory.SwitchOnAsync("epic-budget", cancellationToken).ConfigureAwait(false)))
        {
            var epicBudget = new EpicBudget(epicNotebook, state.Settings.EpicOutputBudget is > 0 ? state.Settings.EpicOutputBudget.Value : EpicBudget.DefaultOutputBudget);
            var ledger = new EpicUsageLedger(epicBudget);
            string Scope() => budgetFactory.CurrentTaskKey ?? state.Agent.Session.SessionId;
            var boundScope = Scope();
            ledger.Bind(boundScope, await budgetFactory.HeldEpicAsync(cancellationToken).ConfigureAwait(false));
            string? owedNotice = null;
            state.RecordReviewUsage = (scope, usage) =>
            {
                if (ledger.Charge(scope, usage.Output) is { } notice) Volatile.Write(ref owedNotice, notice);
            };
            var recordUsage = config.RecordTurnUsage;
            foreach (var helper in helpers)
            {
                var recordHelper = helper.UsageRecorded;
                helper.UsageRecorded = (id, scope, usage) =>
                {
                    recordHelper?.Invoke(id, scope, usage);
                    try { if (ledger.Charge(scope, usage.Output) is { } notice) Volatile.Write(ref owedNotice, notice); }
                    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException) { Err.WriteLine("[budget] helper count not kept: " + ex.Message); }
                };
            }
            config.RecordTurnUsage = (usage, contextTokens, modelId) =>
            {
                recordUsage?.Invoke(usage, contextTokens, modelId);
                try { if (ledger.Charge(Scope(), usage.Output) is { } notice) Volatile.Write(ref owedNotice, notice); }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException) { Err.WriteLine("[budget] epic count not kept: " + ex.Message); }
            };
            var prepareEpic = config.PrepareNextTurn;
            config = config with
            {
                PrepareNextTurn = async turn =>
                {
                    var update = prepareEpic is null ? null : await prepareEpic(turn).ConfigureAwait(false);
                    var scope = Scope();
                    if (scope != boundScope)
                    {
                        ledger.Bind(scope, await budgetFactory.HeldEpicAsync(cancellationToken, refresh: true).ConfigureAwait(false));
                        boundScope = scope;
                    }
                    if (Interlocked.Exchange(ref owedNotice, null) is not { } notice) return update;
                    Err.WriteLine("[budget] " + notice);
                    return (update ?? new TurnUpdate()) with { Trailing = [.. update?.Trailing ?? [], new UserMessage(notice)] };
                },
            };
        }
        StreamFunction? runStream = null;
        if (state.Notebook is { } budgetStore && state.Settings.TaskMaxTurns is > 0)
        {
            // pi-sharp-hh-6: one instance feeds both the wrapped stream (charging) and the budget-reminder
            // hook (TakeDueReminder), so the reminder lands on the turn after the one it describes.
            var budget = new TaskBudget(budgetStore, () => state.Factory?.CurrentTaskKey ?? state.Agent.Session.SessionId,
                state.Settings.TaskMaxTurns.Value, state.Settings.TaskMaxOutputTokens ?? 200000, state.Settings.TaskMaxToolCalls ?? 500);
            config = ComposeBudgetReminder(config, budget);
            runStream = budget.Wrap(state.Stream);
            _taskBudget = budget;
        }
        runStream ??= state.Stream;
        var exit = 0;
        try
        {
            await foreach (var evt in state.Agent.Run(prompts, config, runStream, cancellationToken).ConfigureAwait(false))
            {
                sink.OnEvent(evt);
                runTelemetry.Observe(evt);
                if (evt is MessageEnd { Message: AssistantMessage providerResponse } && state.Factory is { } steering)
                    await steering.ObserveProviderResponseAsync(providerResponse, cancellationToken).ConfigureAwait(false);
                if (evt is MessageEnd { Message: AssistantMessage { StopReason: StopReason.Aborted } })
                    exit = 1;
                if (evt is MessageEnd { Message: AssistantMessage { StopReason: StopReason.Error } failure })
                {
                    if (IsDeterministicImageFailure(failure))
                    {
                        // The byte validator is deliberately not a full codec. A provider's explicit image
                        // rejection must not enter the generic three-resume loop (or assignment-change retry).
                        var refs = ImageRequestReferences(state.Context.Messages);
                        Err.WriteLine($"[image] provider rejected image input; not retrying the unchanged request. Candidate attachment refs: {refs}. Session preserved at {state.Agent.Session.SessionFile ?? "(memory)"}; recapture the source instead of resuming unchanged.");
                        exit = 1;
                    }
                    else exit = cancellationToken.IsCancellationRequested ? 1 : RunModelError;
                    _lastModelFailure = failure;
                    _modelFailingSince ??= DateTimeOffset.UtcNow;
                }
                else if (evt is MessageEnd { Message: AssistantMessage { StopReason: not StopReason.Aborted } })
                    _modelFailingSince = null;
                if (evt is MessageEnd { Message: AssistantMessage { ErrorCode: "budget_exhausted" } }) exit = 5;
            }
        }
        catch (ContextOverflowException ex)
        {
            // P4 (wi-8): a run that died on a context overflow it could not recover from exits
            // loudly and as a typed event. The machine-readable notice goes to stdout on the event
            // channel (so it is parseable without scraping prose) and to stderr for the operator,
            // and the exit status is distinct (ExitCodeContextOverflow) so a supervisor never
            // mistakes an overflow death for a handled turn. This is the third rung of the
            // compensation budget (compaction, then retry, then this) and it is never swallowed.
            var notice = BuildOverflowEvent(ex);
            sink.WriteStructured(notice);
            sink.WriteError(ex.Message);
            return ExitCodeContextOverflow;
        }
        catch (OperationCanceledException)
        {
            sink.WriteError("Aborted.");
            return 1;
        }
        finally
        {
            try { runTelemetry.Finish(Err.WriteLine); }
            catch (Exception ex) { Err.WriteLine("[turn] diff not computed: " + ex.Message); }
            if (state.Notebook is { PendingCount: > 0 } pendingNotebook && state.NotebookSync is { } sync && !cancellationToken.IsCancellationRequested)
            {
                try { await sync.FlushAsync(pendingNotebook, cancellationToken).ConfigureAwait(false); }
                catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
                { Err.WriteLine("[notebook] final sync unconfirmed; durable outbox remains pending: " + ex.Message); }
            }
        }
        return exit;
    }

    /// <summary>Wires the cubicle and the factory into the agent loop's queues. Steering polls both every time,
    /// cubicle first; follow-ups ask the cubicle first (it returns immediately when its wait is 0) and only then
    /// let the factory long-poll. The turn hooks — reply posting and the task-boundary context reset — belong to
    /// the factory, which is the only source that owns a task.</summary>
    /// <summary>pi-sharp-checkpoints-7: the AfterSuccessfulReply hook, built together with the before-batch
    /// hook so both share one root set. The closure remembers the workspace roots the before-batch hook
    /// actually resolved for mutating batches since the previous fire — a skipped, ambiguous or
    /// unresolved batch adds nothing and does not clear the set — and accumulates the tool-call ids of the
    /// batches being closed. At a successful turn end it snapshots each remembered root once (an entry per
    /// created snapshot) then clears the set and the id list. It never resolves from the process cwd and
    /// never scans ~/work. One store per root, like the before-batch hook; a failed snapshot never fails
    /// the run.</summary>
    private static (Func<IReadOnlyList<ToolCallContent>, AgentContext, AgentLoopConfig, CancellationToken, ValueTask<PiSharp.Core.Session.CheckpointEntry?>> Before,
                    Func<TurnContext, CancellationToken, ValueTask<IReadOnlyList<PiSharp.Core.Session.CheckpointEntry>?>> After)
        MakeCheckpointHooks(string cwd, string checkpointsDir)
    {
        // Roots the before-batch hook actually resolved for mutating batches since the previous fire.
        // A skipped / ambiguous / unresolved batch adds nothing and does not clear the set.
        var rememberedRoots = new HashSet<string>(StringComparer.Ordinal);
        // Tool-call ids of every mutating batch since the previous fire (the batches being closed).
        var closedToolCallIds = new List<string>();

        var before = (IReadOnlyList<ToolCallContent> calls, AgentContext context, AgentLoopConfig loopConfig, CancellationToken ct) =>
            TrackAsync(calls, ct);

        async ValueTask<PiSharp.Core.Session.CheckpointEntry?> TrackAsync(IReadOnlyList<ToolCallContent> calls, CancellationToken ct)
        {
            // Resolve roots for this batch (same logic as SnapshotHookAsync).
            var (roots, isAmbiguous) = ResolveBatchRoots(cwd, calls);

            // Remember the root and the tool-call ids for the after-reply hook, but only when the
            // before-batch hook actually resolved (exactly one unambiguous root). An ambiguous or
            // multi-root batch that the before-batch hook skipped is not added and does not clear the set.
            if (roots.Count == 1 && !isAmbiguous)
            {
                rememberedRoots.Add(roots.Single());
                foreach (var call in calls) closedToolCallIds.Add(call.Id);
            }

            // The before-batch entry fires only when exactly one unambiguous root, as before.
            var entry = await SnapshotHookAsync(cwd, checkpointsDir, calls, roots, isAmbiguous, ct).ConfigureAwait(false);
            return entry;
        }

        var after = (TurnContext turn, CancellationToken ct) => CloseAsync(ct);

        async ValueTask<IReadOnlyList<PiSharp.Core.Session.CheckpointEntry>?> CloseAsync(CancellationToken ct)
        {
            if (ct.IsCancellationRequested) return null;
            if (rememberedRoots.Count == 0) return null; // nothing to close since the previous fire

            var rootsToSnapshot = rememberedRoots.ToArray();
            var toolCallIds = closedToolCallIds.ToArray();
            // Clear before snapshotting: the set is per fire.
            rememberedRoots.Clear();
            closedToolCallIds.Clear();

            var entries = new List<PiSharp.Core.Session.CheckpointEntry>();
            foreach (var root in rootsToSnapshot)
            {
                try
                {
                    var store = new CheckpointStore(root, checkpointsDir);
                    var result = await store.CreateSnapshotAsync(ct).ConfigureAwait(false);
                    if (!result.Created || result.Commit is null) continue; // unchanged fingerprint, merge...
                    entries.Add(new PiSharp.Core.Session.CheckpointEntry
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        Commit = result.Commit,
                        WorkspaceRoot = root,
                        Head = result.Head ?? "",
                        ToolCallIds = toolCallIds,
                        Green = false,
                        Partial = result.Partial,
                    });
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch { /* a checkpoint must never fail the run */ }
            }
            return entries.Count > 0 ? entries : null;
        }

        return (before, after);

        // Resolve the workspace roots for a batch of tool calls. Returns the set of resolved roots and
        // whether the batch is ambiguous (a shell call with cd that targets an unknown tree).
        static (HashSet<string> roots, bool isAmbiguous) ResolveBatchRoots(string cwd, IReadOnlyList<ToolCallContent> calls)
        {
            var roots = new HashSet<string>(StringComparer.Ordinal);
            var isAmbiguous = false;
            foreach (var call in calls)
            {
                foreach (var path in MutationPaths(call))
                {
                    var fullPath = Path.IsPathRooted(path) ? path : Path.Combine(cwd, path);
                    if (CheckpointStore.ResolveWorkTreeRoot(fullPath) is { } root) roots.Add(root);
                    else if (call.Name is "bash" or "job")
                    {
                        // Shell execution can change cwd or target an arbitrary tree; never guess.
                        if (call.Arguments.ToJsonString().Contains("cd ", StringComparison.Ordinal))
                            isAmbiguous = true;
                        else if (CheckpointStore.ResolveWorkTreeRoot(cwd) is { } shellRoot) roots.Add(shellRoot);
                    }
                }
            }
            return (roots, isAmbiguous);
        }

        // The CP1 snapshot body: appends one entry for a single unambiguous root, skipping an ambiguous
        // or multi-root batch (the before-batch hook fires only when exactly one root is unambiguous).
        static async ValueTask<PiSharp.Core.Session.CheckpointEntry?> SnapshotHookAsync(
            string cwd,
            string checkpointsDir,
            IReadOnlyList<ToolCallContent> calls,
            HashSet<string> roots,
            bool isAmbiguous,
            CancellationToken ct)
        {
            if (ct.IsCancellationRequested) return null;
            if (roots.Count != 1 || isAmbiguous)
            {
                Console.Error.WriteLine($"[checkpoints] skipped ambiguous/unresolved workspace for mutating batch ({roots.Count} roots)");
                return null;
            }
            var workspaceRoot = roots.Single();
            var store = new CheckpointStore(workspaceRoot, checkpointsDir);
            var result = await store.CreateSnapshotAsync(ct).ConfigureAwait(false);
            if (!result.Created || result.Commit is null) return null;
            return new PiSharp.Core.Session.CheckpointEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                Commit = result.Commit,
                WorkspaceRoot = workspaceRoot,
                Head = result.Head ?? "",
                ToolCallIds = calls.Select(c => c.Id).ToArray(),
                Green = false,
                Partial = result.Partial,
            };
        }

        static IEnumerable<string> MutationPaths(ToolCallContent call)
        {
            if (call.Name is "write" or "edit")
            {
                if (call.Arguments["path"]?.GetValue<string>() is { Length: > 0 } path) yield return path;
                yield break;
            }
            if (call.Name == "apply_patch")
            {
                var patch = call.Arguments["input"]?.GetValue<string>() ?? "";
                foreach (var line in patch.Split('\n'))
                    if (line.StartsWith("*** Update File: ", StringComparison.Ordinal) || line.StartsWith("*** Add File: ", StringComparison.Ordinal) || line.StartsWith("*** Delete File: ", StringComparison.Ordinal))
                        yield return line[(line.IndexOf(':') + 1)..].Trim();
                yield break;
            }
            if (call.Name is "bash" or "job") yield return "";
        }
    }

    /// <summary>pi-sharp-checkpoints-3: the AfterToolBatch hook. After each tool batch the tracker counts the
    /// distance from the last green and may owe the one-shot last-green hint, which goes in as a trailing
    /// user message (the loop appends it after the batch's tool results). Never throws; a hint must
    /// never fail the run.</summary>
    private static Func<IReadOnlyList<ToolResultMessage>, AgentLoopConfig, CancellationToken, ValueTask<IReadOnlyList<Message>?>>
        MakeAfterToolBatchHook(CheckpointGreenTracker tracker)
    {
        return (results, loopConfig, ct) =>
        {
            try
            {
                tracker.AfterBatch();
                var hint = tracker.DrainHint();
                IReadOnlyList<Message>? messages = hint is null ? null : new[] { new UserMessage(hint) };
                var t = new ValueTask<IReadOnlyList<Message>?>(messages);
                return t;
            }
            catch
            {
                var none = (IReadOnlyList<Message>?)null;
                var t2 = new ValueTask<IReadOnlyList<Message>?>(none);
                return t2;
            }
        };
    }

    /// <summary>pi-sharp-checkpoints-3: mark the latest checkpoint green. Runs on the bash command-exit path
    /// (outside any awaited call) so it blocks briefly on git; a failed mark is swallowed — the counter
    /// already reset, so the next green build retries. The green index is the store's monotonic count
    /// (refs/pi/cp/green/N), so it matches what CP2's rewind selector will report.</summary>
    private static void MarkLatestCheckpointGreen(CliState state, CheckpointStore? store, CheckpointGreenTracker tracker)
    {
        try
        {
            CheckpointEntry? latest = null;
            foreach (var entry in state.Agent.Session.Entries.AsEnumerable().Reverse())
            {
                if (entry is CheckpointEntry cp) { latest = cp; break; }
            }
            if (latest is null) return; // no checkpoint yet; the next green build will retry
            var activeStore = latest.WorkspaceRoot is { } root && state.SessionDir is { Length: > 0 } sessionDir
                ? new CheckpointStore(root, CheckpointStore.DefaultDir(sessionDir))
                : store;
            if (activeStore is null) return;
            var index = (activeStore.LastGreenIndex() ?? 0) + 1;
            activeStore.MarkGreenAsync(latest.Commit, index).GetAwaiter().GetResult();
            state.Agent.Session.MarkCheckpointGreen(latest.Id);
            tracker.GreenMarked(index);
        }
        catch { /* a mark must never fail the run */ }
    }

    /// <summary>How often a slow start says which step it is in (and the wait before the first such line).</summary>
    public static TimeSpan StartWatchEvery { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>The first line of the hub's hourly digest in a cubicle (KB WatchDigest).</summary>
    public const string WatchDigestPrefix = "DIGEST for ";

    /// <summary>Whether the cubicle delivered the hub's digest.</summary>
    public static bool IsDigest(IReadOnlyList<Message> messages) =>
        messages.OfType<UserMessage>().Any(m => ContentText.Of(m.Content).Contains(WatchDigestPrefix, StringComparison.Ordinal));

    /// <summary>Marks the next turn a watch turn when the cubicle delivered the hub's digest.</summary>
    private static IReadOnlyList<Message> NoteWatch(CliState state, IReadOnlyList<Message> messages)
    {
        if (IsDigest(messages)) state.WatchTurn = true;
        return messages;
    }

    private static AgentLoopConfig WithMessageSources(AgentLoopConfig config, CliState state, CancellationToken cancellationToken)
    {
        var cubicle = state.Steering;
        var factory = state.Factory;
        if (factory is null)
        {
            return cubicle is null
                ? config
                : config with
                {
                    GetSteeringMessages = cubicle.GetSteeringMessages,
                    GetFollowUpMessages = cubicle.GetFollowUpMessages,
                };
        }

        // An always-on seat runs one long run and takes each new task as a follow-up, so the catalog refresh at run start was
        // its only one: hub 1.0.271 added kb_demo and kb_defect and the seats kept their morning tools (audit 2026-10-24). A new
        // task re-reads the servers' lists first (at most once a minute; an unchanged list rebuilds nothing).
        async ValueTask<IReadOnlyList<Message>> Refreshed(IReadOnlyList<Message> messages)
        {
            if (messages.Count > 0 && state.RefreshMcp is { } refresh)
                try { await refresh(cancellationToken, false).ConfigureAwait(false); }
                catch (Exception ex) when (ex is not OperationCanceledException) { /* the tools it had still work */ }
            return messages;
        }

        // The hub's hourly digest reaches an idle team lead or product owner through its inbox as often as through the
        // cubicle's steering: both mark the watch turn (it runs on the seat's own model).
        Func<ValueTask<IReadOnlyList<Message>>> steering = cubicle is null
            ? async () => NoteWatch(state, await factory.GetSteeringMessagesAsync(cancellationToken).ConfigureAwait(false))
            : async () => Concat(
                NoteWatch(state, await cubicle.GetSteeringMessages().ConfigureAwait(false)),
                NoteWatch(state, await factory.GetSteeringMessagesAsync(cancellationToken).ConfigureAwait(false)));

        Func<ValueTask<IReadOnlyList<Message>>> followUp = cubicle is null
            ? async () => await Refreshed(NoteWatch(state, await factory.GetFollowUpMessagesAsync(cancellationToken).ConfigureAwait(false))).ConfigureAwait(false)
            : async () =>
            {
                var first = NoteWatch(state, await cubicle.GetFollowUpMessages().ConfigureAwait(false));
                return await Refreshed(first.Count > 0 ? first
                    : NoteWatch(state, await factory.GetFollowUpMessagesAsync(cancellationToken).ConfigureAwait(false))).ConfigureAwait(false);
            };

        var verify = state.Settings.VerifyOnStop ?? true;
        var verifier = verify && state.Notebook is { } notebook
            ? CreateStopGate(state, factory, notebook)
            : null;
        // guard-rules-3: the seat's own guard rules run in shadow and their successful-call
        // baseline is captured, so the idle distil turn has something to replay against. The
        // fleet guard on the config stays untouched; a seat rule never blocks the model here.
        var guardSeat = state.Notebook is { } guardStore ? new GuardSeatHook(guardStore, () => factory.CurrentTaskKey) : null;
        // guard-rules-4: while a harness test run is required before review/done and none is
        // recorded for the task, the run may not stop; the refusal reminder is injected as a
        // trailing message (bounded by the nudge cap, after which the stop proceeds).
        var guardCompletion = state.Settings.GuardRequireTestRunBeforeReview == true && state.Notebook is { } cNotebook
            ? new GuardCompletionGate(cNotebook, () => factory.CurrentTaskKey ?? state.Agent.Session.SessionId,
                [new GuardCompletionRule("test-run-before-review")], state.Settings.GuardCompletionMaxNudges ?? 3)
            : null;
        return config with
        {
            GetSteeringMessages = steering,
            GetFollowUpMessages = followUp,
            RecordToolCalls = factory.RecordToolCalls,
            DecideTurn = async turn =>
            {
                guardSeat?.RecordTurn(turn);
                if (verifier is not null && !await verifier.ShouldStopAfterTurnAsync(turn, cancellationToken).ConfigureAwait(false))
                    return TurnDecision.Continue;
                if (guardCompletion is not null && !guardCompletion.MayStop())
                {
                    LogErr.WriteLine($"[guard] refusing stop for {factory.CurrentTaskKey ?? state.Agent.Session.SessionId}: {guardCompletion.TakeReminder()}");
                    return TurnDecision.Continue;
                }
                return await factory.ShouldStopAfterTurnAsync(turn, cancellationToken).ConfigureAwait(false) ? TurnDecision.Stop : TurnDecision.Poll;
            },
            PrepareNextTurn = RefreshStandingBetweenTurns(SelfUpdateBetweenTurns(FollowAssignmentBetweenTurns(ComposeGuardReminder(
                PrepareNextTurn(config, state, verifier, factory, cancellationToken)!,
                guardCompletion), state, cancellationToken), state, cancellationToken), state, cancellationToken),
        };
    }

    /// <summary>Between two turns the seat reads its role texts again (at most every two minutes; <see cref="SeatStanding"/>).
    /// It reads before the inner steps, so a fresh context for a new epic copies the new system prompt; a context that
    /// still carries the old one gets the new one too.</summary>
    internal static Func<TurnContext, ValueTask<TurnUpdate?>> RefreshStandingBetweenTurns(
        Func<TurnContext, ValueTask<TurnUpdate?>> prepare, CliState state, CancellationToken cancellationToken)
    {
        if (state.RefreshStanding is null) return prepare;
        return async turn =>
        {
            if (state.RefreshStanding is { } refresh) await refresh(cancellationToken).ConfigureAwait(false);
            var update = await prepare(turn).ConfigureAwait(false);
            if (update?.Context is { } next && !ReferenceEquals(next, state.Context)) next.SystemPrompt = state.Context.SystemPrompt;
            return update;
        };
    }

    /// <summary>The self-update of an attached seat (digital-scribe, 2026-10-16): it follows a channel of pi-sharp in the
    /// Artifactory on the seat's KB host (PI_SHARP_UPDATE_CHANNEL, default stable; PI_SHARP_SELF_UPDATE=0 turns it off) and
    /// restarts when idle with its last task delivered. Starting is the good sign pi-seat-exec keeps a build by.</summary>
    private SelfUpdater? SelfUpdateFor(Settings settings, FactorySteering steering)
    {
        if (Environment.GetEnvironmentVariable("PI_SHARP_SELF_UPDATE") == "0" || StreamFactory is not null) return null;
        if (settings.Factory?.KbUrl is not { Length: > 0 } kb || !Uri.TryCreate(kb, UriKind.Absolute, out var kbUri)) return null;
        var channel = Environment.GetEnvironmentVariable("PI_SHARP_UPDATE_CHANNEL") is { Length: > 0 } named ? named : "stable";
        var root = Environment.GetEnvironmentVariable("PI_SHARP_SELF_UPDATE_ROOT") is { Length: > 0 } dir ? dir : SelfUpdater.DefaultRoot();
        var updater = new SelfUpdater(new Uri(kbUri.GetLeftPart(UriPartial.Authority) + "/"), channel, Version, root,
            new HttpClient { Timeout = TimeSpan.FromMinutes(10) })
        { Log = line => Err.WriteLine(line) };
        try { updater.MarkGood(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Err.WriteLine($"self-update: off ({ex.Message})"); return null; }
        steering.IdleRestartReason = "a new pi-sharp build is staged";
        steering.AssignmentChanged = async ct =>
        {
            await updater.TickAsync(ct).ConfigureAwait(false);
            return updater.Ready is not null;
        };
        return updater;
    }

    /// <summary>A seat that worked through <see cref="SelfUpdater.MaxDefer"/> without an idle moment restarts between two
    /// turns: the run ends after the turn in flight, and the start prompt resumes its item on the new build.</summary>
    private static Func<TurnContext, ValueTask<TurnUpdate?>> SelfUpdateBetweenTurns(
        Func<TurnContext, ValueTask<TurnUpdate?>> prepare, CliState state, CancellationToken cancellationToken)
    {
        if (state.SelfUpdate is not { } updater || state.Factory is not { } factory) return prepare;
        return async turn =>
        {
            var update = await prepare(turn).ConfigureAwait(false);
            await updater.TickAsync(cancellationToken).ConfigureAwait(false);
            if (updater.Overdue) factory.RequestRestart("self-update to " + updater.Ready);
            return update;
        };
    }

    /// <summary>Between two turns the seat asks the broker (at most once a minute) and, when its assignment changed,
    /// runs the next turn on the new model with the same session; the session's compaction check sees the new
    /// window and compacts first when the context no longer fits it.</summary>
    private static Func<TurnContext, ValueTask<TurnUpdate?>> FollowAssignmentBetweenTurns(
        Func<TurnContext, ValueTask<TurnUpdate?>> prepare, CliState state, CancellationToken cancellationToken)
    {
        if (state.Assignment is not { } assignment) return prepare;
        return async turn =>
        {
            var update = await prepare(turn).ConfigureAwait(false);
            var generation = assignment.Generation;
            await assignment.CheckAsync(false, cancellationToken).ConfigureAwait(false);
            // A hybrid seat picks the side of every turn by the broker's share and the next request as the session
            // measures it (the compaction check below uses the same count; the last turn's usage left out the tool results).
            // A turn that called tools has acted on what it read: the watch is over (the next turns follow the share).
            if (turn.Message is { } last && last.ToolCalls.Any()) state.WatchTurn = false;
            var kind = assignment.Hybrid is { PicksByKind: true } pair
                ? TurnKinds.After(turn, pair.FollowupMaxTokens ?? SubscriptionAssignment.DefaultFollowupMaxTokens) : TurnKind.Unknown;
            assignment.PickTurn(state.Agent.EstimatedContextTokens(), state.WatchTurn, kind);
            if (!FollowAssignment(state))
            {
                // Same model, new band: the sizing may have changed (local sizing once past the first limit).
                if (assignment.Generation != generation)
                {
                    var before = state.Settings.CompactionTriggerTokens;
                    state.Settings = assignment.CompactionFor(state.Settings);
                    state.Agent.CompactionSettings = state.Settings.ToCompactionSettings();
                    // Said whenever it changes: a band change re-sized the seat without a word, so its log kept saying 180000
                    // while it compacted at 67,232 (2026-10-20).
                    if (state.Settings.CompactionTriggerTokens != before) assignment.Log?.Invoke(SizingLine(state, assignment));
                }
                return update;
            }
            return (update ?? new TurnUpdate()) with { Model = state.Model, ThinkingLevel = state.Thinking };
        };
    }

    /// <summary>Moves the seat onto the model its assignment names, if it is not on it yet: the model, its
    /// compaction limits and a model-change entry in the session, as the interactive /model does.</summary>
    internal static bool FollowAssignment(CliState state)
    {
        if (state.Assignment is not { } assignment || ReferenceEquals(state.Model, assignment.Model)) return false;
        var model = assignment.Model;
        state.Model = model;
        state.Thinking = assignment.CurrentEffort;
        state.Settings = assignment.CompactionFor(state.Settings);
        state.Agent.CompactionSettings = state.Settings.ToCompactionSettings();
        state.Agent.Session.AppendModelChange(model.Provider, model.Id);
        foreach (var tool in state.Context.Tools.OfType<ReadTool>())
        {
            tool.SupportsImages = model.SupportsImages;
            tool.ContextWindow = model.ContextWindow;
        }
        assignment.Log?.Invoke(SizingLine(state, assignment));
        return true;
    }

    /// <summary>What the seat runs and the compaction point it actually uses, with the model and window that size it (a
    /// hybrid seat sizes by its subscription side, or by its local side when every turn runs locally).</summary>
    internal static string SizingLine(CliState state, SeatAssignmentSwitch assignment)
    {
        var model = state.Model;
        var sizing = assignment.SizingModel ?? model;
        return $"now running {model.Provider} {model.Id}; compaction at {state.Settings.CompactionTriggerTokens} tokens down to {state.Settings.CompactionTargetTokens}"
            + $" (sized by {sizing.Id}, window {sizing.ContextWindow}, output {sizing.MaxOutputTokens})";
    }

    /// <summary>pi-sharp-hh-6: layers the budget reminder (one per threshold per task, durable in the
    /// notebook so a restart does not repeat it) over the loop's existing next-turn hook, the same way the
    /// guard reminder composes. A reminder is added only as trailing — never into the pinned prefix.</summary>
    private static AgentLoopConfig ComposeBudgetReminder(AgentLoopConfig config, TaskBudget budget)
    {
        var inner = config.PrepareNextTurn;
        return config with
        {
            PrepareNextTurn = async turn =>
            {
                var update = inner is null ? null : await inner(turn).ConfigureAwait(false);
                var reminder = budget.TakeDueReminder();
                if (reminder is null) return update;
                var trailing = new List<Message>(update?.Trailing ?? []);
                trailing.Add(new UserMessage(reminder));
                return (update ?? new TurnUpdate()) with { Trailing = trailing };
            },
        };
    }

    /// <summary>guard-rules-4: layers the completion-rule reminder (a single trailing message, replaced
    /// each turn, never stacked) over the loop's existing next-turn hook, the same way the
    /// verification stop gate composes its feedback.</summary>
    private static Func<TurnContext, ValueTask<TurnUpdate?>> ComposeGuardReminder(
        Func<TurnContext, ValueTask<TurnUpdate?>> prepare, GuardCompletionGate? guard)
    {
        if (guard is null) return prepare!;
        return async turn =>
        {
            var update = prepare is null ? null : await prepare(turn).ConfigureAwait(false);
            var reminder = guard.TakeReminder();
            if (reminder is null) return update;
            var trailing = new List<Message>(update?.Trailing ?? []);
            trailing.Add(new UserMessage(reminder));
            return (update ?? new TurnUpdate()) with { Trailing = trailing };
        };
    }

    /// <summary>The codeintel epic's diagnostics hook, composed outermost: after the verifier's and the
    /// factory's own next-turn notes, a non-empty code-intel diagnostics delta is added as a Trailing note.
    /// Only a turn that edited files touches the service; every failure is swallowed (fail open), so this
    /// wrapper never changes the turn on its own.</summary>
    private static Func<TurnContext, ValueTask<TurnUpdate?>>? PrepareNextTurn(
        AgentLoopConfig config,
        CliState state,
        PiSharp.Core.Verification.VerificationStopGate? verifier,
        PiSharp.Core.Factory.FactorySteering factory,
        CancellationToken cancellationToken)
    {
        var inner = verifier?.ComposePrepareNextTurn(factory.PrepareNextTurnAsync) ?? factory.PrepareNextTurnAsync;
        var feedback = state.CodeIntelFeedback;
        return feedback?.ComposePrepareNextTurn(inner) ?? inner;
    }

    /// <summary>The seat-side half of the guard-rules epic: replays successful tool calls through the
    /// seat's own guard rules (shadow only) and records the successful calls the replay gate counts
    /// against. A seat rule that matches a successful call is a false positive for that rule and is
    /// counted through its lifecycle; the model is never blocked by a seat rule here (a seat rule
    /// enforces only once a librarian publishes it as a fleet rule).</summary>
    private sealed class GuardSeatHook
    {
        private readonly NotebookStore _notebook;
        private readonly Func<string?> _taskKey;
        private readonly object _gate = new();
        private readonly System.Collections.Generic.Dictionary<string, PiSharp.Core.Guard.GuardLifecycle?> _lifecycles = new(System.StringComparer.Ordinal);
        private readonly System.Collections.Generic.List<(string Id, string Pattern, string Message)> _rules = [];
        private PiSharp.Core.Guard.GuardCallCapture? _capture;
        private string _captureTask = "";

        internal GuardSeatHook(NotebookStore notebook, Func<string?> taskKey)
        {
            _notebook = notebook;
            _taskKey = taskKey;
        }

        internal void RecordTurn(TurnContext turn)
        {
            try
            {
                string task;
                lock (_gate)
                {
                    task = _taskKey() ?? "seat";
                    if (_capture is null || _captureTask != task)
                    {
                        _capture = new PiSharp.Core.Guard.GuardCallCapture(_notebook, () => task);
                        _captureTask = task;
                    }
                    RefreshPatternsLocked();
                }
                foreach (var result in turn.ToolResults)
                {
                    if (result.IsError) continue;
                    var (args, call) = CallOf(result);
                    // Each named seat rule is evaluated against the successful call (shadow: a
                    // hit is a would-block and a false positive). A per-call evaluation is
                    // journaled on the rule's lifecycle, so shadow hits are journalled with the
                    // would-be-blocked call.
                    var ruleHits = new System.Collections.Generic.List<string>();
                    foreach (var (id, pattern, message) in _rules.ToArray())
                    {
                        if (!PiSharp.Core.Guard.ToolGuard.Matches(pattern, call)) continue;
                        ruleHits.Add(id);
                        LifecycleOf(id)?.RecordEvaluation(wouldBlock: true, blockedGoodCall: true);
                    }
                    // The per-call record: which successful call ran, and which of the seat's
                    // rules would have blocked it.
                    var body = new JsonObject
                    {
                        ["name"] = result.ToolName,
                        ["arguments"] = args,
                        ["toolCallId"] = result.ToolCallId,
                        ["wouldBlock"] = new JsonArray(ruleHits.Select(h => (JsonNode)JsonValue.Create(h)!).ToArray()),
                    };
                    _notebook.Write("guard-eval-" + result.ToolCallId, body.ToJsonString(),
                        title: "guard evaluation " + result.ToolName, tags: ["guard"], kind: "guard");
                    _capture.RecordSuccess(result.ToolName, args);
                }
            }
            catch { /* the guard must never disturb a turn */ }
        }

        /// <summary>Rebuilds the tool call from the result so a seat rule can be evaluated against the
        /// real name and arguments. The original arguments are preferred from the tool result's
        /// structured <c>Details</c>; when absent, the call carries an empty object (a seat rule
        /// then matches on the tool name alone, which is still a valid shadow signal).</summary>
        private static (string Args, ToolCallContent Call) CallOf(ToolResultMessage result)
        {
            var args = "";
            try { args = result.Details?["arguments"]?.ToJsonString() ?? ""; }
            catch { args = ""; }
            JsonObject node = new JsonObject();
            if (!string.IsNullOrWhiteSpace(args))
            {
                try { node = JsonNode.Parse(args) as JsonObject ?? node; }
                catch { node = new JsonObject(); }
            }
            return (args, new ToolCallContent(result.ToolCallId, result.ToolName, node));
        }

        /// <summary>Reloads the seat's own guard rules. A <c>guard</c> record carries <c>pattern</c>
        /// and <c>ruleId</c>; lifecycle records without a pattern are not rules and are skipped.</summary>
        private void RefreshPatternsLocked()
        {
            var current = new System.Collections.Generic.List<(string, string, string)>();
            foreach (var row in _notebook.List(kind: "guard"))
            {
                if (row["ruleId"]?.GetValue<string>() is not { } id || string.IsNullOrWhiteSpace(id)) continue;
                var text = row["text"]?.GetValue<string>();
                if (text is null) continue;
                JsonObject? body = null;
                try { body = JsonNode.Parse(text)?.AsObject(); }
                catch { body = null; }
                var pattern = body?["pattern"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(pattern)) continue;
                current.Add((id, pattern, body?["message"]?.GetValue<string>() ?? "guard rule " + id));
            }
            if (current.SequenceEqual(_rules)) return;
            _rules.Clear();
            _rules.AddRange(current);
            _lifecycles.Clear();
        }

        private PiSharp.Core.Guard.GuardLifecycle? LifecycleOf(string id)
        {
            lock (_gate)
            {
                if (!_lifecycles.TryGetValue(id, out var lifecycle))
                    _lifecycles[id] = lifecycle = PiSharp.Core.Guard.GuardLifecycle.Resume(_notebook, id);
                return lifecycle;
            }
        }
    }

    private static VerificationStopGate CreateStopGate(CliState state, FactorySteering factory, NotebookStore notebook)
    {
        var source = new ApprovedDoneCheckSource(state.Cwd);
        var verifier = new Verifier(state.Cwd, source.For, provenanceSource: source.ProvenanceFor);
        var defaultAttempts = state.Model.Id.Contains("miniclaude", StringComparison.OrdinalIgnoreCase) ? 3 : 5;
        var backwardReviewer = state.Settings.UseBackwardReviewer(state.Model)
            ? new BackwardReviewer(state.Cwd, state.Model, ReviewStream(state, "backward-review")) : null;
        // pi-sharp-hh-8: the rubric review sub-run. Its own model (settings.reviewModel, falling back to the
        // task model) and its own context; bash is guarded read-only. Usage flows through the seat's
        // stream, so it is charged like any other model call.
        var rubricReviewer = state.Settings.ReviewSubrun == true
            ? new RubricReviewer(state.Cwd, state.Model,
                ResolveModel(state.Models, state.Settings.ReviewModel, state.Model), ReviewStream(state, "rubric-review"),
                [new ReadTool(state.Cwd, supportsImages: false), new GrepTool(state.Cwd), new BashTool(state.Cwd)])
            : null;
        return new VerificationStopGate(verifier, notebook, () => factory.CurrentTaskKey,
            state.Settings.VerifyMaxAttempts ?? defaultAttempts,
            (task, failure, attempts, max, ct) => factory.HandleVerificationCapAsync(task, failure, attempts, max, ct),
            backwardReviewer,
            rubricReviewer);
    }

    /// <summary>pi-sharp-hh-8: resolves <c>settings.reviewModel</c> by id, then by name; null or unknown
    /// ids fall back to the task model.</summary>
    private static ModelConfig ResolveModel(IReadOnlyList<ModelConfig> models, string? reviewModel, ModelConfig fallback)
    {
        if (string.IsNullOrWhiteSpace(reviewModel)) return fallback;
        return models.FirstOrDefault(m => m.Id == reviewModel)
            ?? models.FirstOrDefault(m => string.Equals(m.Name, reviewModel, StringComparison.OrdinalIgnoreCase))
            ?? fallback;
    }

    /// <summary>The codeintel epic's hook factory: null when the feedback mode is off or the <c>code</c>
    /// MCP server is not connected. A connect error here is expected (no service on this seat) and must
    /// not be printed — the run is unaffected.</summary>
    private static PiSharp.Core.CodeIntelFeedback.CodeIntelFeedback? CreateCodeIntelFeedback(
        string cwd,
        McpManager mcp,
        Settings settings)
    {
        var codeIntel = settings.CodeIntel;
        if (codeIntel is null
            || codeIntel.Feedback == PiSharp.Core.Config.CodeIntelFeedbackMode.Off)
            return null;
        var client = mcp.Client(McpServerConfig.CodeServerAlias);
        if (client is null) return null;
        return new PiSharp.Core.CodeIntelFeedback.CodeIntelFeedback(client, cwd, codeIntel);
    }

    private static IReadOnlyList<Message> Concat(IReadOnlyList<Message> first, IReadOnlyList<Message> second)
    {
        if (second.Count == 0) return first;
        if (first.Count == 0) return second;
        var all = new List<Message>(first.Count + second.Count);
        all.AddRange(first);
        all.AddRange(second);
        return all;
    }

    private static SessionManager OpenSession(CliArgs args, string cwd, string sessionDir, bool persist)
    {
        if (!persist) return SessionManager.InMemory(cwd);
        if (args.Fork)
        {
            var source = !string.IsNullOrWhiteSpace(args.Session) ? Path.GetFullPath(ConfigPaths.ExpandHome(args.Session), cwd)
                : SessionManager.FindMostRecentSession(sessionDir, cwd) ?? throw new InvalidOperationException("--fork needs --session <file> or a recent session to continue (-c).");
            return SessionManager.Fork(source, cwd, sessionDir);
        }
        if (!string.IsNullOrWhiteSpace(args.Session))
        {
            var path = Path.GetFullPath(ConfigPaths.ExpandHome(args.Session), cwd);
            return SessionManager.Open(path, cwd);
        }
        if (args.Continue) return SessionManager.ContinueRecent(cwd, sessionDir);
        return SessionManager.Create(cwd, sessionDir);
    }

    private AgentSession NewSession(CliState state)
    {
        var mgr = state.Persist
            ? SessionManager.Create(state.Cwd, state.SessionDir ?? ConfigPaths.SessionDir(state.Cwd, state.ConfigDir))
            : SessionManager.InMemory(state.Cwd);
        mgr.AppendModelChange(state.Model.Provider, state.Model.Id);
        state.Context.Messages.Clear();
        return new AgentSession(mgr, state.Context, state.Settings.ToCompactionSettings(), OverflowDetectorFor(state.Model))
        {
            Notice = line => Err.WriteLine("[compaction] " + line),
            Hooks = FactoryCompactionHooks(state.Factory, state.FactoryActor, state.Cwd),
            NotebookState = state.Notebook is { } snapshotStore ? snapshotStore.StateReference : null,
            WorkingMessages = state.Artifacts is { } artifactView ? artifactView.PrepareMessages : null,
            NotebookTokens = state.Notebook is { } memory ? () => TokenEstimate.EstimateTextTokens(memory.Briefing()) : null,
        };
    }

    /// <summary>
    /// Compaction hooks for an attached seat: ask the model to save its work before a compaction, carry the
    /// unanswered inbox items and live facts (UTC, git) across it, and log each compaction. Null (plain CLI
    /// behaviour) when the seat is not attached to the factory.
    /// </summary>
    internal static CompactionHooks? FactoryCompactionHooks(FactorySteering? factory, string? actor, string cwd)
    {
        if (factory is null) return null;
        return new CompactionHooks
        {
            Actor = actor,
            SavePrompt = CompactionHooks.DefaultSavePrompt,
            OpenTaskCount = () => factory.OpenTasks.Count,
            CarryForward = () =>
            {
                var open = factory.OpenTasksNote();
                var facts = PiSharp.Core.Compaction.LiveFacts.Read(cwd);
                var carried = open is null ? facts : open + "\n\n" + facts;
                // One epic, one session: the epic's memory comes back after every compaction (operating-model plan, item 14).
                return factory.EpicMemoryNote() is { } epic ? carried + "\n\n" + epic : carried;
            },
            // wi-60: a running seat must actually compute the carried-across mark (NoteCompactionOccurred had
            // zero production callers before this): after every automatic compaction, mark exactly the items
            // the open-tasks note listed across it. The note is read here, before the mark is added, so the
            // suffix itself never re-marks on a later compaction.
            OnCompactionOccurred = note => factory.NoteCompactionOccurred(note),
            OnCompactionFailed = reason => factory.NoteCompactionFailed(reason),
        };
    }

    /// <summary>
    /// Serializes the machine-readable notice that a run died on a context overflow it could not
    /// recover from. Carries the provider error code and the terminal message so a supervisor (or
    /// the next run) sees why, and reads it off the event channel rather than parsing prose.
    /// </summary>
    private static string BuildOverflowEvent(ContextOverflowException ex)
    {
        var obj = new JsonObject
        {
            ["type"] = "context_overflow",
            ["message"] = ex.Message,
            ["recovered"] = false,
        };
        if (ex.Assistant is { } overflow)
        {
            if (overflow.ErrorCode is not null) obj["errorCode"] = overflow.ErrorCode;
            obj["stopReason"] = overflow.StopReason.ToString().ToLowerInvariant();
            obj["promptTokens"] = overflow.Usage.Input + overflow.Usage.CacheRead;
            obj["completionTokens"] = overflow.Usage.Output;
        }
        return obj.ToJsonString();
    }

    /// <summary>
    /// Builds the overflow recognizer for a model. The parsed provider error code leads; the
    /// model's <c>compat.overflowPatterns</c> from models.json extend the free-text fallback so a
    /// endpoint that words overflow differently can be taught without a code change.
    /// </summary>
    private static Func<AssistantMessage, int?, bool> OverflowDetectorFor(ModelConfig model)
    {
        var patterns = model.Compat.OverflowPatterns;
        if (patterns.Count == 0) return (message, window) => OverflowDetector.IsContextOverflow(message, window);
        return (message, window) => OverflowDetector.IsContextOverflow(message, window, patterns);
    }

    private static ModelConfig? ResolveModel(IReadOnlyList<ModelConfig> models, string? flag, string? setting)
    {
        if (!string.IsNullOrWhiteSpace(flag))
            return ModelsFile.Find(models, flag) ?? models.FirstOrDefault(m => m.Id == flag);
        if (!string.IsNullOrWhiteSpace(setting))
        {
            var found = ModelsFile.Find(models, setting);
            if (found is not null) return found;
        }
        return models.Count > 0 ? models[0] : null;
    }

    private static IReadOnlyList<IAgentTool> SelectTools(IReadOnlyList<IAgentTool> all, CliArgs args)
    {
        if (args.NoTools) return [];
        IEnumerable<IAgentTool> selected = all;
        if (args.Tools.Count > 0)
        {
            var allow = new HashSet<string>(args.Tools, StringComparer.OrdinalIgnoreCase);
            selected = selected.Where(t => allow.Contains(t.Name));
        }
        if (args.ExcludeTools.Count > 0)
        {
            var deny = new HashSet<string>(args.ExcludeTools, StringComparer.OrdinalIgnoreCase);
            selected = selected.Where(t => !deny.Contains(t.Name));
        }
        return selected.ToList();
    }

    private async Task<McpManager> ConnectMcpAsync(
        string cwd,
        string? globalDir,
        Settings settings,
        CancellationToken cancellationToken)
    {
        McpConfig config;
        try
        {
            config = McpConfig.LoadDefault(cwd, globalDir);
            // codeintel epic: inject the self-hosted code-intel 'code' server from the per-folder seat
            // config when an mcp.json does not already define one (a project file may override it).
            config = McpConfig.WithCodeServer(config, settings.CodeIntel ?? new CodeIntelSettings());
        }
        catch (Exception ex)
        {
            Err.WriteLine("MCP configuration error: " + ex.Message);
            return new McpManager();
        }

        var manager = McpFactory is { } factory
            ? await factory(config, cancellationToken).ConfigureAwait(false)
            : await McpManager.ConnectAllAsync(config, cancellationToken).ConfigureAwait(false);
        foreach (var connection in manager.Connections.Where(c => c.Error is not null))
            Err.WriteLine($"MCP server {connection.Alias} unavailable: {connection.Error}");
        return manager;
    }

    public static string InformationalVersion()
    {
        var attr = typeof(CliHost).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        return attr?.InformationalVersion ?? Version;
    }
}
