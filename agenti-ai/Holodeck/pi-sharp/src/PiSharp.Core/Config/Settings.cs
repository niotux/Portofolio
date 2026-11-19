// Layered settings.json: global ~/.config/pi-sharp then project .pi-sharp, then CLI overlays.
// Replaces pi: packages/coding-agent/src/core/settings-manager.ts (subset of keys) @ 71dca871b
using System.Text.Json.Nodes;
using PiSharp.Core.Compaction;
using PiSharp.Core.Models;
using PiSharp.Core.Agent;

namespace PiSharp.Core.Config;

/// <summary>One hook handler. <paramref name="Matcher"/>: a regex the tool name must match (PreToolUse, PostToolUse); null =
/// every call. Several handlers may share an event (Codex hooks: event, matcher group, handlers).</summary>
public sealed record CommandHookSetting(CommandHookEvent Event, string Command, string Version, TimeSpan Timeout, HookFailMode FailMode,
    string? Matcher = null);

/// <summary>User-editable settings. Unknown JSON keys are ignored. Null means "unset, inherit".</summary>
public sealed record Settings
{
    public string? DefaultModel { get; init; }
    public ThinkingLevel? DefaultThinkingLevel { get; init; }
    public double? Temperature { get; init; }
    public int? MaxTokens { get; init; }
    /// <summary>Loop-enforced maximum tool runtime in seconds; unset uses 900 seconds.</summary>
    public int? ToolCallTimeoutSeconds { get; init; }
    /// <summary>Opt-in command hooks, each with an event, executable command, and version label.</summary>
    public IReadOnlyList<CommandHookSetting>? CommandHooks { get; init; }
    /// <summary>Opt-in to loading hook blocks from active KB rule documents for an attached seat.</summary>
    public bool? CommandHooksFromKb { get; init; }
    public bool? CompactionEnabled { get; init; }
    public int? CompactionReserveTokens { get; init; }
    public int? CompactionKeepRecentTokens { get; init; }
    public int? CompactionTriggerTokens { get; init; }
    /// <summary>Settings key <c>compaction.targetTokens</c>: the whole context a compaction should leave behind; the kept tail is sized to fit it.</summary>
    public int? CompactionTargetTokens { get; init; }
    /// <summary>File checkpoints: snapshot the workspace after each mutating tool batch into a shadow git store (pi-sharp-checkpoints-1).</summary>
    public bool? CheckpointsEnabled { get; init; }
    /// <summary>pi-sharp-checkpoints-7: snapshot once more, after a successful reply, for every workspace
    /// root the before-batch hook already touched this turn. Project-layer only (<c>.pi-sharp</c>): a global
    /// <c>~/.config/pi-sharp</c> value is ignored, so <see cref="Merge"/> carries only the project value.</summary>
    public bool? CheckpointsAutoOnTurnEnd { get; init; }
    /// <summary>Commands whose successful exit marks the latest checkpoint green (pi-sharp-checkpoints-3).
    /// Null or empty means the built-in default (<c>dotnet build</c>, <c>dotnet test</c>).</summary>
    public IReadOnlyList<string>? CheckpointGreenCommands { get; init; }
    /// <summary>Failed green-command runs since the last green before the one-shot last-green hint is due (default 3).</summary>
    public int? CheckpointHintFailures { get; init; }
    public string? NotebookDirectory { get; init; }
    public string? NotebookEmbeddingUrl { get; init; }
    public string? NotebookEmbeddingModel { get; init; }
    public string? NotebookEmbeddingApiKeyEnv { get; init; }
    /// <summary>Enables the compact plan facade when notebook storage is configured. Unset on an attached seat: the factory switch
    /// <c>plan-tool</c> decides (the Engine Room page); true or false forces it.</summary>
    public bool? PlanTool { get; init; }
    /// <summary>How often the current plan step is re-recited in the notebook delta (default 8 turns).</summary>
    public int? PlanReciteEveryTurns { get; init; }
    /// <summary>Turns without plan progress before a one-time stall nudge is injected (default 15 for miniclaude, 30 elsewhere).</summary>
    public int? PlanStallTurns { get; init; }
    /// <summary>Enables the read-only explore sub-run tool (pi-sharp-hh-11) when notebook storage is configured. On by default.</summary>
    public bool? ExploreTool { get; init; }
    public int? TaskMaxTurns { get; init; }
    public int? TaskMaxOutputTokens { get; init; }
    public int? TaskMaxToolCalls { get; init; }
    public bool? VerifyOnStop { get; init; }
    public int? VerifyMaxAttempts { get; init; }
    /// <summary>Optional backward patch reviewer. <c>backward</c> is supported on subscription models only; null/off makes no extra model calls.</summary>
    public string? VerifyReviewer { get; init; }
    /// <summary>pi-sharp-hh-8: run the bounded rubric review sub-run when the stop gate is about to let a task
    /// move to review. A P0/P1 finding (confidence &gt;= 0.6) keeps the turn alive once.</summary>
    public bool? ReviewSubrun { get; init; }
    /// <summary>pi-sharp-hh-8: model id for the rubric review sub-run; unset reuses the task's model.</summary>
    public string? ReviewModel { get; init; }
    /// <summary>guard-rules-4: when true, a task may not <c>kb_transition</c> to review/done (and a stop is nudged, bounded)
    /// until a harness verification run exists for it. A <c>```completion```</c> fence with
    /// <c>{"testRun": true}</c> in the task's spec declares the same rule per item.</summary>
    public bool? GuardRequireTestRunBeforeReview { get; init; }
    /// <summary>guard-rules-4: per-task block cap per guard rule; after this many blocks the rule stops
    /// blocking in that task and logs one ops line. 0 = unlimited (off). Default 3.</summary>
    public int? GuardMaxBlocksPerRulePerTask { get; init; }
    /// <summary>guard-rules-4: completion-rule stop nudges per task before the run stops anyway. Default 3.</summary>
    public int? GuardCompletionMaxNudges { get; init; }
    /// <summary>pi-sharp-guard-cli: path to the fleet guard rules file (a JSON array of rule specs).
    /// When set and the file exists, those rules are the seat's active fleet set; otherwise the seeded
    /// fleet rules apply. Read by <c>pi-sharp guard list/explain</c> and by the seat's guard load path.</summary>
    public string? GuardRulesFile { get; init; }
    /// <summary>The guard over the main loop's tool calls: off, shadow (log only) or enforce. Default: enforce on a seat
    /// attached to the factory, shadow elsewhere (plan-pi-sharp-codex item 1).</summary>
    public string? GuardMode { get; init; }
    /// <summary>shellEnvironment.exclude: names (globs) taken out of the model's commands' environment; default on a seat
    /// attached to the factory: its token and anything named like a key, secret, token or password.</summary>
    public IReadOnlyList<string>? ShellEnvironmentExclude { get; init; }
    /// <summary>shellEnvironment.set: variables set for the model's commands.</summary>
    public IReadOnlyDictionary<string, string>? ShellEnvironmentSet { get; init; }
    /// <summary>Send apply_patch to the Codex backend as a free-form grammar tool (plan-pi-sharp-codex item 7). Off by default
    /// until checked live on one seat.</summary>
    public bool? FreeformApplyPatch { get; init; }
    /// <summary>Jobs that may run at once (default 4).</summary>
    public int? MaxJobs { get; init; }
    /// <summary>The most bytes of project instruction files in the prompt (Codex project_doc_max_bytes; default 32 KiB).</summary>
    public int? ProjectDocMaxBytes { get; init; }
    /// <summary>The most bytes of an attached seat's role texts in the prompt (default <see cref="PiSharp.Core.Factory.SeatStanding.DefaultMaxBytes"/>).</summary>
    public int? SeatStandingMaxBytes { get; init; }
    /// <summary>Let the provider compact (OpenAI Responses context_management) just below the seat's own compaction point
    /// (plan-pi-sharp-codex item 10). Off by default: compaction sizing is the owner's (Subscriptions page).</summary>
    public bool? ServerCompaction { get; init; }
    /// <summary>The task tool (helpers: triage, worker); on unless false.</summary>
    public bool? TaskTool { get; init; }
    /// <summary>The review tool (branch review before hand-in); on unless false.</summary>
    public bool? ReviewTool { get; init; }
    /// <summary>An attached seat sends its requests, tool calls, guard decisions and turn diffs to the hub
    /// (plan-pi-sharp-codex item 20); on unless false.</summary>
    public bool? Telemetry { get; init; }
    /// <summary>The goal audits (plan-pi-sharp-codex items 14-15: hand-in by criterion, audit before blocking, epic budget notices).
    /// Unset: each follows its factory switch, which the owner flips on the Engine Room page. true or false: forced on this seat.</summary>
    public bool? GoalAudits { get; init; }
    /// <summary>The output-token budget of an epic without its own attrs.budget_output_tokens (notices at 50/80/100%, with goalAudits).</summary>
    public long? EpicOutputBudget { get; init; }
    public IReadOnlyList<string> Skills { get; init; } = [];
    /// <summary>Maximum number of MCP skills listed in the prompt; 0 keeps them search-only.</summary>
    public int? RemoteSkillsPromptCap { get; init; }
    /// <summary>Image pixel threshold for read warnings; 0 disables the warning.</summary>
    public int? ImageWarnPixels { get; init; }
    public IReadOnlyList<string> Prompts { get; init; } = [];
    public bool? EnableSkillCommands { get; init; }
    public bool? HideThinking { get; init; }
    public bool? NoContextFiles { get; init; }
    /// <summary>AI Factory cubicle (chat) client; null or an empty <c>Url</c> means off.</summary>
    public CubicleSettings? Cubicle { get; init; }
    /// <summary>AI Factory attached mode; a set <c>actor</c> turns it on and the run does an identity preflight at
    /// startup. The token falls back to the <c>PI_FACTORY_TOKEN</c> environment variable and must never be written
    /// into a settings.json in a shared or tracked location.</summary>
    public FactorySettings? Factory { get; init; }
    /// <summary>Code-intel MCP wiring (codeintel epic): <c>feedback</c> mode off|errors|errors+warnings, <c>url</c> of the
    /// code-intel service and the <c>token</c> (per-folder seat config, else CODEINTEL_TOKEN, else PI_FACTORY_TOKEN).</summary>
    public CodeIntelSettings? CodeIntel { get; init; }

