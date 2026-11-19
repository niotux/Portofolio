// pi-sharp-hh-11: the explore tool. Runs a read-only sub-run (a nested AgentLoop with read,
// grep and find only) so a question like "where is X handled?" is answered in its own context
// instead of filling the main context with tool output that compaction later summarises lossily.
// Caps: at most MaxTurns turns and MaxOutputTokens output tokens, charged to a TaskBudget row
// (durable per sub-run id, so a restart cannot earn fresh turns); at most one sub-run at a time
// per seat (memory); at most FindingsTokens tokens of findings returned to the main context.
using System.Text.Json.Nodes;
using PiSharp.Core.Agent;
using PiSharp.Core.Messages;
using PiSharp.Core.Models;
using PiSharp.Core.Notebook;
using PiSharp.Core.Session;

namespace PiSharp.Tools;

/// <summary>One read-only exploration sub-run with its own context and budget.</summary>
public sealed class ExploreTool : IAgentTool
{
    public const int DefaultMaxTurns = 10;
    public const int DefaultMaxOutputTokens = 30_000;
    public const int DefaultFindingsTokens = 2_000;
    public static readonly TimeSpan DefaultMaxDuration = TimeSpan.FromMinutes(5);

    private static readonly JsonObject ArgsSchema = System.Text.Json.Nodes.JsonNode.Parse("""
        {
          "type": "object",
          "properties": {
            "task": { "type": "string", "description": "What to find out: the question to answer with read/grep/find, e.g. 'where are tool call deadlines enforced and which config bounds them?'" }
          },
          "required": ["task"]
        }
        """)!.AsObject();

    private static readonly string Describe =
        "Explore the codebase in a read-only sub-run with its own context. It runs the read, grep and find tools " +
        "inside a nested agent loop capped at " + DefaultMaxTurns + " turns and " + DefaultMaxOutputTokens / 1000 + "k output tokens " +
        "(charged to the task budget) and returns at most " + DefaultFindingsTokens + " tokens of findings; one sub-run at a time per seat. " +
        "Use it for codebase questions ('where is X handled', 'which files implement Y') instead of many read/grep calls " +
        "in the main conversation: the exploration output stays in the sub-run's context and only the findings enter yours. " +
        "It cannot edit, write, run commands or use any other tool.";

    private readonly string _cwd;
    private readonly Func<(ModelConfig Model, StreamFunction Stream)> _runtime;
    private readonly Func<string> _taskKey;
    private readonly NotebookStore? _store;
    private readonly Action<AgentEvent>? _observer;
    private readonly int _maxTurns;
    private readonly int _maxOutputTokens;
    private readonly int _findingsTokens;
    private int _active;

    /// <param name="cwd">Working directory the read-only tools resolve paths against.</param>
    /// <param name="runtime">The seat's current model and stream function, read per call so model switches apply.</param>
    /// <param name="taskKey">The task charged for the budget row; the sub-run gets its own row id.</param>
    /// <param name="store">The notebook store the budget row lives in; null disables durable charging (in-memory cap only).</param>
    /// <param name="observer">Receives the sub-run's AgentEvents for the tracer (null = no tracing).</param>
    public ExploreTool(
        string cwd,
        Func<(ModelConfig Model, StreamFunction Stream)> runtime,
        Func<string> taskKey,
        NotebookStore? store = null,
        Action<AgentEvent>? observer = null,
        int maxTurns = DefaultMaxTurns,
        int maxOutputTokens = DefaultMaxOutputTokens,
        int findingsTokens = DefaultFindingsTokens)
    {
        _cwd = cwd;
        _runtime = runtime;
        _taskKey = taskKey;
        _store = store;
        _observer = observer;
        _maxTurns = Math.Max(1, maxTurns);
        _maxOutputTokens = Math.Max(1, maxOutputTokens);
        _findingsTokens = Math.Max(1, findingsTokens);
    }

    public const string ToolName = "explore";
    public string Name => ToolName;
    public string Label => ToolName;
    public string Description => Describe;
    public JsonObject Parameters => ArgsSchema;
    public ToolExecutionMode? ExecutionMode => ToolExecutionMode.Sequential;
    /// <summary>The sub-run's own deadline: the loop's ToolCallTimeout is also applied by the parent.</summary>
    public TimeSpan? MaxDuration => DefaultMaxDuration;

    /// <summary>True while a sub-run of this seat is executing.</summary>
    public bool IsRunning => Volatile.Read(ref _active) > 0;

