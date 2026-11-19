// Ported from pi: packages/agent/src/types.ts (AgentLoopConfig and hook contexts) @ 71dca871b
// Dropped by decision 3 (no approvals, fully automated): beforeToolCall, afterToolCall.
// Dropped as unneeded for one API: getApiKey (ModelConfig resolves the key).
using PiSharp.Core.Messages;
using PiSharp.Core.Models;
using PiSharp.Core.Guard;

namespace PiSharp.Core.Agent;

/// <summary>Continue runs the next model turn; Poll waits for new work; Stop ends the run.</summary>
public enum TurnDecision { Poll, Continue, Stop }

/// <summary>Passed to <see cref="AgentLoopConfig.ShouldStopAfterTurn"/> and <see cref="AgentLoopConfig.PrepareNextTurn"/>.</summary>
/// <param name="NewMessages">Messages this run has added so far (prompt included for Run, excluded for Continue).</param>
/// <param name="Final">The loop will not call the model again on its own: the turn made no tool calls, or its batch
/// ended the run (the loop guard, or every tool asked to terminate). Only a new message starts another turn.</param>
public sealed record TurnContext(
    AssistantMessage Message,
    IReadOnlyList<ToolResultMessage> ToolResults,
    AgentContext Context,
    IReadOnlyList<Message> NewMessages,
    bool Final = false);

/// <summary>Replacement state for the next turn. Null members keep the current value.
/// <paramref name="Trailing"/> is added to the context after the turn's steering messages, so it is the last
/// thing the model reads before answering (the open-tasks note after a compaction).</summary>
public sealed record TurnUpdate(
    AgentContext? Context = null,
    ModelConfig? Model = null,
    ThinkingLevel? ThinkingLevel = null,
    IReadOnlyList<Message>? Trailing = null);

public sealed record AgentLoopConfig
{
    public required ModelConfig Model { get; init; }
    /// <summary>Requested reasoning level. Null or Off sends no reasoning request.</summary>
    public ThinkingLevel? Reasoning { get; init; }
    public int? MaxTokens { get; init; }
    public double? Temperature { get; init; }
    /// <summary>Sent as <see cref="StreamOptions.CacheKey"/> on every request of the loop.</summary>
    public string? CacheKey { get; init; }

    /// <summary>Default: <see cref="ToolExecutionMode.Parallel"/>.</summary>
    public ToolExecutionMode ToolExecution { get; init; } = ToolExecutionMode.Parallel;