    public bool CompactionIsEnabled => CompactionEnabled ?? true;
    public bool SkillCommandsEnabled => EnableSkillCommands ?? true;
    public bool HideThinkingBlock => HideThinking ?? false;
    public bool SkipContextFiles => NoContextFiles ?? false;
    public bool UseBackwardReviewer(ModelConfig model) =>
        string.Equals(VerifyReviewer, "backward", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(model.Connection);

    /// <summary>The compaction settings as configured: what settings.json and the Subscriptions tab leave out comes from the
    /// model (<see cref="CompactionSettings.ReserveFor"/>, <see cref="CompactionSettings.KeepRecentFor"/>), never a default
    /// in code (digital-scribe, 2026-10-20).</summary>
    public CompactionSettings ToCompactionSettings() => new(
        Enabled: CompactionIsEnabled,
        ReserveTokens: CompactionReserveTokens,
        KeepRecentTokens: CompactionKeepRecentTokens,
        TriggerTokens: CompactionTriggerTokens,
        TargetTokens: CompactionTargetTokens);
}

/// <summary>Loads and merges settings.json files.</summary>
public static class SettingsLoader
{
    public static Settings Load(string cwd, string? globalDir = null)
    {
        var global = ParseFile(ConfigPaths.SettingsFile(ConfigPaths.GlobalConfigDir(globalDir)));
        var project = ParseFile(ConfigPaths.SettingsFile(ConfigPaths.ProjectConfigDir(cwd)));
        return Merge(global, project);
    }

