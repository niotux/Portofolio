// Glue between AgentLoop and SessionManager. Compacts before any turn that would cross the threshold,
// and on overflow compacts and retries the turn once.
// Overflow detection is injected so Core does not depend on PiSharp.Llm.
using PiSharp.Core.Agent;
using PiSharp.Core.Compaction;
using PiSharp.Core.Messages;
using PiSharp.Core.Models;

namespace PiSharp.Core.Session;

/// <summary>Thrown when the prompt did not fit and compaction could not recover.</summary>
public sealed class ContextOverflowException : Exception
{
    public AssistantMessage Assistant { get; }

    public ContextOverflowException(AssistantMessage assistant, string? message = null)
        : base(message ?? assistant.ErrorMessage ?? "Context overflow")
    {
        Assistant = assistant;
    }
}

/// <summary>
/// Persists turns to a <see cref="SessionManager"/>, rebuilds LLM context from the latest
/// compaction, and on overflow compact-then-retries the turn once.
/// </summary>
public sealed class AgentSession
{
    public SessionManager Session { get; }
    public AgentContext Context { get; }
    public CompactionSettings CompactionSettings { get; set; }
    public Func<AssistantMessage, int?, bool> IsContextOverflow { get; set; }

    /// <summary>Receives one line when compaction between turns ran, was skipped, or failed. The run continues.</summary>
    public Action<string>? Notice { get; set; }

    /// <summary>Attached-mode additions around an automatic compaction (between turns, before a run, on
    /// overflow): the save-before-compact turn, the carry-forward note (open inbox tasks, live facts) and the
    /// actor for the compaction log. Null (plain CLI) = summary only.</summary>
    public CompactionHooks? Hooks { get; set; }
    /// <summary>What a PostCompact hook added, carried with the next note after the compaction.</summary>
    private string? _postCompactContext;
    public Func<string>? NotebookState { get; set; }
    public Func<int>? NotebookTokens { get; set; }
    public Func<IReadOnlyList<Message>, IReadOnlyList<Message>>? WorkingMessages { get; set; }
    private ModelConfig? _requestModel;

    /// <summary>Tokens the save-before-compact turn may need on top of the current context. When the context
    /// plus this does not fit the window, the save turn is skipped and the compaction runs at once.</summary>
    public const int SaveTurnTokens = 4096;

    // Set when the save-before-compact prompt went out: the turn that answers it is not a task end, and the next
    // PrepareNextTurn compacts. _continueAfterSave: that turn made no tool calls, so the loop needs one steering
    // message to go on instead of treating "saved" as the task's final answer.
    private bool _saveTurnAsked;
    private bool _continueAfterSave;

    // Set when the save turn made tool calls (the notebook write the prompt asks for): the compaction ran right
    // after those calls, so the model's "saved" comes on the far side of it, when _saveTurnAsked is already
    // clear. Cleared by the first turn without tool calls; if that turn is only the save acknowledgement, the
    // loop continues instead of stopping. Without this, 53 of 509 seat compactions on 2026-09-27..29 ended the
    // seat's task on the word "saved" (pi4 dropped pi-sharp-codeintel-mcp-3 mid-debug at 21:48Z on 09-29).
    private bool _saveReplyPending;

    // MessageEnd events of the current loop that this session has already persisted. The loop runs on
    // its own task and hands events over a channel, so when PrepareNextTurn fires the session can lag
    // behind the loop, and compacting from a lagging session would rebuild the context without the
    // newest turn.
    private int _messageEndsPersisted;
    private CommandHookRunner? _commandHooks;
    private string? _hookTaskKey;

    // After a compaction that could not get under the threshold, do not summarise again every turn.
    private int _noCompactionBelowTokens;

    // Index into Session.Entries where the live context begins: 0, or the first entry of the task a
    // replacement context started. Compaction never reaches behind it.
    private int _contextStart;

    /// <summary>
    /// Upper bound on compaction-and-retry rounds after a context overflow (wi-8). The design
    /// ruling caps the retry at one: a retry that itself overflows means compaction failed, not a
    /// different failure worth chasing. One compaction-and-retry round, and the round must shrink
    /// the context, never an open-ended loop that re-sends an oversized prompt.
    /// </summary>
    public const int MaxOverflowRetries = 1;

