// plan-pi-sharp-codex item 16 (Codex subagents): helper sub-runs with their own context for work whose raw output would fill
// the seat's context: a test run or a log to read (role triage), a self-contained change (role worker), or a role a librarian
// defined in the KB. Only a summary comes back. A helper runs to its end in the call (the default), or in the background:
// spawn, then message it, wait for it, close it (Codex's spawn_agent, send_input, wait, close_agent). Each has capped turns and
// output tokens charged to a TaskBudget row; at most MaxHelpers at once and one writer (two helpers editing one tree collide).
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using PiSharp.Core.Agent;
using PiSharp.Core.Guard;
using PiSharp.Core.Messages;
using PiSharp.Core.Models;
using PiSharp.Core.Notebook;

namespace PiSharp.Tools;

/// <summary>Helper sub-runs (triage, worker, or a role from the KB) with their own context and budget.</summary>
public sealed partial class TaskTool : IAgentTool, IAsyncDisposable
{
    public const int DefaultMaxTurns = 16;
    public const int DefaultMaxOutputTokens = 40_000;
    public const int DefaultFindingsTokens = 2_500;
    public const int MaxHelpers = 3;
    public const int CompletedRetention = 32;
    public const int MaxWaitSeconds = 900;
    public static readonly TimeSpan DefaultMaxDuration = TimeSpan.FromMinutes(30);

    /// <summary>A helper's job: what it is told and which tools it gets.</summary>
    public sealed record Role(string Name, string Instructions, bool Writes);

    public static readonly IReadOnlyDictionary<string, Role> Roles = new Dictionary<string, Role>(StringComparer.Ordinal)
    {
        ["triage"] = new("triage",
            "You are a triage helper inside a coding seat. Run what the task names (a test suite, a build, a script) or read the " +
            "logs it names, and report only what the caller needs to act: the exact command, the counts (passed, failed, " +
            "skipped), and for each failure its test or step, its cause in one line, and the file:line it points at. Group " +
            "failures that share a cause. Do not fix, edit or write anything; do not commit or push.", Writes: false),
        ["worker"] = new("worker",
            "You are a worker helper inside a coding seat. Make the self-contained change the task describes, in the files it " +
            "names, following the code around it. Check it the way the task says (build or the narrowest tests). Do not commit, " +
            "push or touch files the task does not concern. Report the files you changed, what you changed in a line each, and " +
            "how you checked it.", Writes: true),
    };

    private static readonly JsonObject ArgsSchema = JsonNode.Parse("""
        {
          "type": "object",
          "properties": {
            "op": { "type": "string", "enum": ["run", "spawn", "message", "wait", "close", "list"], "description": "run (default): the helper runs to its end and its summary comes back; spawn: it runs in the background and its id comes back; message: send it more to do (it reads it before its next step); wait: until it ends or timeout_seconds; close: stop it; list: the helpers and their state" },
            "role": { "type": "string", "description": "triage: run tests or read logs and return only the failures and their causes; worker: make a self-contained change and report it; or a role the KB defines" },
            "task": { "type": "string", "description": "What to do, with the paths and commands it needs: the helper sees nothing of your conversation" },
            "id": { "type": "string", "description": "message, wait, close: the helper's id from spawn" },
            "text": { "type": "string", "description": "message: what to tell it" },
            "timeout_seconds": { "type": "number", "description": "wait: how long to wait (default 300, max 900)" }
          }
        }
        """)!.AsObject();

    private sealed class Helper(string id, Role role, string task, string scope, CancellationTokenSource stop)
    {
        public string Id { get; } = id;
        public Role Role { get; } = role;
        public string Task { get; } = task;
        public string Scope { get; } = scope;
        public CancellationTokenSource Stop { get; } = stop;
        public ConcurrentQueue<Message> Inbox { get; } = new();
        public Task<AgentToolResult> Run { get; set; } = System.Threading.Tasks.Task.FromResult(AgentToolResult.FromText(""));
        public int OutputTokens;
    }