    public static Settings ParseFile(string path)
    {
        if (!File.Exists(path)) return new Settings();
        try
        {
            return Parse(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Settings();
        }
    }

    public static Settings Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Settings();
        JsonNode? node;
        try { node = JsonNode.Parse(json); }
        catch (System.Text.Json.JsonException) { return new Settings(); }
        return node is JsonObject obj ? FromObject(obj) : new Settings();
    }

    public static Settings Merge(Settings lower, Settings higher) => new()
    {
        DefaultModel = higher.DefaultModel ?? lower.DefaultModel,
        DefaultThinkingLevel = higher.DefaultThinkingLevel ?? lower.DefaultThinkingLevel,
        Temperature = higher.Temperature ?? lower.Temperature,
        MaxTokens = higher.MaxTokens ?? lower.MaxTokens,
        ToolCallTimeoutSeconds = higher.ToolCallTimeoutSeconds ?? lower.ToolCallTimeoutSeconds,
        CommandHooks = higher.CommandHooks is { Count: > 0 } ? higher.CommandHooks : lower.CommandHooks,
        CommandHooksFromKb = higher.CommandHooksFromKb ?? lower.CommandHooksFromKb,
        CompactionEnabled = higher.CompactionEnabled ?? lower.CompactionEnabled,
        CompactionReserveTokens = higher.CompactionReserveTokens ?? lower.CompactionReserveTokens,
        CompactionKeepRecentTokens = higher.CompactionKeepRecentTokens ?? lower.CompactionKeepRecentTokens,
        CompactionTriggerTokens = higher.CompactionTriggerTokens ?? lower.CompactionTriggerTokens,
        CompactionTargetTokens = higher.CompactionTargetTokens ?? lower.CompactionTargetTokens,
        CheckpointsEnabled = higher.CheckpointsEnabled ?? lower.CheckpointsEnabled,
        // checkpoints.autoOnTurnEnd is project-layer only: the global value is ignored by design,
        // so the flag is the project value alone (null off).
        CheckpointsAutoOnTurnEnd = higher.CheckpointsAutoOnTurnEnd,
        CheckpointGreenCommands = (higher.CheckpointGreenCommands is { Count: > 0 } ? higher.CheckpointGreenCommands
            : lower.CheckpointGreenCommands) ?? null,
        CheckpointHintFailures = higher.CheckpointHintFailures ?? lower.CheckpointHintFailures,
        NotebookDirectory = higher.NotebookDirectory ?? lower.NotebookDirectory,
        NotebookEmbeddingUrl = higher.NotebookEmbeddingUrl ?? lower.NotebookEmbeddingUrl,
        NotebookEmbeddingModel = higher.NotebookEmbeddingModel ?? lower.NotebookEmbeddingModel,
        NotebookEmbeddingApiKeyEnv = higher.NotebookEmbeddingApiKeyEnv ?? lower.NotebookEmbeddingApiKeyEnv,
        PlanTool = higher.PlanTool ?? lower.PlanTool,
        PlanReciteEveryTurns = higher.PlanReciteEveryTurns ?? lower.PlanReciteEveryTurns,
        PlanStallTurns = higher.PlanStallTurns ?? lower.PlanStallTurns,
        ExploreTool = higher.ExploreTool ?? lower.ExploreTool,
        TaskMaxTurns = higher.TaskMaxTurns ?? lower.TaskMaxTurns,
        TaskMaxOutputTokens = higher.TaskMaxOutputTokens ?? lower.TaskMaxOutputTokens,
        TaskMaxToolCalls = higher.TaskMaxToolCalls ?? lower.TaskMaxToolCalls,
        VerifyOnStop = higher.VerifyOnStop ?? lower.VerifyOnStop,
        VerifyMaxAttempts = higher.VerifyMaxAttempts ?? lower.VerifyMaxAttempts,
        VerifyReviewer = higher.VerifyReviewer ?? lower.VerifyReviewer,
        ReviewSubrun = higher.ReviewSubrun ?? lower.ReviewSubrun,
        ReviewModel = higher.ReviewModel ?? lower.ReviewModel,
        GuardRequireTestRunBeforeReview = higher.GuardRequireTestRunBeforeReview ?? lower.GuardRequireTestRunBeforeReview,
        GuardMaxBlocksPerRulePerTask = higher.GuardMaxBlocksPerRulePerTask ?? lower.GuardMaxBlocksPerRulePerTask,
        GuardCompletionMaxNudges = higher.GuardCompletionMaxNudges ?? lower.GuardCompletionMaxNudges,
        GuardRulesFile = higher.GuardRulesFile ?? lower.GuardRulesFile,
        GuardMode = higher.GuardMode ?? lower.GuardMode,
        ShellEnvironmentExclude = higher.ShellEnvironmentExclude ?? lower.ShellEnvironmentExclude,
        ShellEnvironmentSet = higher.ShellEnvironmentSet ?? lower.ShellEnvironmentSet,
        FreeformApplyPatch = higher.FreeformApplyPatch ?? lower.FreeformApplyPatch,
        MaxJobs = higher.MaxJobs ?? lower.MaxJobs,
        ProjectDocMaxBytes = higher.ProjectDocMaxBytes ?? lower.ProjectDocMaxBytes,
        SeatStandingMaxBytes = higher.SeatStandingMaxBytes ?? lower.SeatStandingMaxBytes,
        ServerCompaction = higher.ServerCompaction ?? lower.ServerCompaction,
        TaskTool = higher.TaskTool ?? lower.TaskTool,
        ReviewTool = higher.ReviewTool ?? lower.ReviewTool,
        Telemetry = higher.Telemetry ?? lower.Telemetry,
        GoalAudits = higher.GoalAudits ?? lower.GoalAudits,
        EpicOutputBudget = higher.EpicOutputBudget ?? lower.EpicOutputBudget,
        Skills = ConcatDistinct(lower.Skills, higher.Skills),
        RemoteSkillsPromptCap = higher.RemoteSkillsPromptCap ?? lower.RemoteSkillsPromptCap,
        ImageWarnPixels = higher.ImageWarnPixels ?? lower.ImageWarnPixels,
        Prompts = ConcatDistinct(lower.Prompts, higher.Prompts),
        EnableSkillCommands = higher.EnableSkillCommands ?? lower.EnableSkillCommands,
        HideThinking = higher.HideThinking ?? lower.HideThinking,
        NoContextFiles = higher.NoContextFiles ?? lower.NoContextFiles,
        Cubicle = CubicleSettings.Merge(lower.Cubicle, higher.Cubicle),
        Factory = FactorySettings.Merge(lower.Factory, higher.Factory),
        CodeIntel = CodeIntelSettings.Merge(lower.CodeIntel, higher.CodeIntel),
    };