    public AgentSession(
        SessionManager session,
        AgentContext context,
        CompactionSettings? compactionSettings = null,
        Func<AssistantMessage, int?, bool>? isContextOverflow = null)
    {
        Session = session;
        Context = context;
        CompactionSettings = compactionSettings ?? CompactionSettings.Default;
        IsContextOverflow = isContextOverflow ?? ((_, _) => false);
        if (Context.Messages.Count == 0)
            Context.Messages.AddRange(session.BuildContextMessages());
    }

    public void SyncContextFromSession()
    {
        Context.Messages.Clear();
        Context.Messages.AddRange(Session.BuildContextMessages());
    }

    public async Task<CompactionResult?> CompactAsync(
        ModelConfig model,
        StreamFunction stream,
        StreamOptions? options = null,
        string? customInstructions = null,
        CancellationToken cancellationToken = default)
    {
        // Only what the live context holds is summarised: entries before _contextStart belong to a task the
        // loop has already dropped, and summarising them would bring them back.
        var entries = _contextStart == 0 ? Session.Entries : Session.Entries.Skip(_contextStart).ToList();
        var settings = CompactionSettings with { KeepRecentTokens = CompactionSettings.KeepRecentFor(FixedTokens(), model) };
        var preparation = Compactor.Prepare(entries, settings);
        if (preparation is null) return null;
        if (_commandHooks is not null)
        {
            var decision = await _commandHooks.InvokeAsync(CommandHookEvent.PreCompact, new System.Text.Json.Nodes.JsonObject
            {
                ["session_id"] = Session.SessionId,
                ["task_key"] = _hookTaskKey,
                ["tokens_before"] = preparation.TokensBefore,
            }, cancellationToken).ConfigureAwait(false);
            if (decision.Block)
            {
                Notice?.Invoke("pre-compact hook blocked compaction: " + (decision.Reason ?? decision.AdditionalContext ?? "no reason"));
                return null;
            }
        }
        var notebookBefore = NotebookState?.Invoke();
        var boundary = Session.LeafId;
        var result = await CompactInPlaceAsync(preparation, model, stream, options, customInstructions, cancellationToken).ConfigureAwait(false)
            ?? await Compactor.CompactAsync(preparation, model, stream, options, customInstructions, cancellationToken).ConfigureAwait(false);
        if (notebookBefore != NotebookState?.Invoke() || boundary != Session.LeafId)
            throw new InvalidOperationException("Compaction state changed during summarization; checkpoint rejected, original context retained.");
        var details = (result.Details ?? new CompactionDetails([], [])) with { NotebookState = notebookBefore, SourceBoundary = boundary };
        Session.AppendCompaction(result.Summary, result.FirstKeptEntryId, result.TokensBefore, details, result.Usage);
        SyncContextFromSession();
        _lastRequest = null;
        if (_commandHooks is not null && _commandHooks.Has(CommandHookEvent.PostCompact))
        {
            // PostCompact (Codex): a hook's added context goes with the note the seat carries after every compaction.
            var after = await _commandHooks.InvokeAsync(CommandHookEvent.PostCompact, new System.Text.Json.Nodes.JsonObject
            {
                ["session_id"] = Session.SessionId,
                ["task_key"] = _hookTaskKey,
                ["tokens_before"] = result.TokensBefore,
                ["summary"] = result.Summary,
            }, cancellationToken).ConfigureAwait(false);
            _postCompactContext = after.AdditionalContext;
        }
        return result;
    }