    private readonly string _cwd;
    private readonly Func<(ModelConfig Model, StreamFunction Stream)> _runtime;
    private readonly Func<string> _taskKey;
    private readonly NotebookStore? _store;
    private readonly Func<IReadOnlyList<IAgentTool>, IReadOnlyList<IAgentTool>>? _wrapTools;
    private readonly Dictionary<string, Helper> _helpers = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed;
    public Action<string, string, Usage>? UsageRecorded { get; set; }

    /// <summary>Task changes revoke old helpers before the parent makes another model request.</summary>
    public void StopOutsideScope()
    {
        var scope = _taskKey();
        lock (_helpers)
            foreach (var helper in _helpers.Values.Where(h => h.Scope != scope && !h.Run.IsCompleted)) helper.Stop.Cancel();
    }

    /// <param name="guard">The seat's guard, applied to the helper's calls too.</param>
    /// <param name="reviewer">Decides the helper's Ask calls, as for the seat.</param>
    /// <param name="wrapTools">The seat's wrapping of tools (artifact budgets), applied to the helper's.</param>
    public TaskTool(string cwd, Func<(ModelConfig Model, StreamFunction Stream)> runtime, Func<string> taskKey,
        NotebookStore? store = null, ToolGuard? guard = null, IActionReviewer? reviewer = null,
        Func<IReadOnlyList<IAgentTool>, IReadOnlyList<IAgentTool>>? wrapTools = null)
    {
        _cwd = cwd;
        _runtime = runtime;
        _taskKey = taskKey;
        _store = store;
        Guard = guard;
        Reviewer = reviewer;
        _wrapTools = wrapTools;
    }

    public ToolGuard? Guard { get; set; }
    public IActionReviewer? Reviewer { get; set; }

    /// <summary>The seat's command hooks: SubagentStart and SubagentStop fire around each helper, and the rest run inside it.</summary>
    public Func<CommandHookRunner?>? Hooks { get; set; }

    /// <summary>Roles a librarian defined in the KB's active rules (<see cref="ParseRoles"/>), beside the built-in ones.</summary>
    public IReadOnlyDictionary<string, Role> ExtraRoles { get; set; } = new Dictionary<string, Role>();

    public const string ToolName = "task";
    public string Name => ToolName;
    public string Label => ToolName;
    public string Description =>
        "Hand work to a helper with its own context; only its summary comes back to yours (Codex subagents). role=triage runs " +
        "a test suite or build, or reads logs, and returns just the failures and their causes: use it for full test runs and " +
        "long logs instead of reading their output yourself. role=worker makes a self-contained change and reports the files " +
        "changed. op=spawn runs one in the background (then message, wait, close); up to " + MaxHelpers + " at once, one worker. " +
        "Capped at " + DefaultMaxTurns + " turns each. Give it everything it needs in the task text." +
        (ExtraRoles.Count > 0 ? " Roles from the KB: " + string.Join(", ", ExtraRoles.Keys) + "." : "");
    public JsonObject Parameters => ArgsSchema;
    public ToolExecutionMode? ExecutionMode => ToolExecutionMode.Sequential;
    public TimeSpan? MaxDuration => DefaultMaxDuration;