    internal static Settings FromObject(JsonObject obj)
    {
        var compaction = obj["compaction"] as JsonObject;
        var checkpointNode = obj["checkpoints"];
        var checkpoints = checkpointNode as JsonObject;
        return new Settings
        {
            DefaultModel = AsString(obj["defaultModel"]),
            DefaultThinkingLevel = ParseThinking(AsString(obj["defaultThinkingLevel"])),
            Temperature = AsDouble(obj["temperature"]),
            MaxTokens = AsInt(obj["maxTokens"]),
            ToolCallTimeoutSeconds = AsInt(obj["toolCallTimeoutSeconds"]),
            CommandHooks = ParseCommandHooks(obj["commandHooks"]),
            CommandHooksFromKb = AsBool(obj["commandHooksFromKb"]),
            CompactionEnabled = compaction is null ? null : AsBool(compaction["enabled"]),
            CompactionReserveTokens = compaction is null ? null : AsInt(compaction["reserveTokens"]),
            CompactionKeepRecentTokens = compaction is null ? null : AsInt(compaction["keepRecentTokens"]),
            CompactionTriggerTokens = compaction is null ? null : AsInt(compaction["triggerTokens"]),
            CompactionTargetTokens = compaction is null ? null : AsInt(compaction["targetTokens"]),
            CheckpointsEnabled = checkpoints is null ? AsBool(checkpointNode) : AsBool(checkpoints["enabled"]),
            CheckpointsAutoOnTurnEnd = checkpoints is null ? null : AsBool(checkpoints["autoOnTurnEnd"]),
            CheckpointGreenCommands = checkpoints is null ? null : ReadStringList(checkpoints["greenCommands"]),
            CheckpointHintFailures = checkpoints is null ? null : AsInt(checkpoints["hintFailures"]),
            NotebookDirectory = AsString(obj["notebookDirectory"]),
            NotebookEmbeddingUrl = AsString(obj["notebookEmbeddingUrl"]),
            NotebookEmbeddingModel = AsString(obj["notebookEmbeddingModel"]),
            NotebookEmbeddingApiKeyEnv = AsString(obj["notebookEmbeddingApiKeyEnv"]),
            PlanTool = AsBool(obj["planTool"]),
            PlanReciteEveryTurns = AsInt(obj["planReciteEveryTurns"]),
            PlanStallTurns = AsInt(obj["planStallTurns"]),
            ExploreTool = AsBool(obj["exploreTool"]),
            TaskMaxTurns = AsInt(obj["taskMaxTurns"]),
            TaskMaxOutputTokens = AsInt(obj["taskMaxOutputTokens"]),
            TaskMaxToolCalls = AsInt(obj["taskMaxToolCalls"]),
            VerifyOnStop = AsBool(obj["verifyOnStop"]),
            VerifyMaxAttempts = AsInt(obj["verifyMaxAttempts"]),
            VerifyReviewer = AsString(obj["verifyReviewer"]),
            ReviewSubrun = AsBool(obj["reviewSubrun"]),
            ReviewModel = AsString(obj["reviewModel"]),
            GuardRequireTestRunBeforeReview = AsBool(obj["guardRequireTestRunBeforeReview"]),
            GuardMaxBlocksPerRulePerTask = AsInt(obj["guardMaxBlocksPerRulePerTask"]),
            GuardCompletionMaxNudges = AsInt(obj["guardCompletionMaxNudges"]),
            GuardRulesFile = AsString(obj["guardRulesFile"]),
            GuardMode = AsString(obj["guardMode"]),
            FreeformApplyPatch = AsBool(obj["freeformApplyPatch"]),
            MaxJobs = AsInt(obj["maxJobs"]),
            ProjectDocMaxBytes = AsInt(obj["projectDocMaxBytes"]),
            SeatStandingMaxBytes = AsInt(obj["seatStandingMaxBytes"]),
            ServerCompaction = AsBool(obj["serverCompaction"]),
            TaskTool = AsBool(obj["taskTool"]),
            ReviewTool = AsBool(obj["reviewTool"]),
            Telemetry = AsBool(obj["telemetry"]),
            GoalAudits = AsBool(obj["goalAudits"]),
            EpicOutputBudget = AsInt(obj["epicOutputBudget"]),
            ShellEnvironmentExclude = obj["shellEnvironment"] is JsonObject shellExclude ? ReadStringList(shellExclude["exclude"]) : null,
            ShellEnvironmentSet = obj["shellEnvironment"]?["set"] is JsonObject shellSet
                ? shellSet.Where(p => p.Value is JsonValue).ToDictionary(p => p.Key, p => p.Value!.ToString()) : null,
            Skills = ReadStringList(obj["skills"]),
            RemoteSkillsPromptCap = AsInt(obj["remoteSkillsPromptCap"]),
            ImageWarnPixels = obj["images"] is JsonObject images
                ? AsInt(images["warnPixels"])
                : null,
            Prompts = ReadStringList(obj["prompts"]),
            EnableSkillCommands = AsBool(obj["enableSkillCommands"]),
            HideThinking = AsBool(obj["hideThinkingBlock"]),
            NoContextFiles = AsBool(obj["noContextFiles"]),
            Cubicle = obj["cubicle"] is JsonObject cubicle ? CubicleSettings.FromObject(cubicle) : null,
            Factory = obj["factory"] is JsonObject factory ? FactorySettings.FromObject(factory) : null,
            CodeIntel = obj["codeintel"] is JsonObject codeIntel ? CodeIntelSettings.FromObject(codeIntel) : null,
        };
    }