    /// <summary>Loop-enforced maximum tool runtime when a tool has no shorter MaxDuration. Default 15 minutes.</summary>
    public TimeSpan ToolCallTimeout { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>Optional deterministic guard evaluated after argument validation and before execution.</summary>
    public ToolGuard? ToolGuard { get; init; }

    /// <summary>See <see cref="StreamOptions.FreeformTools"/> (setting freeformApplyPatch).</summary>
    public IReadOnlySet<string>? FreeformTools { get; init; }

    /// <summary>See <see cref="StreamOptions.ServerCompactThreshold"/> (setting serverCompaction).</summary>
    public int? ServerCompactThreshold { get; init; }

    /// <summary>Decides the calls a guard rule marks Ask (Codex auto-review); without one, an Ask is refused.</summary>
    public IActionReviewer? ActionReviewer { get; init; }

    /// <summary>Optional external seat hooks. No hooks run unless explicitly configured.</summary>
    public CommandHookRunner? CommandHooks { get; init; }
    public string? HookSessionId { get; init; }
    public string? HookTaskKey { get; init; }
    public int MaxStopBlocks { get; init; } = 2;

    /// <summary>Passive, fail-safe observer for the model-requested tool-call batch.</summary>
    public Action<IReadOnlyList<ToolCallContent>>? RecordToolCalls { get; init; }

    /// <summary>
    /// Stop after this many consecutive identical tool-call batches (same names and arguments).
    /// 0 disables the guard. Default 3.
    /// </summary>
    public int MaxIdenticalToolCalls { get; init; } = 3;

    /// <summary>
    /// The same limit for a batch made only of input actions (<see cref="IAgentTool.InputAction"/>): the demo seat's
    /// desktop__act pressing one key three times was stopped as a loop, twice in one night, each time ending its task
    /// (2026-10-20; digital-scribe: "yes, fix the desktop loop guard in pi-sharp"). Default 8.
    /// </summary>
    public int MaxIdenticalInputCalls { get; init; } = 8;

    /// <summary>
    /// How many times one run may trip the identical-call guard and go on (the repeated calls refused, the refusal in
    /// context, one more model turn) before the guard ends the run. Default 2: the third trip ends it.
    /// </summary>
    public int LoopGuardRetries { get; init; } = 2;

    /// <summary>
    /// Execute at most this many tool calls from one assistant message; the rest get an error
    /// result instead. A guard against runaway output, not a budget. 0 disables it. Default 64.
    /// </summary>
    public int MaxToolCallsPerMessage { get; init; } = 64;

    /// <summary>
    /// Execute at most this many chat posts (tools named <c>*chat_post</c>) from one assistant
    /// message. 0 disables it. Default 10.
    /// </summary>
    public int MaxChatPostsPerMessage { get; init; } = 10;

    /// <summary>Maximum estimated tokens of text retained from each tool result. Default 10,000; 0 disables truncation.</summary>
    public int ToolResultTokenBudget { get; init; } = 10_000;

    /// <summary>Recover <c>&lt;tool_call&gt;</c> / JSON tool calls from assistant text. Default true.</summary>
    public bool RepairTextToolCalls { get; init; } = true;

    /// <summary>
    /// Maps the transcript to the messages the model receives. Custom message subclasses must be
    /// converted to user/assistant/toolResult or dropped. Default keeps only LLM messages.
    /// Must not throw.
    /// </summary>
    public Func<IReadOnlyList<Message>, ValueTask<IReadOnlyList<Message>>> ConvertToLlm { get; init; } = KeepLlmMessages;

    /// <summary>Runs before <see cref="ConvertToLlm"/>, e.g. for pruning. Must not throw.</summary>
    public Func<IReadOnlyList<Message>, CancellationToken, ValueTask<IReadOnlyList<Message>>>? TransformContext { get; init; }

    /// <summary>Polled after each turn; returned messages are injected before the next model call.</summary>
    public Func<ValueTask<IReadOnlyList<Message>>>? GetSteeringMessages { get; init; }

    /// <summary>pi-sharp-checkpoints-1: called once, immediately before a batch that contains a
    /// mutating tool (edit, write, apply_patch, bash, job) executes and before the first tool of the
    /// batch runs. Return a <see cref="PiSharp.Core.Session.CheckpointEntry"/> when a shadow snapshot
    /// was created so the caller records it on the session; null when the hook did nothing
    /// (disabled, unchanged, merge in progress, snapshot failed). Must never throw — a checkpoint
    /// must never fail the batch. Runs on the agent's token; on a timeout it returns null and the
    /// batch continues uncheckpointed.</summary>
    public Func<IReadOnlyList<ToolCallContent>, AgentContext, AgentLoopConfig, CancellationToken, ValueTask<PiSharp.Core.Session.CheckpointEntry?>>? BeforeMutatingBatch { get; init; }

    /// <summary>pi-sharp-checkpoints-3: called once after every tool batch has finished executing
    /// (results are already in the context). Return extra trailing messages to append after the
    /// batch's tool results (the last-green hint) or null when nothing is due. Must never throw —
    /// a hint must never fail the run.</summary>
    public Func<IReadOnlyList<ToolResultMessage>, AgentLoopConfig, CancellationToken, ValueTask<IReadOnlyList<Message>?>>? AfterToolBatch { get; init; }

    /// <summary>pi-sharp-checkpoints-7: called once when a turn ends successfully: StopReason is not
    /// Error/Aborted, there are no pending tool calls, and the stop hook did not block. The loop calls
    /// it before <see cref="ShouldStopAfterTurn"/>. Return the <see cref="PiSharp.Core.Session.CheckpointEntry"/>
    /// entries for the shadow snapshots created this turn (one per workspace root the before-batch hook
    /// resolved since the previous fire) so the caller records them on the session; null or empty when the
    /// hook created nothing (disabled, no roots remembered, every fingerprint unchanged, a merge in
    /// progress, a snapshot failed). Must never throw — a checkpoint must never fail the run. Absent
    /// (null) when <c>checkpoints.autoOnTurnEnd</c> is off; the before-batch hook is a separate flag and is
    /// not affected by this one.</summary>
    public Func<TurnContext, CancellationToken, ValueTask<IReadOnlyList<PiSharp.Core.Session.CheckpointEntry>?>>? AfterSuccessfulReply { get; init; }

    /// <summary>Polled when the agent would otherwise stop; returned messages start another turn.</summary>
    public Func<ValueTask<IReadOnlyList<Message>>>? GetFollowUpMessages { get; init; }

    /// <summary>Return true to end the run after the current turn without polling queues.</summary>
    public Func<TurnContext, ValueTask<bool>>? ShouldStopAfterTurn { get; init; }

    /// <summary>Explicit task-boundary decision. Takes precedence over the legacy stop/poll callback.</summary>
    public Func<TurnContext, ValueTask<TurnDecision>>? DecideTurn { get; init; }

    /// <summary>Bound for cancellation cleanup, including tools that ignore cancellation.</summary>
    public TimeSpan ToolCleanupTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Observes one finished assistant turn (measurement only; must not throw): the reported usage,
    /// the estimated context tokens of the request that produced the turn, and the model id. Wired
    /// by the seat to the cache-stats tracker (adr-pi-sharp-cache-stats).
    /// </summary>
    public Action<Usage, int, string?>? RecordTurnUsage { get; set; }

    /// <summary>Observes each guard decision on a call (measurement only; must not throw): the verdict and what came of it,
    /// "blocked", "unreviewed" (an Ask with no reviewer), "allowed", "denied", "denied, turn stopped" or "no answer". Wired
    /// by the seat to its telemetry (plan-pi-sharp-codex item 20).</summary>
    public Action<ToolCallContent, PiSharp.Core.Guard.GuardVerdict, string>? RecordGuard { get; init; }

    /// <summary>Called right before the next turn starts; may replace context, model or thinking level.</summary>
    public Func<TurnContext, ValueTask<TurnUpdate?>>? PrepareNextTurn { get; init; }

    public static ValueTask<IReadOnlyList<Message>> KeepLlmMessages(IReadOnlyList<Message> messages) =>
        ValueTask.FromResult<IReadOnlyList<Message>>(messages.Where(m => m.IsLlmMessage).ToList());
}