    public async Task<AgentToolResult> ExecuteAsync(string toolCallId, JsonObject arguments, Action<AgentToolResult>? onUpdate,
        CancellationToken cancellationToken)
    {
        var op = arguments["op"]?.GetValue<string>() ?? "run";
        switch (op)
        {
            case "run":
            case "spawn":
            {
                var roleName = arguments["role"]?.GetValue<string>() ?? throw new ToolArgumentException("role required: triage, worker" + RoleList());
                if (!Roles.TryGetValue(roleName, out var role) && !ExtraRoles.TryGetValue(roleName, out role))
                    throw new ToolArgumentException($"role '{roleName}' is not one of triage, worker{RoleList()}");
                var task = arguments["task"]?.GetValue<string>() ?? throw new ToolArgumentException("task required: what the helper does");
                var (helper, refusal) = await StartAsync(role, task, op == "spawn" ? CancellationToken.None : cancellationToken).ConfigureAwait(false);
                if (helper is null) return AgentToolResult.Error(refusal!);
                if (op == "spawn")
                    return AgentToolResult.FromText($"Helper {helper.Id} ({role.Name}) started in the background. Use op=wait id={helper.Id} for its summary, op=message to tell it more, op=close to stop it.",
                        new JsonObject { ["subRunId"] = helper.Id, ["role"] = role.Name, ["state"] = "running" });
                return await helper.Run.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            case "message":
            {
                var helper = Find(arguments);
                var text = arguments["text"]?.GetValue<string>() ?? throw new ToolArgumentException("text required: what to tell the helper");
                if (helper.Run.IsCompleted) return AgentToolResult.Error($"Helper {helper.Id} has ended; spawn a new one with what it should do.");
                helper.Inbox.Enqueue(new UserMessage("[from the seat] " + text));
                return AgentToolResult.FromText($"Told helper {helper.Id}: it reads it before its next step.");
            }
            case "wait":
            {
                var helper = Find(arguments);
                var seconds = Math.Clamp(WriteStdinTool.Number(arguments["timeout_seconds"]) ?? 300, 1, MaxWaitSeconds);
                await Task.WhenAny(helper.Run, Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken)).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                return helper.Run.IsCompleted ? await helper.Run.ConfigureAwait(false)
                    : AgentToolResult.FromText($"Helper {helper.Id} ({helper.Role.Name}) is still running after {seconds:0} s ({Volatile.Read(ref helper.OutputTokens)} output tokens so far).",
                        new JsonObject { ["subRunId"] = helper.Id, ["state"] = "running" });
            }
            case "close":
            {
                var helper = Find(arguments);
                if (!helper.Run.IsCompleted) helper.Stop.Cancel();
                try { await helper.Run.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false); }
                catch (TimeoutException) { return AgentToolResult.Error($"Helper {helper.Id} is stopping; it has not closed yet."); }
                return AgentToolResult.FromText($"Helper {helper.Id} closed.");
            }
            case "list":
            {
                Helper[] all;
                lock (_helpers) all = [.. _helpers.Values];
                return AgentToolResult.FromText(all.Length == 0 ? "No helpers." : string.Join("\n", all.Select(h =>
                    $"{h.Id} {h.Role.Name}: {(h.Run.IsCompleted ? "ended" : "running")}, {Volatile.Read(ref h.OutputTokens)} output tokens: {Cut(h.Task, 120)}")));
            }
            default:
                throw new ToolArgumentException($"op '{op}' is not run, spawn, message, wait, close or list");
        }
    }

    private string RoleList() => ExtraRoles.Count == 0 ? "" : ", " + string.Join(", ", ExtraRoles.Keys);

    private Helper Find(JsonObject arguments)
    {
        var id = arguments["id"]?.GetValue<string>() ?? throw new ToolArgumentException("id required: the helper's id from spawn");
        lock (_helpers)
            return _helpers.TryGetValue(id, out var helper) ? helper : throw new ToolArgumentException($"no helper '{id}' (op=list shows them)");
    }

    /// <summary>Claims a slot (at most <see cref="MaxHelpers"/> running, one writer) and starts the helper; or why not.</summary>
    private async Task<(Helper? Helper, string? Refusal)> StartAsync(Role role, string task, CancellationToken callToken)
    {
        var hooks = Hooks?.Invoke();
        if (hooks is not null && hooks.Has(CommandHookEvent.SubagentStart))
        {
            var start = await hooks.InvokeAsync(CommandHookEvent.SubagentStart, new JsonObject { ["role"] = role.Name, ["task"] = task }, callToken).ConfigureAwait(false);
            if (start.Block) return (null, "task: the SubagentStart hook refused it: " + (start.Reason ?? "no reason given"));
            if (start.AdditionalContext is { Length: > 0 } added) task += "\n\n[hook] " + added;
        }
        var id = "task-" + role.Name + "-" + Guid.NewGuid().ToString("N");
        lock (_helpers)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var scope = _taskKey();
            foreach (var old in _helpers.Values.Where(h => h.Scope != scope && !h.Run.IsCompleted)) old.Stop.Cancel();
            foreach (var old in _helpers.Values.Where(h => h.Run.IsCompleted).Take(Math.Max(0, _helpers.Values.Count(h => h.Run.IsCompleted) - CompletedRetention + 1)).ToArray())
            { _helpers.Remove(old.Id); old.Stop.Dispose(); }
            var running = _helpers.Values.Where(h => !h.Run.IsCompleted).ToList();
            if (running.Count >= MaxHelpers) return (null, $"task: {MaxHelpers} helpers are already running on this seat; wait for one or close one first.");
            if (role.Writes && running.Any(h => h.Role.Writes)) return (null, "task: another helper that edits files is already running on this seat; wait for it to finish first.");
            var helper = new Helper(id, role, task, scope, CancellationTokenSource.CreateLinkedTokenSource(callToken, _lifetime.Token));
            helper.Stop.CancelAfter(DefaultMaxDuration);
            _helpers[id] = helper;
            helper.Run = Task.Run(() => RunHelperAsync(helper, hooks));
            return (helper, null);
        }
    }

    private async Task<AgentToolResult> RunHelperAsync(Helper helper, CommandHookRunner? hooks)
    {
        var (model, rawStream) = _runtime();
        var stream = _store is null ? rawStream
            : new TaskBudget(_store, () => helper.Id, DefaultMaxTurns, DefaultMaxOutputTokens, DefaultMaxTurns * 16).Wrap(rawStream);
        IReadOnlyList<IAgentTool> tools = helper.Role.Writes
            ? [new ReadTool(_cwd, supportsImages: false) { WarningSink = _ => { } }, new BashTool(_cwd), new EditTool(_cwd), new WriteTool(_cwd),
               new PatchTool(_cwd), new GrepTool(_cwd), new FindTool(_cwd)]
            : [new ReadTool(_cwd, supportsImages: false) { WarningSink = _ => { } }, new BashTool(_cwd), new GrepTool(_cwd), new FindTool(_cwd)];
        if (_wrapTools is not null) tools = _wrapTools(tools);
        var context = new AgentContext { SystemPrompt = helper.Role.Instructions, Tools = [.. tools] };
        var config = new AgentLoopConfig
        {
            Model = model,
            ToolExecution = ToolExecutionMode.Sequential,
            ToolCallTimeout = TimeSpan.FromMinutes(20),
            MaxIdenticalToolCalls = 8,
            ToolResultTokenBudget = 4_000,
            // A read-only helper may run tests but never edits: the reviewer's read-only rules, the seat's guard otherwise.
            ToolGuard = helper.Role.Writes ? Guard : new ToolGuard([.. PiSharp.Core.Verification.RubricReviewer.ReadOnlyRules(), .. Guard?.Rules ?? []], GuardMode.Enforce),
            ActionReviewer = Reviewer,
            // The seat's hooks run inside its helpers too (PreToolUse, PostToolUse); their own start and stop are SubagentStart/Stop.
            CommandHooks = hooks,
            GetSteeringMessages = () =>
            {
                var messages = new List<Message>();
                while (helper.Inbox.TryDequeue(out var m)) messages.Add(m);
                return ValueTask.FromResult<IReadOnlyList<Message>>(messages);
            },
            ShouldStopAfterTurn = _ => ValueTask.FromResult(context.Messages.OfType<AssistantMessage>().Count() >= DefaultMaxTurns),
            RecordTurnUsage = (usage, _, _) =>
            {
                Interlocked.Add(ref helper.OutputTokens, usage.Output > 0 ? usage.Output : usage.TotalTokens);
                try { UsageRecorded?.Invoke(helper.Id, helper.Scope, usage); }
                catch (Exception ex) when (ex is not OutOfMemoryException) { /* Measurement never fails the helper. */ }
            },
        };
        AgentEnd? end = null;
        try
        {
            await foreach (var evt in AgentLoop.Run([new UserMessage("Task: " + helper.Task)], context, config, stream, helper.Stop.Token).ConfigureAwait(false))
                if (evt is AgentEnd e) end = e;
        }
        catch (OperationCanceledException) when (helper.Stop.IsCancellationRequested) { }
        var messages = end?.Messages ?? context.Messages;
        var summary = messages.OfType<AssistantMessage>().LastOrDefault()?.Text ?? "";
        var outcome = helper.Stop.IsCancellationRequested ? "closed" : end?.Outcome ?? "unknown";
        if (string.IsNullOrWhiteSpace(summary)) summary = $"(no summary: the helper stopped {(helper.Stop.IsCancellationRequested ? "when it was closed" : end?.Outcome ?? "early")} without a final answer)";
        var kept = ExploreTool.CapTokens(summary, DefaultFindingsTokens);
        var turns = messages.OfType<AssistantMessage>().Count();
        if (hooks is not null && hooks.Has(CommandHookEvent.SubagentStop))
        {
            try
            {
                var stop = await hooks.InvokeAsync(CommandHookEvent.SubagentStop, new JsonObject
                {
                    ["role"] = helper.Role.Name, ["id"] = helper.Id, ["outcome"] = outcome, ["summary"] = kept,
                }, CancellationToken.None).ConfigureAwait(false);
                if (stop.AdditionalContext is { Length: > 0 } added) kept += "\n[hook] " + added;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException) { }
        }
        return AgentToolResult.FromText($"Helper {helper.Id} ({helper.Role.Name}) finished ({outcome}, {turns} turns, {Volatile.Read(ref helper.OutputTokens)} output tokens). Summary:\n{kept}",
            new JsonObject { ["subRunId"] = helper.Id, ["role"] = helper.Role.Name, ["turns"] = turns, ["outcome"] = outcome });
    }

    [GeneratedRegex("```helper-role\\s*\\r?\\n(?<json>[\\s\\S]*?)\\r?\\n```", RegexOptions.CultureInvariant)]
    private static partial Regex FencedRole();

    /// <summary>Helper roles a librarian wrote in active KB rules, as fenced <c>helper-role</c> blocks:
    /// <c>{"name": "...", "instructions": "...", "writes": false}</c>. A built-in name is not overridden; a malformed block is skipped.</summary>
    public static IReadOnlyDictionary<string, Role> ParseRoles(IEnumerable<string> documents)
    {
        var roles = new Dictionary<string, Role>(StringComparer.Ordinal);
        foreach (var document in documents)
            foreach (Match match in FencedRole().Matches(document))
            {
                try
                {
                    if (JsonNode.Parse(match.Groups["json"].Value) is not JsonObject node
                        || node["name"]?.GetValue<string>()?.Trim() is not { Length: > 0 and <= 40 } name
                        || !name.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
                        || node["instructions"]?.GetValue<string>()?.Trim() is not { Length: > 0 } instructions
                        || Roles.ContainsKey(name)) continue;
                    roles[name] = new Role(name, instructions, node["writes"]?.GetValue<bool>() == true);
                }
                catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException) { }
            }
        return roles;
    }

    private static string Cut(string text, int max) => text.Length <= max ? text.ReplaceLineEndings(" ") : text[..max].ReplaceLineEndings(" ") + "...";

    public async ValueTask DisposeAsync()
    {
        Helper[] helpers;
        lock (_helpers)
        {
            if (_disposed) return;
            _disposed = true;
            helpers = [.. _helpers.Values];
        }
        _lifetime.Cancel();
        var ending = Task.WhenAll(helpers.Select(h => h.Run));
        try { await ending.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false); }
        catch (TimeoutException)
        {
            _ = ending.ContinueWith(t => _ = t.Exception, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
        catch (Exception) { /* Observe failed helpers while disposing every owned token. */ }
        foreach (var helper in helpers)
        {
            if (helper.Run.IsCompleted) helper.Stop.Dispose();
            else _ = helper.Run.ContinueWith(_ => helper.Stop.Dispose(), TaskScheduler.Default);
        }
        _lifetime.Dispose();
    }
}