    private static IReadOnlyList<CommandHookSetting>? ParseCommandHooks(JsonNode? node)
    {
        if (node is not JsonArray array) return null;
        var hooks = new List<CommandHookSetting>();
        foreach (var item in array.OfType<JsonObject>())
        {
            if (!Enum.TryParse<CommandHookEvent>(AsString(item["event"]), true, out var hookEvent)) continue;
            var command = AsString(item["command"]);
            if (string.IsNullOrWhiteSpace(command)) continue;
            var failMode = string.Equals(AsString(item["failMode"]), "closed", StringComparison.OrdinalIgnoreCase) ? HookFailMode.Closed : HookFailMode.Open;
            hooks.Add(new CommandHookSetting(hookEvent, command, AsString(item["version"]) ?? "1",
                TimeSpan.FromSeconds(Math.Clamp(AsInt(item["timeoutSeconds"]) ?? 5, 1, 300)), failMode, AsString(item["matcher"])));
        }
        return hooks;
    }

    internal static IReadOnlyList<string> ConcatDistinct(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        if (a.Count == 0) return b;
        if (b.Count == 0) return a;
        var seen = new HashSet<string>(a, StringComparer.Ordinal);
        var list = new List<string>(a);
        foreach (var item in b)
        {
            if (seen.Add(item)) list.Add(item);
        }
        return list;
    }