    public async Task<AgentToolResult> ExecuteAsync(
        string toolCallId,
        JsonObject arguments,
        Action<AgentToolResult>? onUpdate,
        CancellationToken cancellationToken)
    {
        var task = arguments["task"]?.GetValue<string>()
            ?? throw new ToolArgumentException("task required: what to find out");
        if (Interlocked.Exchange(ref _active, 1) == 1)
            return AgentToolResult.Error(
                "explore: another sub-run is already running on this seat; wait for it to finish (max " +
                ((int)(MaxDuration!.Value.TotalMinutes)) + " minutes) before exploring again.");
        try
        {
            var id = "explore-" + Guid.NewGuid().ToString("N");
            var (model, rawStream) = _runtime();
            var stream = _store is null
                ? rawStream
                : new TaskBudget(_store, () => id, _maxTurns, _maxOutputTokens, _maxTurns * 16).Wrap(rawStream);

            var context = new AgentContext
            {
                SystemPrompt = SubRunPrompt(_maxTurns),
                Tools = [new ReadTool(_cwd, supportsImages: false) { WarningSink = _ => { } }, new GrepTool(_cwd), new FindTool(_cwd)],
            };
            var config = new AgentLoopConfig
            {
                Model = model,
                ToolExecution = ToolExecutionMode.Sequential,
                ToolCallTimeout = TimeSpan.FromMinutes(2),
                MaxIdenticalToolCalls = 8,
                // The sub-run's raw tool output is disposable: keep it small so the sub-run's own context
                // stays light even without the seat's artifact budgeting.
                ToolResultTokenBudget = 3_000,
                ShouldStopAfterTurn = _ => ValueTask.FromResult(StopAfterTurn(context)),
                RecordTurnUsage = (usage, _, _) => _outputTokens += usage.Output > 0 ? usage.Output : usage.TotalTokens,
            };

            var events = AgentLoop.Run([new UserMessage(SubRunTaskInstruction(task))], context, config, stream, cancellationToken);
            AgentEnd? end = null;
            var charged = 0;
            await foreach (var evt in events.ConfigureAwait(false))
            {
                try { _observer?.Invoke(evt); } catch { /* tracing must never fail the run */ }
                if (evt is AgentEnd e) end = e;
                if (evt is MessageEnd { Message: AssistantMessage assistant } && end is null && assistant.Usage.Output > 0)
                    charged += assistant.Usage.Output;
            }

            var messages = end?.Messages ?? context.Messages;
            var lastAssistant = messages.OfType<AssistantMessage>().LastOrDefault();
            var findings = lastAssistant?.Text ?? "";
            var stoppedForBudget = lastAssistant?.ErrorCode == "budget_exhausted";
            if (string.IsNullOrWhiteSpace(findings))
                findings = "(no findings: the sub-run stopped " + (end?.Outcome ?? "early") + " without a final answer)";

            var kept = CapTokens(findings, _findingsTokens);
            var details = new JsonObject
            {
                ["subRunId"] = id,
                ["turns"] = messages.OfType<AssistantMessage>().Count(),
                ["outputTokens"] = Math.Max(charged, Volatile.Read(ref _outputTokens)),
                ["maxTurns"] = _maxTurns,
                ["maxOutputTokens"] = _maxOutputTokens,
                ["findingsTokens"] = TokenEstimate.EstimateTextTokens(kept),
                ["outcome"] = end?.Outcome ?? "unknown",
            };
            if (stoppedForBudget) details["budgetExhausted"] = true;
            var header = $"Explore sub-run {id} finished ({end?.Outcome ?? "unknown"}, " +
                details["turns"] + " turns, " + details["outputTokens"] + " output tokens charged to the task budget). Findings:" +
                (TokenEstimate.EstimateTextTokens(findings) > _findingsTokens ? " (truncated to " + _findingsTokens + " tokens)" : "");
            return AgentToolResult.FromText(header + "\n" + kept, details);
        }
        finally
        {
            Volatile.Write(ref _outputTokens, 0);
            Interlocked.Exchange(ref _active, 0);
        }
    }

    private int _outputTokens;
    private bool StopAfterTurn(AgentContext context) =>
        context.Messages.OfType<AssistantMessage>().Count() >= _maxTurns;

    internal static string CapTokens(string text, int maxTokens)
    {
        if (TokenEstimate.EstimateTextTokens(text) <= maxTokens) return text;
        var chars = Math.Max(0, maxTokens * TokenEstimate.CharsPerToken - 40);
        var kept = text.Length <= chars ? text : text[..chars];
        var keptTokens = TokenEstimate.EstimateTextTokens(kept);
        return kept + $"\n[... {Math.Max(0, TokenEstimate.EstimateTextTokens(text) - keptTokens)} more tokens omitted from the sub-run's findings]";
    }

    private static string SubRunPrompt(int maxTurns) =>
        "You are a read-only codebase explorer running inside a seat's explore tool. " +
        "Answer the task with the read, grep and find tools only. There are no write, edit or command tools: " +
        "you cannot and must not try to change anything. Keep each read/grep/find call small (line ranges, " +
        "bounded output). When done, your final message must be exactly your findings (no prose before or " +
        "after them): what you found, with file paths and line numbers, in as few lines as possible. " +
        "bounded patterns, file filters). Finish with a single findings message: the direct answer, the file " +
        "paths and line numbers that prove it, and anything you did not verify. Do not restate file contents " +
        "in bulk; cite what matters. The sub-run is capped at " + maxTurns + " turns.";

    private static string SubRunTaskInstruction(string task) =>
        "Task: " + task +
        "\nUse read/grep/find on the working tree. Finish with the findings: answer, evidence (paths and line numbers), gaps.";
}