    /// <summary>The summary asked on the seat's own last request (<see cref="Compactor.CompactInPlaceAsync"/>);
    /// null when there is no usable last request or the answer was not a summary, so the transcript path runs.</summary>
    private async Task<CompactionResult?> CompactInPlaceAsync(
        CompactionPreparation preparation, ModelConfig model, StreamFunction stream, StreamOptions? options,
        string? customInstructions, CancellationToken cancellationToken)
    {
        if (_lastRequest is not { } last || last.Model.Id != model.Id) return null;
        // The instruction and the summary must fit beside the whole last request.
        var room = model.ContextWindow - TokenEstimate.EstimateContextTokens(last.Context).Tokens - 2048;
        if (room < preparation.Settings.SummaryAllowance) return null;
        try
        {
            return await Compactor.CompactInPlaceAsync(preparation, last.Context, model, stream,
                (options ?? new StreamOptions()) with
                {
                    CacheKey = options?.CacheKey ?? last.Options.CacheKey,
                    Reasoning = options?.Reasoning ?? last.Options.Reasoning,
                    Temperature = options?.Temperature ?? last.Options.Temperature,
                },
                customInstructions, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            Notice?.Invoke("in-place summary failed, summarising the transcript instead: " + ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Run a prompt. Persists user/assistant/tool messages. If the model reports overflow,
    /// compact and retry the turn once.
    /// </summary>
    public IAsyncEnumerable<AgentEvent> Run(
        IReadOnlyList<Message> prompts,
        AgentLoopConfig config,
        StreamFunction stream,
        CancellationToken cancellationToken = default)
    {
        return RunCore((hooked, recording) => AgentLoop.Run(prompts, Context, hooked, recording, cancellationToken),
            config, stream, cancellationToken);
    }

    public IAsyncEnumerable<AgentEvent> Continue(
        AgentLoopConfig config,
        StreamFunction stream,
        CancellationToken cancellationToken = default)
    {
        return RunCore((hooked, recording) => AgentLoop.Continue(Context, hooked, recording, cancellationToken),
            config, stream, cancellationToken);
    }

    private AgentLoopConfig WithSessionHooks(AgentLoopConfig config, StreamFunction stream, CancellationToken cancellationToken)
    {
        var inner = config.ConvertToLlm;
        var prepare = config.PrepareNextTurn;
        var checkpoint = config.BeforeMutatingBatch;
        var afterReply = config.AfterSuccessfulReply;
        var hooked = config with
        {
            ConvertToLlm = messages => inner(SessionConvert.ToLlm(messages)),
            // pi-sharp-checkpoints-1: a snapshot taken by the factory's BeforeMutatingBatch lands on
            // this session's journal, so the run's checkpoints are the session's, wherever the hook came from.
            BeforeMutatingBatch = checkpoint is null ? null
                : async (calls, context, loopConfig, token) =>
                {
                    var entry = await checkpoint(calls, context, loopConfig, token).ConfigureAwait(false);
                    if (entry is not null)
                    {
                        Session.AppendCheckpoint(entry);
                        await ValueTask.CompletedTask;
                    }
                    return entry;
                },
            // pi-sharp-checkpoints-7: the after-reply snapshots (one per remembered root) land on this
            // session's journal too, the same way BeforeMutatingBatch does.
            AfterSuccessfulReply = afterReply is null ? null
                : async (turn, token) =>
                {
                    var entries = await afterReply(turn, token).ConfigureAwait(false);
                    if (entries is not null)
                    {
                        foreach (var entry in entries)
                        {
                            Session.AppendCheckpoint(entry);
                            await ValueTask.CompletedTask;
                        }
                    }
                    return entries;
                },
            // A headless run is one prompt followed by many tool turns. Checking the threshold only when
            // a prompt starts meant such a run never compacted and died on the server overflow instead.
            PrepareNextTurn = async turn =>
            {
                if (_saveTurnAsked)
                {
                    // The turn that just ended answered the save-before-compact prompt. It is not a task end, so
                    // the inner hook (the factory's task-boundary reset) is not asked; the compaction runs now.
                    _saveTurnAsked = false;
                    _saveReplyPending = turn.Message.ToolCalls.Any();
                    // The model the loop runs now, not the one the run started on: an assignment switched in place
                    // between turns (0.5.63) left config.Model stale, and the broker refused every summary with 409
                    // while the turns themselves went through (agent-maker-2, 2026-10-02).
                    return await CompactBetweenTurnsAsync(turn, CurrentModel(config), stream, null, saved: true, cancellationToken).ConfigureAwait(false);
                }

                var update = prepare is null ? null : await prepare(turn).ConfigureAwait(false);
                if (turn.Message?.Content.Any(PiSharp.Core.Compaction.ServerCompaction.IsItem) == true)
                {
                    // The provider compacted inside that turn: the seat's after-compaction note follows, as after its own.
                    Notice?.Invoke($"the provider compacted the context; ~{ContextTokens()} tokens from its compaction item on");
                    if (CarryForwardMessage() is { } carried) return WithTrailing(update, carried);
                }
                if (update?.Context is { } fresh && !ReferenceEquals(fresh, Context))
                {
                    // A replacement context (FactorySteering starts every inbox task from an empty one) is
                    // adopted into Context, so the loop keeps sending the object this session measures and
                    // compacts. Letting the loop run on the replacement left Context frozen at the first task:
                    // the architect seat grew to 167k tokens in one task and never compacted (2026-09-18).
                    AdoptContext(fresh, turn);
                    return update with { Context = Context };
                }

                var model = update?.Model ?? CurrentModel(config);
                if (update?.Model is { } switched) _requestModel = switched;
                if (CompactionDue(model) is not int tokens) return update;
                if (DateTime.UtcNow < _compactionRetryAfter) return update;
                if (Hooks?.SavePrompt is { Length: > 0 } save && !_savedSinceFailure)
                {
                    if (tokens + SaveTurnTokens <= model.ContextWindow)
                    {
                        // One turn to write partial work down before the summary replaces it.
                        _saveTurnAsked = true;
                        Notice?.Invoke($"compaction due at ~{tokens} tokens: one save-before-compact turn first");
                        return WithTrailing(update, new UserMessage(save));
                    }
                    Notice?.Invoke($"save-before-compact skipped at ~{tokens} tokens: the save turn would not fit the context window");
                }
                return await CompactBetweenTurnsAsync(turn, model, stream, update, saved: false, cancellationToken).ConfigureAwait(false);
            },
        };
        if (Hooks?.SavePrompt is not { Length: > 0 }) return hooked;

        var stop = config.ShouldStopAfterTurn;
        var decide = config.DecideTurn;
        var steer = config.GetSteeringMessages;
        return hooked with
        {
            DecideTurn = async turn =>
            {
                // A save turn that ended without tool calls ("saved") is not the task's answer: the inner hook
                // would post it as the final text. With tool calls the inner hook only records them.
                if (_saveTurnAsked && !turn.Message.ToolCalls.Any())
                {
                    _continueAfterSave = true;
                    return TurnDecision.Continue;
                }
                if (_saveReplyPending && !turn.Message.ToolCalls.Any())
                {
                    _saveReplyPending = false;
                    if (IsSaveAcknowledgement(turn.Message.Text))
                    {
                        _continueAfterSave = true;
                        return TurnDecision.Continue;
                    }
                }
                return decide is not null ? await decide(turn).ConfigureAwait(false)
                    : stop is not null && await stop(turn).ConfigureAwait(false) ? TurnDecision.Stop : TurnDecision.Poll;
            },
            GetSteeringMessages = async () =>
            {
                IReadOnlyList<Message> messages = steer is null ? [] : await steer().ConfigureAwait(false);
                if (!_continueAfterSave) return messages;
                _continueAfterSave = false;
                return [.. messages, new UserMessage(CompactionHooks.ContinueAfterSave)];
            },
        };
    }

    /// <summary>True for a reply that only acknowledges the save prompt ("saved", "Notes saved.", "**Saved**"),
    /// never for a real answer: short, and the word "saved" is in it.</summary>
    public static bool IsSaveAcknowledgement(string text)
    {
        var bare = new string(text.Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c)).ToArray()).Trim();
        return bare.Length <= 32
            && bare.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Any(w => w.Equals("saved", StringComparison.OrdinalIgnoreCase));
    }

    private static TurnUpdate WithTrailing(TurnUpdate? update, Message message) =>
        (update ?? new TurnUpdate()) with { Trailing = [.. update?.Trailing ?? [], message] };

    /// <summary>The context's token estimate when an automatic compaction is due now, else null.</summary>
    /// <remarks>
    /// Two triggers, checked in order:
    /// <list type="number">
    ///   <item>The original reactive gate: the estimate has crossed <c>window − reserve</c>
    ///   (<see cref="Compactor.ShouldCompact"/>) and is above <c>_noCompactionBelowTokens</c>.</item>
    ///   <item>The proactive gate (wi-59): after a compaction, <c>_noCompactionBelowTokens</c> is
    ///   raised to <c>after + KeepRecentTokens/2</c>.  Once the estimate reaches that floor — i.e. the
    ///   next turn would land at or past the post-compaction target — compact again *before* the
    ///   context gets anywhere near <c>window − reserve</c>.  This fires on a turn that would have
    ///   grown the context above the wall, so the refusal path (exit 4) is only a backstop.</item>
    /// </list>
    /// Both paths run from <c>PrepareNextTurn</c>, which is polled once per turn, so there is at most
    /// one compaction per turn; <c>_noCompactionBelowTokens</c> is raised after each compaction so
    /// the same turn cannot fire again.
    /// </remarks>
    private int? CompactionDue(ModelConfig model)
    {
        if (!CompactionSettings.Enabled) return null;
        var tokens = ContextTokens();

        // Reactive gate: original behaviour, unchanged.
        if (tokens > model.ContextWindow - CompactionSettings.ReserveFor(model)
            || (Compactor.ShouldCompact(tokens, model, CompactionSettings) && tokens >= _noCompactionBelowTokens))
            return tokens;

        // Proactive gate (wi-59): if a post-compaction floor has been set and the estimate has
        // reached it, compact early — before the context approaches window − reserve.
        // The floor is always above 0 (set to after + KeepRecentTokens/2 after a compaction),
        // and we require the estimate to be at or above it.  When no compaction has yet run
        // in this session, _noCompactionBelowTokens is 0 and this branch is never entered.
        if (CompactionSettings.TriggerTokens is null && _noCompactionBelowTokens > 0 && tokens >= _noCompactionBelowTokens)
            return tokens;

        return null;
    }

    private void AdoptContext(AgentContext fresh, TurnContext turn)
    {
        Context.SystemPrompt = fresh.SystemPrompt;
        Context.Tools = fresh.Tools;
        Context.Messages = [.. fresh.Messages];
        _lastRequest = null;
        // Messages the loop has ended but this session has not persisted yet still belong to the old task.
        var pending = Math.Max(0, turn.NewMessages.Count - Volatile.Read(ref _messageEndsPersisted));
        _contextStart = Session.Entries.Count + pending;
    }

    /// <summary>The <see cref="CompactionHooks.CarryForward"/> note as a user message; null when there is none or
    /// it failed. A failure never fails the compaction.</summary>
    private UserMessage? CarryForwardMessage()
    {
        try
        {
            var text = Hooks?.CarryForward?.Invoke();
            if (Interlocked.Exchange(ref _postCompactContext, null) is { Length: > 0 } hooked)
                text = string.IsNullOrWhiteSpace(text) ? "[hook] " + hooked : text + "\n\n[hook] " + hooked;
            return string.IsNullOrWhiteSpace(text) ? null : new UserMessage(text);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Notice?.Invoke("carry-forward note after compaction failed: " + ex.Message);
            return null;
        }
    }

    /// <summary>After a compaction outside a turn (before a run, before the overflow retry): logs it and appends
    /// the carry-forward note straight to the context and the session.</summary>
    private void AfterCompactionOutsideTurn(string trigger, int tokensBefore)
    {
        LogCompaction(trigger, tokensBefore, saved: false);
        if (CarryForwardMessage() is not { } note) return;
        Context.Messages.Add(note);
        Session.AppendMessage(note);
    }

    private int ContextTokens()
    {
        var messages = WorkingMessages?.Invoke(Context.Messages) ?? Context.Messages;
        var model = _requestModel ?? _lastRequest?.Model;
        var opaque = ServerCompaction.LatestIndex(messages) >= 0;
        if (model is not null) messages = ServerCompaction.FromLatest(messages, model.Id, model.Provider);
        return TokenEstimate.EstimateContextTokens(new LlmContext(Context.SystemPrompt, messages,
            model is null ? Context.Tools : ToolCatalog.Present(Context.Tools, model, Context.Messages)), opaque ? model?.Id : null, opaque ? model?.Provider : null).Tokens + (NotebookTokens?.Invoke() ?? 0);
    }

    /// <summary>pi-sharp-hh-6: the same context estimate the compaction gate measures, exposed so the
    /// context_remaining tool reports the number the seat's context actually stands at.</summary>
    public int EstimatedContextTokens() => ContextTokens();

    /// <summary>What every request carries whatever the transcript holds: system prompt, tools and notebook.</summary>
    private int FixedTokens() =>
        TokenEstimate.EstimateContextTokens(new LlmContext(Context.SystemPrompt, [],
            _requestModel is null ? Context.Tools : ToolCatalog.Present(Context.Tools, _requestModel, Context.Messages))).Tokens + (NotebookTokens?.Invoke() ?? 0);

    /// <summary>Told each logged compaction (the seat's telemetry, plan-pi-sharp-codex item 20); must not throw.</summary>
    public Action<CompactionLogEntry>? Compacted { get; set; }

    /// <summary>One line on <see cref="Notice"/> and one <see cref="CompactionLogEntry"/> in the session per
    /// automatic compaction, so ops can line a dropped task up with the compaction before it.</summary>
    private void LogCompaction(string trigger, int tokensBefore, bool saved)
    {
        // Attached seats only: a plain CLI session file and stderr stay as they were.
        if (Hooks is null) return;
        var tokensAfter = ContextTokens();
        int? open = null;
        try
        {
            open = Hooks?.OpenTaskCount?.Invoke();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // an unreadable count is logged as unknown
        }
        var save = Hooks?.SavePrompt is not { Length: > 0 } ? "off" : saved ? "ran" : "skipped";
        var entry = new CompactionLogEntry
        {
            Id = "",
            Actor = Hooks?.Actor,
            Trigger = trigger,
            TokensBefore = tokensBefore,
            TokensAfter = tokensAfter,
            OpenTasks = open,
            SaveBeforeCompact = save,
        };
        Session.AppendCompactionLog(entry);
        try { Compacted?.Invoke(entry); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { Notice?.Invoke("compaction telemetry failed: " + ex.Message); }
        Notice?.Invoke(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"compaction log: actor={Hooks?.Actor ?? "-"} at={DateTimeOffset.UtcNow:yyyy-MM-ddTHH:mm:ssZ} trigger={trigger} tokens={tokensBefore}->{tokensAfter} open_tasks={(open is int n ? n.ToString(System.Globalization.CultureInfo.InvariantCulture) : "-")} save_before_compact={save}"));
        try
        {
            Hooks?.OnCompactionOccurred?.Invoke(Hooks.CarryForward?.Invoke());
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Notice?.Invoke("compaction occurred hook failed: " + ex.Message);
        }
    }

    /// <summary>Compacts between turns (the caller decided it is due) and returns the update for the next turn:
    /// <paramref name="update"/> plus the carry-forward note as a trailing message. Steering items polled before
    /// this hook are added after it returns, and the note must come after them.</summary>
    private async Task<TurnUpdate?> CompactBetweenTurnsAsync(
        TurnContext turn, ModelConfig model, StreamFunction stream, TurnUpdate? update, bool saved, CancellationToken cancellationToken)
    {
        var tokens = ContextTokens();

        // Every message in NewMessages had its MessageEnd emitted before it was added, so the session
        // has caught up once it has persisted that many.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (Volatile.Read(ref _messageEndsPersisted) < turn.NewMessages.Count)
        {
            if (DateTime.UtcNow > deadline)
            {
                Notice?.Invoke($"compaction skipped at ~{tokens} tokens: the session did not catch up with the loop within 30s");
                return update;
            }
            await Task.Delay(10, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            var result = await CompactAsync(model, stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (result is null)
            {
                Notice?.Invoke($"compaction skipped at ~{tokens} tokens: nothing to compact");
                _noCompactionBelowTokens = tokens + CompactionSettings.KeepRecentFor(FixedTokens(), model) / 2;
                return update;
            }
            var after = ContextTokens();
            Notice?.Invoke($"compacted between turns: ~{tokens} -> ~{after} tokens");
            // Always set the floor after a compaction (wi-59): the proactive gate fires when the
            // estimate reaches this floor, which is below the reactive threshold, so a turn that
            // would have grown the context toward the wall is compacted early.
            _noCompactionBelowTokens = after + CompactionSettings.KeepRecentFor(FixedTokens(), model) / 2;
            _savedSinceFailure = false;
            LogCompaction("between-turns", tokens, saved);
            return CarryForwardMessage() is { } note ? WithTrailing(update, note) : update;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // The overflow path is still the backstop; losing the whole run here would be worse.
            // Back off: retrying on the next turn asked a fresh save turn and failed again, every turn, while the
            // context grew to the wall (agent-maker-2, 2026-10-02: 82 cycles). The save already happened, so the
            // retry compacts directly.
            _compactionRetryAfter = DateTime.UtcNow + CompactionRetryDelay;
            _savedSinceFailure |= saved;
            Notice?.Invoke($"compaction between turns failed at ~{tokens} tokens: {ex.Message}; next try after {CompactionRetryDelay.TotalSeconds:0}s");
            try { Hooks?.OnCompactionFailed?.Invoke(ex.Message); }
            catch (Exception hookEx) when (hookEx is not OutOfMemoryException) { Notice?.Invoke("compaction failed hook failed: " + hookEx.Message); }
            return update;
        }
    }

    private IAsyncEnumerable<AgentEvent> RunCore(
        Func<AgentLoopConfig, StreamFunction, IAsyncEnumerable<AgentEvent>> start,
        AgentLoopConfig config,
        StreamFunction stream,
        CancellationToken cancellationToken)
    {
        return RunCoreAsync(start, config, stream, cancellationToken);
    }

    /// <summary>The request the loop sent last since the context last changed shape (compaction, a fresh task),
    /// kept so a compaction can ask on the very prefix the provider has cached.</summary>
    private (ModelConfig Model, LlmContext Context, StreamOptions Options)? _lastRequest;

    /// <summary>The model the loop sends now: the last request's, else the one the run started on.</summary>
    private ModelConfig CurrentModel(AgentLoopConfig config) => _requestModel ?? config.Model;

    private DateTime _compactionRetryAfter;
    private bool _savedSinceFailure;
    public TimeSpan CompactionRetryDelay { get; set; } = TimeSpan.FromSeconds(60);

    private StreamFunction Recording(StreamFunction stream) => (model, context, options, ct) =>
    {
        _lastRequest = (model, context, options);
        _requestModel = model;
        return stream(model, context, options, ct);
    };

    private async IAsyncEnumerable<AgentEvent> RunCoreAsync(
        Func<AgentLoopConfig, StreamFunction, IAsyncEnumerable<AgentEvent>> start,
        AgentLoopConfig config,
        StreamFunction stream,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        _saveTurnAsked = false;
        _continueAfterSave = false;
        _saveReplyPending = false;
        _requestModel = config.Model;
        _compactionRetryAfter = default;
        _savedSinceFailure = false;
        _commandHooks = config.CommandHooks;
        _hookTaskKey = config.HookTaskKey;
        if (CompactionSettings.Enabled)
        {
            var estimate = ContextTokens();
            if (Compactor.ShouldCompact(estimate, config.Model, CompactionSettings)
                && await CompactAsync(config.Model, stream, cancellationToken: cancellationToken).ConfigureAwait(false) is not null)
                AfterCompactionOutsideTurn("before-run", estimate);
        }

        // The first pass's AgentEnd is withheld when it overflowed: the retry (or the eventual
        // ContextOverflowException) is the real outcome, so a silent error turn never reaches the
        // caller. `overflow` records the terminal assistant message that triggered recovery.
        AssistantMessage? overflow = null;
        var hooked = WithSessionHooks(config, stream, cancellationToken);
        _noCompactionBelowTokens = 0;

        Volatile.Write(ref _messageEndsPersisted, 0);
        await foreach (var evt in start(hooked, Recording(stream)).WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            switch (evt)
            {
                case MessageEnd { Message: { } message }:
                    PersistIfNeeded(message, config.Model.ContextWindow);
                    Interlocked.Increment(ref _messageEndsPersisted);
                    yield return evt;
                    break;
                case AgentEnd end:
                    overflow = FindOverflow(end.Messages, config.Model.ContextWindow);
                    if (overflow is null) yield return end;
                    break;
                default:
                    yield return evt;
                    break;
            }
        }

        // The request that overflowed cannot carry a summary instruction on top (cleared per round below).
        if (overflow is null) yield break;

        // Deterministic retry (wi-8): overflow is a recoverable condition only if compaction can
        // make the context fit. A single compaction pass is the common case; we allow a small,
        // fixed number of compaction-and-retry rounds (never an unbounded loop), and every round
        // must shrink the context or we stop instead of re-sending the same oversized prompt
        // (which is what killed runs silently before). The final round's result is inspected for
        // overflow: if the turn still does not fit we surface ContextOverflowException rather than
        // yielding a silent error turn.
        var lastOverflow = overflow;
        var rounds = 0;
        while (rounds < MaxOverflowRetries)
        {
            rounds++;
            _lastRequest = null;
            CompactionResult? compacted;
            try
            {
                compacted = await CompactAsync(CurrentModel(config), stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // wi-8 A4: persist the terminal overflow turn so the session JSONL ends with it.
                Session.AppendMessage(lastOverflow!);
                throw new ContextOverflowException(lastOverflow, "Compaction after overflow failed: " + ex.Message);
            }

            if (compacted is null)
            {
                // wi-8 A4: persist the terminal overflow turn so the session JSONL ends with it.
                Session.AppendMessage(lastOverflow!);
                throw new ContextOverflowException(lastOverflow, "Context overflow and nothing left to compact");
            }
            AfterCompactionOutsideTurn("overflow", compacted.TokensBefore);

            var beforeTokens = ContextTokens();

            var retryHadOverflow = false;
            Volatile.Write(ref _messageEndsPersisted, 0);
            await foreach (var evt in AgentLoop.Continue(Context, hooked, stream, cancellationToken)
                .WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                if (evt is MessageEnd { Message: { } message })
                {
                    PersistIfNeeded(message, config.Model.ContextWindow);
                    Interlocked.Increment(ref _messageEndsPersisted);
                    if (message is AssistantMessage assistant && IsContextOverflow(assistant, config.Model.ContextWindow))
                    {
                        retryHadOverflow = true;
                        lastOverflow = assistant;
                    }
                }
                yield return evt;
            }

            if (!retryHadOverflow) yield break;

            // The retry overflowed again. Stop early when compaction is not making the context fit,
            // so we never re-send an oversized prompt a third time. Compare the current estimate to
            // the round's starting point: if it grew back to (or above) what we just sent, another
            // compaction cannot help and we bail deterministically.
            var afterTokens = ContextTokens();
            if (afterTokens >= beforeTokens)
            {
                // wi-8 A4: persist the terminal overflow turn so the session JSONL ends with it.
                Session.AppendMessage(lastOverflow!);
                throw new ContextOverflowException(lastOverflow,
                    $"Context overflow persisted after {rounds} compaction round(s); the turn does not fit "
                    + "and further compaction would re-send an oversized prompt");
            }
        }

        // wi-8 A4: persist the terminal overflow turn so the session JSONL ends with it.
        Session.AppendMessage(lastOverflow!);
        throw new ContextOverflowException(lastOverflow,
            $"Context overflow persisted after {MaxOverflowRetries} compaction-and-retry rounds");
    }

    private void PersistIfNeeded(Message message, int contextWindow)
    {
        switch (message)
        {
            case CompactionSummaryMessage:
                return;
            case AssistantMessage assistant when assistant.StopReason is StopReason.Error or StopReason.Aborted:
                if (IsContextOverflow(assistant, contextWindow)) return;
                Session.AppendMessage(assistant);
                return;
            case UserMessage or AssistantMessage or ToolResultMessage:
                Session.AppendMessage(message);
                return;
        }
    }

    private AssistantMessage? FindOverflow(IReadOnlyList<Message> newMessages, int contextWindow)
    {
        for (var i = newMessages.Count - 1; i >= 0; i--)
        {
            if (newMessages[i] is AssistantMessage assistant && IsContextOverflow(assistant, contextWindow))
                return assistant;
        }
        return null;
    }
}