    internal static IReadOnlyList<string> ReadStringList(JsonNode? node)
    {
        if (node is not JsonArray array) return [];
        var list = new List<string>();
        foreach (var item in array)
        {
            var s = AsString(item);
            if (!string.IsNullOrWhiteSpace(s)) list.Add(s);
        }
        return list;
    }

    internal static string? AsString(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static bool? AsBool(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<bool>(out var b) ? b : null;

    internal static int? AsInt(JsonNode? node)
    {
        if (node is not JsonValue v) return null;
        if (v.TryGetValue<int>(out var i)) return i;
        if (v.TryGetValue<double>(out var d)) return (int)d;
        return null;
    }

    private static double? AsDouble(JsonNode? node)
    {
        if (node is not JsonValue v) return null;
        if (v.TryGetValue<double>(out var d)) return d;
        if (v.TryGetValue<int>(out var i)) return i;
        return null;
    }

    private static ThinkingLevel? ParseThinking(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return Enum.TryParse<ThinkingLevel>(raw.Replace("-", ""), ignoreCase: true, out var level) ? level : null;
    }
}

/// <summary>Settings key <c>cubicle</c>: <c>url</c>, <c>token</c> (fallback env PI_FACTORY_TOKEN),
/// <c>followUpWaitSeconds</c> (0 = off), optional <c>about</c> list attached to every trace event.</summary>
public sealed record CubicleSettings
{
    public const string TokenEnv = "PI_FACTORY_TOKEN";

    public string? Url { get; init; }
    public string? Token { get; init; }
    public int? FollowUpWaitSeconds { get; init; }
    public IReadOnlyList<string> About { get; init; } = [];

    public bool Enabled => !string.IsNullOrWhiteSpace(Url);

    /// <summary>Token from settings, else from the PI_FACTORY_TOKEN environment variable.</summary>
    public string? ResolveToken() =>
        !string.IsNullOrEmpty(Token) ? Token : Environment.GetEnvironmentVariable(TokenEnv);

    public static CubicleSettings? Merge(CubicleSettings? lower, CubicleSettings? higher)
    {
        if (lower is null) return higher;
        if (higher is null) return lower;
        return new CubicleSettings
        {
            Url = higher.Url ?? lower.Url,
            Token = higher.Token ?? lower.Token,
            FollowUpWaitSeconds = higher.FollowUpWaitSeconds ?? lower.FollowUpWaitSeconds,
            About = SettingsLoader.ConcatDistinct(lower.About, higher.About),
        };
    }

    internal static CubicleSettings FromObject(JsonObject obj) => new()
    {
        Url = SettingsLoader.AsString(obj["url"]),
        Token = SettingsLoader.AsString(obj["token"]),
        FollowUpWaitSeconds = SettingsLoader.AsInt(obj["followUpWaitSeconds"]),
        About = SettingsLoader.ReadStringList(obj["about"]),
    };
}

/// <summary>Settings key <c>codeintel</c>: the self-hosted code-intel MCP service (codeintel epic).
/// <c>feedback</c> is off (no diagnostics notes), errors (default) or errors+warnings; <c>url</c> points at the
/// service and is the opt-in: with neither it nor <c>CODEINTEL_URL</c> set, no <c>code</c> server is added
/// (a guessed default made every seat that has a factory token wait out a connect timeout at startup);
/// <c>token</c> is the per-folder seat token — it falls back to <c>CODEINTEL_TOKEN</c>, then
/// <c>PI_FACTORY_TOKEN</c> — and must never be written into a tracked settings.json.</summary>
public sealed record CodeIntelSettings
{
    public const string UrlEnv = "CODEINTEL_URL";
    public const string TokenEnv = "CODEINTEL_TOKEN";

    /// <summary>The <c>feedback</c> mode: off = never inject diagnostics notes, errors = the default (notes only on
    /// new errors), errors+warnings = notes on new errors and warnings the language server reports.</summary>
    public CodeIntelFeedbackMode Feedback { get; init; } = CodeIntelFeedbackMode.Unset;
    public string? Url { get; init; }
    public string? Token { get; init; }

    /// <summary>The effective URL: settings, else CODEINTEL_URL, else null (code-intel not configured).</summary>
    public string? ResolveUrl() =>
        !string.IsNullOrWhiteSpace(Url) ? Url
        : Environment.GetEnvironmentVariable(UrlEnv) is { Length: > 0 } env ? env
        : null;

    /// <summary>The effective token: settings, else CODEINTEL_TOKEN, else the KB actor token PI_FACTORY_TOKEN.</summary>
    public string? ResolveToken() =>
        !string.IsNullOrEmpty(Token) ? Token
        : Environment.GetEnvironmentVariable(TokenEnv)
        ?? Environment.GetEnvironmentVariable(CubicleSettings.TokenEnv);

    public static CodeIntelSettings? Merge(CodeIntelSettings? lower, CodeIntelSettings? higher)
    {
        if (lower is null) return higher;
        if (higher is null) return lower;
        return new CodeIntelSettings
        {
            Feedback = higher.Feedback != CodeIntelFeedbackMode.Unset ? higher.Feedback : lower.Feedback,
            Url = higher.Url ?? lower.Url,
            Token = higher.Token ?? lower.Token,
        };
    }

    internal static CodeIntelSettings FromObject(JsonObject obj)
    {
        static string? S(JsonNode? n) => n is JsonValue v ? v.GetValue<string>() : null;
        return new CodeIntelSettings
        {
            Feedback = ParseFeedback(S(obj["feedback"])),
            Url = S(obj["url"]),
            Token = S(obj["token"]),
        };
    }

    private static CodeIntelFeedbackMode ParseFeedback(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return CodeIntelFeedbackMode.Unset;
        var value = raw.Trim();
        return value.ToLowerInvariant() switch
        {
            "off" or "none" or "disabled" => CodeIntelFeedbackMode.Off,
            "errors" => CodeIntelFeedbackMode.Errors,
            "errors+warnings" or "errors and warnings" or "all" => CodeIntelFeedbackMode.ErrorsAndWarnings,
            _ => CodeIntelFeedbackMode.Unset,
        };
    }
}

/// <summary>The <c>codeintel.feedback</c> modes. <c>Unset</c> (no setting at all) behaves like <see cref="Errors"/>.</summary>
public enum CodeIntelFeedbackMode
{
    Unset,
    Off,
    Errors,
    ErrorsAndWarnings,
}

/// <summary>Settings key <c>factory</c>: <c>kbUrl</c>, <c>chatUrl</c>, <c>actor</c> (set = attached mode),
/// <c>token</c> (fallback env PI_FACTORY_TOKEN), optional <c>idleExitSeconds</c>.</summary>
public sealed record FactorySettings
{
    public const string TokenEnv = "PI_FACTORY_TOKEN";

    public string? KbUrl { get; init; }
    public string? ChatUrl { get; init; }
    public string? Actor { get; init; }
    public string? Token { get; init; }
    /// <summary>Optional: end the attached run after this many idle seconds with no inbox traffic
    /// (null = stay attached until cancelled).</summary>
    public int? IdleExitSeconds { get; init; }
    /// <summary>Key <c>freshContext</c>: <c>arrival</c> (default: any inbox arrival after a turn with no tool calls
    /// starts from an empty context) or <c>assignment</c> (only a <c>/fresh-task</c> item from <see cref="FreshTaskFrom"/>
    /// does, and only once the previous task is delivered) or <c>backlog</c> (the team lead and product owner: the
    /// context is cleared only when the seat moves from one of <see cref="Backlogs"/> to another).</summary>
    public string? FreshContext { get; init; }
    /// <summary>Key <c>freshTaskFrom</c>: the actors whose <c>/fresh-task</c> may clear this seat's context.</summary>
    public IReadOnlyList<string>? FreshTaskFrom { get; init; }
    /// <summary>Key <c>correctionFrom</c>: actor slugs whose inbox replies may be captured as corrections.</summary>
    public IReadOnlyList<string>? CorrectionFrom { get; init; }
    /// <summary>Key <c>backlogs</c>: <c>[{"name", "products": [slugs], "rooms": [room slugs]}]</c>, used by
    /// <c>freshContext = "backlog"</c>.</summary>
    public IReadOnlyList<Factory.FactoryBacklog>? Backlogs { get; init; }
    /// <summary>Key <c>workArea</c>: the directory holding the seat's per-item working directories
    /// (<c>workArea/&lt;wi-slug&gt;</c>), where the paused handover commits and pushes WIP (rate-bands-d).
    /// Null = the <c>work</c> directory under the user profile.</summary>
    public string? WorkArea { get; init; }

    /// <summary>Attached mode is on when an actor slug is configured; the run then verifies that identity first.</summary>
    public bool Attached => !string.IsNullOrWhiteSpace(Actor);

    /// <summary>Token from settings, else from the PI_FACTORY_TOKEN environment variable.</summary>
    public string? ResolveToken() =>
        !string.IsNullOrEmpty(Token) ? Token : Environment.GetEnvironmentVariable(TokenEnv);

    public static FactorySettings? Merge(FactorySettings? lower, FactorySettings? higher)
    {
        if (lower is null) return higher;
        if (higher is null) return lower;
        return new FactorySettings
        {
            KbUrl = higher.KbUrl ?? lower.KbUrl,
            ChatUrl = higher.ChatUrl ?? lower.ChatUrl,
            Actor = higher.Actor ?? lower.Actor,
            Token = higher.Token ?? lower.Token,
            IdleExitSeconds = higher.IdleExitSeconds ?? lower.IdleExitSeconds,
            FreshContext = higher.FreshContext ?? lower.FreshContext,
            FreshTaskFrom = higher.FreshTaskFrom ?? lower.FreshTaskFrom,
            CorrectionFrom = higher.CorrectionFrom ?? lower.CorrectionFrom,
            Backlogs = higher.Backlogs ?? lower.Backlogs,
            WorkArea = higher.WorkArea ?? lower.WorkArea,
        };
    }

    private static IReadOnlyList<Factory.FactoryBacklog>? ReadBacklogs(JsonNode? node)
    {
        if (node is not JsonArray array) return null;
        var backlogs = new List<Factory.FactoryBacklog>();
        foreach (var entry in array.OfType<JsonObject>())
        {
            if (SettingsLoader.AsString(entry["name"]) is not { Length: > 0 } name) continue;
            backlogs.Add(new Factory.FactoryBacklog(name,
                entry["products"] is null ? [] : SettingsLoader.ReadStringList(entry["products"]),
                entry["rooms"] is null ? [] : SettingsLoader.ReadStringList(entry["rooms"])));
        }
        return backlogs;
    }

    internal static FactorySettings FromObject(JsonObject obj) => new()
    {
        KbUrl = SettingsLoader.AsString(obj["kbUrl"]),
        ChatUrl = SettingsLoader.AsString(obj["chatUrl"]),
        Actor = SettingsLoader.AsString(obj["actor"]),
        Token = SettingsLoader.AsString(obj["token"]),
        IdleExitSeconds = SettingsLoader.AsInt(obj["idleExitSeconds"]),
        FreshContext = SettingsLoader.AsString(obj["freshContext"]),
        FreshTaskFrom = obj["freshTaskFrom"] is null ? null : SettingsLoader.ReadStringList(obj["freshTaskFrom"]),
        CorrectionFrom = obj["correctionFrom"] is null ? null : SettingsLoader.ReadStringList(obj["correctionFrom"]),
        Backlogs = ReadBacklogs(obj["backlogs"]),
        WorkArea = SettingsLoader.AsString(obj["workArea"]),
    };
}
