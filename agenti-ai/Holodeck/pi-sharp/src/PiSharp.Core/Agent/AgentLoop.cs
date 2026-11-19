// Ported from pi: packages/agent/src/agent-loop.ts @ 71dca871b
// Differences: no beforeToolCall/afterToolCall hooks (decision 3), events flow through a
// Channel instead of pi's EventStream, the final message list rides on AgentEnd.
using System.Text.Json.Nodes;
using System.Threading.Channels;
using PiSharp.Core.Messages;
using PiSharp.Core.Models;
using PiSharp.Core.Guard;
using PiSharp.Core.Session;

namespace PiSharp.Core.Agent;

/// <summary>
/// Low-level agent loop: streams an assistant response, executes its tool calls, repeats until the
/// model stops, honoring steering and follow-up queues. Stateless; all state lives in <see cref="AgentContext"/>.
/// </summary>
public static class AgentLoop
{
    private delegate ValueTask Emit(AgentEvent evt);

    /// <summary>
    /// Start a run with new prompt messages. Emits message events for the prompts, then runs the loop.
    /// The returned stream is hot: the loop starts immediately and buffers events until read.
    /// </summary>
    public static IAsyncEnumerable<AgentEvent> Run(
        IReadOnlyList<Message> prompts,
        AgentContext context,
        AgentLoopConfig config,
        StreamFunction streamFunction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prompts);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(streamFunction);
        if (prompts.Count == 0) throw new ArgumentException("At least one prompt message is required", nameof(prompts));

        return Start(async (emit, ct) =>
        {
            await emit(new AgentStart());
            await emit(new TurnStart());

            var newMessages = new List<Message>();
            foreach (var prompt in await UserPromptHooksAsync(prompts, config, ct).ConfigureAwait(false))
            {
                await emit(new MessageStart(prompt));
                await emit(new MessageEnd(prompt));
                context.Messages.Add(prompt);
                newMessages.Add(prompt);
            }

            await RunLoopAsync(context, newMessages, config, streamFunction, emit, ct);
        }, cancellationToken);
    }

    /// <summary>
    /// Continue from the current context without adding a message (retries, resumed sessions).
    /// The last context message must convert to a user or toolResult message.
    /// </summary>
    public static IAsyncEnumerable<AgentEvent> Continue(
        AgentContext context,
        AgentLoopConfig config,
        StreamFunction streamFunction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(streamFunction);
        if (context.Messages.Count == 0) throw new InvalidOperationException("Cannot continue: no messages in context");
        if (context.Messages[^1] is AssistantMessage) throw new InvalidOperationException("Cannot continue from message role: assistant");

        return Start(async (emit, ct) =>
        {
            await emit(new AgentStart());
            await emit(new TurnStart());
            await RunLoopAsync(context, [], config, streamFunction, emit, ct);
        }, cancellationToken);
    }

    /// <summary>Convenience: run to completion and return the messages the run added.</summary>
    public static async Task<IReadOnlyList<Message>> RunToEndAsync(
        IAsyncEnumerable<AgentEvent> events,
        Func<AgentEvent, ValueTask>? onEvent = null)
    {
        IReadOnlyList<Message> result = [];
        await foreach (var evt in events)
        {
            if (onEvent is not null) await onEvent(evt);
            if (evt is AgentEnd end) result = end.Messages;
        }
        return result;
    }

    private static IAsyncEnumerable<AgentEvent> Start(Func<Emit, CancellationToken, Task> body, CancellationToken cancellationToken)
    {
        var lifetime = new CancellationTokenSource();
        var runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
        var channel = Channel.CreateBounded<AgentEvent>(new BoundedChannelOptions(256) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
        Emit emit = evt =>
        {
            // Intermediate snapshots may be coalesced under backpressure; terminal messages must survive.
            if (evt is MessageUpdate or ToolExecutionUpdate) { channel.Writer.TryWrite(evt); return ValueTask.CompletedTask; }
            return channel.Writer.WriteAsync(evt, lifetime.Token);
        };

        var producer = Task.Run(async () =>
        {
            try
            {
                await body(emit, runCancellation.Token).ConfigureAwait(false);
                channel.Writer.TryComplete();
            }
            catch (Exception ex)
            {
                channel.Writer.TryComplete(ex);
            }
        });

        return Consume();
        async IAsyncEnumerable<AgentEvent> Consume([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken consumerCancellation = default)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, consumerCancellation);
            try { await foreach (var item in channel.Reader.ReadAllAsync(linked.Token).ConfigureAwait(false)) yield return item; }
            finally { lifetime.Cancel(); await producer.ConfigureAwait(false); runCancellation.Dispose(); lifetime.Dispose(); }
        }
    }

    private static async Task RunLoopAsync(
        AgentContext initialContext,
        List<Message> newMessages,
        AgentLoopConfig initialConfig,
        StreamFunction streamFunction,
        Emit emit,
        CancellationToken ct)
    {
        var context = initialContext;
        var config = initialConfig;
        TurnContext? lastCompletedTurn = null;
        string? lastToolSignature = null;
        var identicalToolBatches = 0;
        var loopGuardRetries = 0;
        var degenerateReprompted = false;
        var stopHookBlocks = 0;
        // Places this run posted to since its last other tool call (see SplitByCaps).
        var postedSinceWork = new HashSet<string>(StringComparer.Ordinal);

        // Steering typed while the previous run was finishing.
        var pending = await PollAsync(config.GetSteeringMessages);

        // Outer loop: continues when follow-up messages arrive after the agent would stop.
        while (true)
        {
            var hasMoreToolCalls = true;

            // Inner loop: tool calls and steering messages.
            while (hasMoreToolCalls || pending.Count > 0)
            {
                IReadOnlyList<Message> trailing = [];
                if (lastCompletedTurn is not null)
                {
                    if (config.PrepareNextTurn is not null)
                    {
                        var update = await config.PrepareNextTurn(lastCompletedTurn);
                        if (update is not null)
                        {
                            trailing = update.Trailing ?? [];
                            if (update.Context is { } replacement && !ReferenceEquals(replacement, context))
                                replacement.PendingCleanup = context.PendingCleanup;
                            context = update.Context ?? context;
                            config = config with
                            {
                                Model = update.Model ?? config.Model,
                                Reasoning = update.ThinkingLevel is null
                                    ? config.Reasoning
                                    : update.ThinkingLevel == ThinkingLevel.Off ? null : update.ThinkingLevel,
                            };
                        }
                    }
                    // Preparation can be slow (compaction). Pick up steering queued meanwhile,
                    // but only if the earlier poll returned nothing.
                    if (pending.Count == 0)
                    {
                        pending = await PollAsync(config.GetSteeringMessages);
                    }
                    await emit(new TurnStart());
                }

                if (pending.Count > 0)
                {
                    pending = await UserPromptHooksAsync(pending, config, ct).ConfigureAwait(false);
                    foreach (var message in pending)
                    {
                        await emit(new MessageStart(message));
                        await emit(new MessageEnd(message));
                        context.Messages.Add(message);
                        newMessages.Add(message);
                    }
                    pending = [];
                }

                // After the steering messages, so a note from PrepareNextTurn is read last.
                foreach (var message in trailing)
                {
                    await emit(new MessageStart(message));
                    await emit(new MessageEnd(message));
                    context.Messages.Add(message);
                    newMessages.Add(message);
                }

                var assistant = await StreamAssistantResponseAsync(context, config, streamFunction, emit, ct);
                newMessages.Add(assistant);

                if (assistant.StopReason is StopReason.Error or StopReason.Aborted)
                {
                    await emit(new TurnEnd(assistant, []));
                    await emit(new AgentEnd(newMessages));
                    return;
                }

                var toolCalls = assistant.ToolCalls.ToList();
                var toolResults = new List<ToolResultMessage>();
                hasMoreToolCalls = false;

                if (toolCalls.Count > 0)
                {
                    var signature = ToolCatalog.BatchSignature(toolCalls);
                    if (signature.Length > 0 && signature == lastToolSignature) identicalToolBatches++;
                    else
                    {
                        identicalToolBatches = signature.Length > 0 ? 1 : 0;
                        lastToolSignature = signature.Length > 0 ? signature : null;
                    }
                    // Input actions (a key, a click) repeat as real steps, so a batch of only those gets the larger limit.
                    var limit = toolCalls.All(c => context.Tools.FirstOrDefault(t => t.Name == c.Name) is { InputAction: true })
                        ? config.MaxIdenticalInputCalls : config.MaxIdenticalToolCalls;
                    var looping = config.MaxIdenticalToolCalls > 0 && limit > 0 && identicalToolBatches >= limit;
                    // A guard can be a transient bad response: the run gets LoopGuardRetries fresh model turns with the
                    // guard's refusal in context before the guard ends it.
                    var retryAfterGuard = looping && loopGuardRetries < config.LoopGuardRetries;
                    if (retryAfterGuard)
                    {
                        loopGuardRetries++;
                        lastToolSignature = null;
                        identicalToolBatches = 0;
                    }

                    // A "length" stop means the output was cut by the token limit, so every tool
                    // call may carry truncated arguments. Fail them all instead of executing.
                    var batch = assistant.StopReason == StopReason.Length
                        ? await FailToolCallsFromTruncatedMessageAsync(toolCalls, emit)
                        : looping
                            ? await FailToolCallsFromLoopAsync(toolCalls, limit, emit)
                            : await ExecuteCappedToolCallsAsync(context, toolCalls, config, postedSinceWork, emit, ct);
                    NotePostsSinceWork(postedSinceWork, toolCalls, batch.Messages);

                    toolResults.AddRange(batch.Messages);
                    hasMoreToolCalls = retryAfterGuard || (!batch.Terminate && !looping);

                    foreach (var result in toolResults)
                    {
                        context.Messages.Add(result);
                        newMessages.Add(result);
                    }

                    // pi-sharp-checkpoints-3: after the batch's results are in, the green tracker may
                    // owe a one-shot last-green hint. Trailing, so the model reads it last.
                    if (config.AfterToolBatch is not null && toolResults.Count > 0)
                    {
                        IReadOnlyList<Message>? trailingHint = null;
                        try
                        {
                            trailingHint = await config.AfterToolBatch(toolResults, config, ct).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (ct.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch { /* a hint must never fail the run */ }
                        foreach (var message in trailingHint ?? [])
                        {
                            await emit(new MessageStart(message));
                            await emit(new MessageEnd(message));
                            context.Messages.Add(message);
                            newMessages.Add(message);
                        }
                    }
                }

                // A looped or length-cut text ends no task: cut it in the transcript and give the model one more turn.
                if (toolCalls.Count == 0 && pending.Count == 0 && !degenerateReprompted
                    && DegenerateReply.Why(assistant.Text, assistant.StopReason == StopReason.Length) is { } why)
                {
                    degenerateReprompted = true;
                    var cut = assistant with { Content = [new TextContent(DegenerateReply.Cut(assistant.Text))] };
                    if (context.Messages.Count > 0 && ReferenceEquals(context.Messages[^1], assistant)) context.Messages[^1] = cut;
                    newMessages[^1] = cut;
                    assistant = cut;
                    pending = [new UserMessage(string.Format(DegenerateReply.Nudge, why))];
                    await emit(new TurnEnd(assistant, toolResults, Final: false));
                    lastCompletedTurn = new TurnContext(assistant, toolResults, context, [.. newMessages], Final: false);
                    // Like a stop-hook block: no reply hook sees this turn, so nothing posts the cut text.
                    continue;
                }

                await emit(new TurnEnd(assistant, toolResults, Final: !hasMoreToolCalls));

                lastCompletedTurn = new TurnContext(assistant, toolResults, context, [.. newMessages], Final: !hasMoreToolCalls);

                if (!hasMoreToolCalls && config.CommandHooks is not null
                    && config.CommandHooks.Hooks.ContainsKey(CommandHookEvent.Stop)
                    && stopHookBlocks < Math.Max(0, config.MaxStopBlocks))
                {
                    var decision = await config.CommandHooks.InvokeAsync(CommandHookEvent.Stop, new System.Text.Json.Nodes.JsonObject
                    {
                        ["session_id"] = config.HookSessionId,
                        ["task_key"] = config.HookTaskKey,
                        ["last_assistant_message"] = string.Concat(assistant.Content.OfType<TextContent>().Select(c => c.Text)),
                        ["stop_hook_active"] = stopHookBlocks > 0,
                    }, ct).ConfigureAwait(false);
                    if (decision.Block)
                    {
                        stopHookBlocks++;
                        var note = decision.Reason ?? decision.AdditionalContext ?? "Stop hook requested another turn.";
                        var message = new UserMessage("Stop hook requested that the turn continue: " + note);
                        pending = [message];
                        continue;
                    }
                }

                // pi-sharp-checkpoints-7: the after-reply snapshot hook. Fires only when the turn
                // ended successfully: StopReason is not Error/Aborted (checked above), there are no
                // pending tool calls, and the stop hook did not block. Runs before
                // ShouldStopAfterTurn, the follow-up poll and AgentEnd. Must never fail the run.
                if (!hasMoreToolCalls && config.AfterSuccessfulReply is not null)
                {
                    try
                    {
                        await config.AfterSuccessfulReply(lastCompletedTurn, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch { /* a checkpoint must never fail the run */ }
                }

                var turnDecision = config.DecideTurn is not null
                    ? await config.DecideTurn(lastCompletedTurn).ConfigureAwait(false)
                    : config.ShouldStopAfterTurn is not null && await config.ShouldStopAfterTurn(lastCompletedTurn)
                        ? TurnDecision.Stop : TurnDecision.Poll;
                if (turnDecision == TurnDecision.Stop)
                {
                    await emit(new AgentEnd(newMessages));
                    return;
                }

                if (turnDecision == TurnDecision.Continue) hasMoreToolCalls = true;

                pending = await PollAsync(config.GetSteeringMessages);
            }

            var followUps = await PollAsync(config.GetFollowUpMessages);
            if (followUps.Count > 0)
            {
                pending = followUps;
                continue;
            }

            break;
        }

        await emit(new AgentEnd(newMessages));
    }

    private static async ValueTask<IReadOnlyList<Message>> PollAsync(Func<ValueTask<IReadOnlyList<Message>>>? source)
    {
        if (source is null) return [];
        return await source() ?? [];
    }

    private static async Task<AssistantMessage> StreamAssistantResponseAsync(
        AgentContext context,
        AgentLoopConfig config,
        StreamFunction streamFunction,
        Emit emit,
        CancellationToken ct)
    {
        IReadOnlyList<Message> messages = context.Messages;
        if (config.TransformContext is not null)
        {
            messages = await config.TransformContext(messages, ct);
        }

        var llmMessages = await config.ConvertToLlm(messages);
        context.Tools.OfType<ToolDiscovery>().FirstOrDefault()?.Observe(context.Messages.Count);
        var advertised = ToolCatalog.Present(context.Tools, config.Model, llmMessages);
        var llmContext = new LlmContext(context.SystemPrompt, llmMessages, advertised);
        var options = new StreamOptions
        {
            Reasoning = config.Reasoning,
            MaxTokens = config.MaxTokens,
            Temperature = config.Temperature,
            ApiKey = config.Model.ResolveApiKey(),
            CacheKey = config.CacheKey,
            FreeformTools = config.FreeformTools,
            ServerCompactThreshold = config.ServerCompactThreshold,
        };

        AssistantMessage? partial = null;
        AssistantMessage? final = null;
        var addedPartial = false;

        try
        {
            // Every request asks for no more output than its window leaves beside its prompt (digital-scribe, 2026-10-20: token
            // budgets come "at worst from config files at best from Subscriptions settings"): a 200k model capped at 131,072
            // writes up to 131,072 on a small context and ~20,000 near 180,000. Until then a request whose prompt plus the
            // whole cap passed the window was refused (and only with tool discovery), which held the compaction point at the
            // window less the cap. Only a prompt that fills the window is refused now.
            var cap = config.MaxTokens ?? config.Model.MaxOutputTokens;
            if (config.Model.ContextWindow <= 0 || cap <= 0)
            {
                final = EmptyAssistant(config.Model) with { StopReason = StopReason.Error, ErrorCode = "model_budget_not_configured",
                    ErrorMessage = $"Model '{config.Model.Id}' has no {(config.Model.ContextWindow <= 0 ? "contextWindow" : "maxOutputTokens")} configured; set it in models.json or on its Subscriptions assignment." };
            }
            else
            {
                var estimated = TokenEstimate.EstimateTextTokens(llmContext.SystemPrompt)
                    + llmMessages.Sum(m => TokenEstimate.EstimateMessageTokens(m) + 32)
                    + advertised.Sum(t => TokenEstimate.EstimateTextTokens(t.Parameters.ToJsonString() + t.Description + t.Name) + 32);
                var room = config.Model.ContextWindow - estimated;
                if (room < 1)
                {
                    final = EmptyAssistant(config.Model) with { StopReason = StopReason.Error, ErrorCode = "context_length_exceeded",
                        ErrorMessage = "Complete request exceeds the configured context window; compact or reduce mandatory input." };
                }
                else options = options with { MaxTokens = Math.Min(cap, room) };
            }
            if (final is null) await foreach (var evt in streamFunction(config.Model, llmContext, options, ct).ConfigureAwait(false))
            {
                switch (evt)
                {
                    case AssistantStart start:
                        partial = start.Partial;
                        context.Messages.Add(partial);
                        addedPartial = true;
                        await emit(new MessageStart(partial));
                        break;

                    case AssistantDone done:
                        final = done.Message;
                        break;

                    case AssistantError error:
                        final = error.Error;
                        break;

                    case AssistantPartialEvent update:
                        if (partial is not null)
                        {
                            partial = update.Partial;
                            context.Messages[^1] = partial;
                            await emit(new MessageUpdate(partial, update));
                        }
                        break;
                }

                if (final is not null) break;
            }
        }
        catch (Exception ex)
        {
            // The stream function violated its contract. Turn the failure into an error turn
            // so the caller still sees a complete event sequence.
            var aborted = ex is OperationCanceledException || ct.IsCancellationRequested;
            final = (partial ?? EmptyAssistant(config.Model)) with
            {
                StopReason = aborted ? StopReason.Aborted : StopReason.Error,
                ErrorMessage = aborted ? "Operation aborted" : ex.Message,
            };
        }

        final ??= (partial ?? EmptyAssistant(config.Model)) with
        {
            StopReason = StopReason.Error,
            ErrorMessage = "Stream ended without a terminal event",
        };

        if (config.RepairTextToolCalls
            && final.StopReason is StopReason.Stop or StopReason.Length
            && !final.ToolCalls.Any())
        {
            var repaired = TextToolCallParser.TryRepair(final, context.Tools);
            if (repaired is not null) final = repaired;
        }

        if (addedPartial)
        {
            context.Messages[^1] = final;
        }
        else
        {
            context.Messages.Add(final);
            await emit(new MessageStart(final));
        }
        if (config.RecordTurnUsage is { } recordTurn)
        {
            var contextTokens = TokenEstimate.EstimateContextTokens(llmContext).Tokens;
            recordTurn(final.Usage, contextTokens, final.ResponseModel ?? final.Model);
        }

        await emit(new MessageEnd(final));
        return final;
    }

    private static AssistantMessage EmptyAssistant(ModelConfig model) => new()
    {
        Content = [],
        Model = model.Id,
    };

    // ---- Tool execution -------------------------------------------------------------------

    private sealed record FinalizedToolCall(ToolCallContent ToolCall, AgentToolResult Result, bool IsError);

    private sealed record ToolBatch(IReadOnlyList<ToolResultMessage> Messages, bool Terminate);

    /// <summary>A call ready to run; <paramref name="Review"/> set when a rule asks for the reviewer's decision first.</summary>
    private sealed record PreparedToolCall(ToolCallContent ToolCall, IAgentTool Tool, JsonObject Arguments, GuardVerdict? Review = null);

    private static async Task<ToolBatch> FailToolCallsFromLoopAsync(IReadOnlyList<ToolCallContent> toolCalls, int limit, Emit emit)
    {
        var messages = new List<ToolResultMessage>();
        foreach (var toolCall in toolCalls)
        {
            await emit(new ToolExecutionStart(toolCall.Id, toolCall.Name, toolCall.Arguments));
            var finalized = new FinalizedToolCall(
                toolCall,
                AgentToolResult.Error(
                    $"Tool call \"{toolCall.Name}\" was not executed: the same tool call was repeated {limit} times in a row. Stop looping: " +
                    "look at the current state first (read the output, a capture or the window tree) and then try a different approach."),
                IsError: true);
            await EmitToolExecutionEndAsync(finalized, emit);
            var message = CreateToolResultMessage(finalized);
            await EmitToolResultMessageAsync(message, emit);
            messages.Add(message);
        }
        return new ToolBatch(messages, Terminate: true);
    }

    private static async Task<ToolBatch> FailToolCallsFromTruncatedMessageAsync(IReadOnlyList<ToolCallContent> toolCalls, Emit emit)
    {
        var messages = new List<ToolResultMessage>();
        foreach (var toolCall in toolCalls)
        {
            await emit(new ToolExecutionStart(toolCall.Id, toolCall.Name, toolCall.Arguments));
            var finalized = new FinalizedToolCall(
                toolCall,
                AgentToolResult.Error(
                    $"Tool call \"{toolCall.Name}\" was not executed: the response hit the output token limit, so its arguments may be truncated. Re-issue the tool call with complete arguments."),
                IsError: true);
            await EmitToolExecutionEndAsync(finalized, emit);
            var message = CreateToolResultMessage(finalized);
            await EmitToolResultMessageAsync(message, emit);
            messages.Add(message);
        }
        return new ToolBatch(messages, Terminate: false);
    }

    /// <summary>
    /// Runs the calls within the per-message caps and fails the rest without executing them, so
    /// one runaway message (hundreds of chat posts, 2026-09-27) cannot act hundreds of times.
    /// </summary>

    /// <summary>pi-sharp-checkpoints-1: the built-in tools that mutate the work tree. A batch
    /// containing one of these triggers the <see cref="AgentLoopConfig.BeforeMutatingBatch"/> hook.
    /// MCP and custom tools are excluded by design (their side effects are unknown and unbounded); a
    /// seat that wants them checkpointed can name them in a future config surface.</summary>
    private static bool IsMutatingToolName(string? name) =>
        name is "edit" or "write" or "apply_patch" or "bash" or "job";
    private static async Task<ToolBatch> ExecuteCappedToolCallsAsync(
        AgentContext context,
        IReadOnlyList<ToolCallContent> toolCalls,
        AgentLoopConfig config,
        IReadOnlySet<string> postedSinceWork,
        Emit emit,
        CancellationToken ct)
    {
                // pi-sharp-checkpoints-1: one shadow snapshot before a mutating batch, before the
                // first tool of the batch runs. Skipped when the batch mutates nothing; must never
                // fail the batch.
                if (config.BeforeMutatingBatch is not null
                    && toolCalls.Any(c => IsMutatingToolName(c.Name)))
                {
                    try
                    {
                        await config.BeforeMutatingBatch(toolCalls, context, config, ct).ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                        // A checkpoint must never fail the batch.
                    }
                }
        var (allowed, rejected) = SplitByCaps(toolCalls, config.MaxToolCallsPerMessage, config.MaxChatPostsPerMessage, postedSinceWork);
        if (rejected.Count == 0) return await ExecuteToolCallsAsync(context, toolCalls, config, emit, ct);

        var executed = allowed.Count > 0
            ? await ExecuteToolCallsAsync(context, allowed, config, emit, ct)
            : new ToolBatch([], Terminate: false);
        var messages = new List<ToolResultMessage>(executed.Messages);
        foreach (var (toolCall, reason) in rejected)
        {
            await emit(new ToolExecutionStart(toolCall.Id, toolCall.Name, toolCall.Arguments));
            var finalized = new FinalizedToolCall(toolCall, AgentToolResult.Error(reason), IsError: true);
            await EmitToolExecutionEndAsync(finalized, emit);
            var message = CreateToolResultMessage(finalized, config);
            await EmitToolResultMessageAsync(message, emit);
            messages.Add(message);
        }
        return new ToolBatch(messages, executed.Terminate);
    }

    public static (List<ToolCallContent> Allowed, List<(ToolCallContent Call, string Reason)> Rejected) SplitByCaps(
        IReadOnlyList<ToolCallContent> toolCalls, int maxCalls, int maxPosts, IReadOnlySet<string>? postedSinceWork = null)
    {
        var allowed = new List<ToolCallContent>();
        var rejected = new List<(ToolCallContent, string)>();
        var posts = toolCalls.Count(IsChatPost);
        var posted = 0;
        // The 2026-09-27 runaway repeated itself inside one message: 68 identical kb_get calls, and 455 posts, most
        // of them to the same room. An identical call cannot learn anything its twin does not, and a second post to
        // the same place in one message is one post split in two.
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        var postedTo = new HashSet<string>(StringComparer.Ordinal);
        foreach (var toolCall in toolCalls)
        {
            var isPost = IsChatPost(toolCall);
            var key = toolCall.Name + "\n" + Canonical(toolCall.Arguments);
            var target = isPost ? PostTarget(toolCall.Arguments) : null;
            if (seen.TryGetValue(key, out var twin))
            {
                rejected.Add((toolCall,
                    $"Tool call \"{toolCall.Name}\" was not executed: it is identical to call {twin} in this message, whose result is above. Do not repeat a call."));
            }
            else if (target is not null && postedTo.Contains(target))
            {
                rejected.Add((toolCall,
                    $"Tool call \"{toolCall.Name}\" was not executed: this message already posts to {target}. Put everything for one place in one post."));
            }
            else if (target is not null && postedSinceWork?.Contains(target) == true)
            {
                // 2026-10-06, agent-maker-3: one message per model call, so the per-message rule never saw it, posted
                // 13 rewordings of one "OPS CHECK complete" note to ops in two minutes with no other call between them.
                rejected.Add((toolCall,
                    $"Tool call \"{toolCall.Name}\" was not executed: you already posted to {target} and have done nothing else since. That post was delivered; its id is in the result above. Do not post it again in other words: end the turn, or do the next piece of work first."));
            }
            else if (maxCalls > 0 && allowed.Count >= maxCalls)
            {
                rejected.Add((toolCall,
                    $"Tool call \"{toolCall.Name}\" was not executed: this message made {toolCalls.Count} tool calls and at most {maxCalls} run per message. Send fewer, larger calls."));
            }
            else if (isPost && maxPosts > 0 && posted >= maxPosts)
            {
                rejected.Add((toolCall,
                    $"Tool call \"{toolCall.Name}\" was not executed: this message made {posts} chat posts and at most {maxPosts} run per message. Post only what someone must act on, one message per thread."));
            }
            else
            {
                allowed.Add(toolCall);
                seen[key] = toolCall.Id;
                if (isPost) posted++;
                if (target is not null) postedTo.Add(target);
            }
        }
        return (allowed, rejected);
    }

    /// <summary>Carries the places posted to across model calls of one run: a message with any other tool call is
    /// work and clears them; a message of posts only adds the places its delivered posts reached.</summary>
    public static void NotePostsSinceWork(
        HashSet<string> postedSinceWork, IReadOnlyList<ToolCallContent> toolCalls, IReadOnlyList<ToolResultMessage> results)
    {
        if (toolCalls.Any(c => !IsChatPost(c)))
        {
            postedSinceWork.Clear();
            return;
        }
        foreach (var call in toolCalls)
            if (PostTarget(call.Arguments) is { } target
                && results.Any(r => r.ToolCallId == call.Id && !r.IsError))
                postedSinceWork.Add(target);
    }

    private static bool IsChatPost(ToolCallContent toolCall) =>
        toolCall.Name.EndsWith("chat_post", StringComparison.Ordinal);

    /// <summary>Where a chat post lands: its thread, direct recipient or room; null when it names none.</summary>
    private static string? PostTarget(JsonObject arguments)
    {
        foreach (var (name, label) in new[] { ("thread", "thread "), ("to", "@"), ("room", "room ") })
            if (arguments[name] is JsonValue value && value.ToString() is { Length: > 0 } text && text != "null")
                return label + text;
        return null;
    }

    /// <summary>The arguments with object keys sorted, so the same call spelled in another key order matches.</summary>
    private static string Canonical(JsonNode? node) => node switch
    {
        JsonObject o => "{" + string.Join(",", o.OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => JsonValue.Create(p.Key).ToJsonString() + ":" + Canonical(p.Value))) + "}",
        JsonArray a => "[" + string.Join(",", a.Select(Canonical)) + "]",
        null => "null",
        _ => node.ToJsonString(),
    };

    private static Task<ToolBatch> ExecuteToolCallsAsync(
        AgentContext context,
        IReadOnlyList<ToolCallContent> toolCalls,
        AgentLoopConfig config,
        Emit emit,
        CancellationToken ct)
    {
        try { config.RecordToolCalls?.Invoke(toolCalls.ToArray()); }
        catch { /* Optional instrumentation cannot affect tool execution. */ }
        return config.ToolExecution == ToolExecutionMode.Sequential
            ? ExecuteSequentialAsync(context, toolCalls, config, emit, ct)
            : ExecuteScheduledAsync(context, toolCalls, config, emit, ct);
    }

    // Mutations/unknown sequential tools are barriers. Independent reads on either side still share a wave.
    private static async Task<ToolBatch> ExecuteScheduledAsync(AgentContext context, IReadOnlyList<ToolCallContent> calls,
        AgentLoopConfig config, Emit emit, CancellationToken ct)
    {
        var messages = new List<ToolResultMessage>();
        var terminate = true;
        var wave = new List<ToolCallContent>();
        var waveAccess = new List<ToolAccess?>();
        async Task Flush()
        {
            if (wave.Count == 0) return;
            var batch = await ExecuteParallelAsync(context, wave.ToArray(), config, emit, ct);
            messages.AddRange(batch.Messages);
            terminate &= batch.Terminate;
            wave.Clear();
            waveAccess.Clear();
        }
        foreach (var call in calls)
        {
            if (ct.IsCancellationRequested) break;
            var tool = context.FindTool(call.Name);
            ToolAccess? access = null;
            try { access = tool?.Access(call.Arguments); } catch { /* validation will produce the actual error */ }
            if (tool?.ExecutionMode == ToolExecutionMode.Sequential
                || (access is { ReadOnly: false } && tool?.ExecutionMode != ToolExecutionMode.Parallel))
            {
                await Flush();
                if (ct.IsCancellationRequested) break;
                var batch = await ExecuteSequentialAsync(context, [call], config, emit, ct);
                messages.AddRange(batch.Messages);
                terminate &= batch.Terminate;
            }
            else
            {
                if (waveAccess.Any(prior => ResourceConflict(prior, access))) await Flush();
                wave.Add(call); waveAccess.Add(access);
            }
        }
        await Flush();
        return new ToolBatch(messages, messages.Count > 0 && terminate);
    }

    private static bool ResourceConflict(ToolAccess? first, ToolAccess? second)
    {
        if (first is { ReadOnly: true } && second is { ReadOnly: true }) return false;
        // Legacy tools follow their explicit execution mode. Annotated writes conflict with unknown effects.
        if (first is null || second is null) return first is { ReadOnly: false } || second is { ReadOnly: false };
        if (first.Resources.Count == 0 || second.Resources.Count == 0) return true;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return first.Resources.Any(a => second.Resources.Any(b =>
        {
            a = a.Replace('\\', '/').TrimEnd('/'); b = b.Replace('\\', '/').TrimEnd('/');
            return a.Equals(b, comparison) || a.StartsWith(b + "/", comparison) || b.StartsWith(a + "/", comparison);
        }));
    }

    private static async Task<ToolBatch> ExecuteSequentialAsync(
        AgentContext context,
        IReadOnlyList<ToolCallContent> toolCalls,
        AgentLoopConfig config,
        Emit emit,
        CancellationToken ct)
    {
        var finalizedCalls = new List<FinalizedToolCall>();
        var messages = new List<ToolResultMessage>();

        foreach (var toolCall in toolCalls)
        {
            await emit(new ToolExecutionStart(toolCall.Id, toolCall.Name, toolCall.Arguments));

            var finalized = PrepareToolCall(context, toolCall, config.ToolGuard, ct, config.ActionReviewer is not null, config.RecordGuard) switch
            {
                PreparedToolCall prepared => await ExecutePreparedAsync(prepared, emit, ct, config.ToolCallTimeout, config, context),
                FinalizedToolCall immediate => immediate,
                _ => throw new InvalidOperationException("unreachable"),
            };

            await EmitToolExecutionEndAsync(finalized, emit);
            var message = CreateToolResultMessage(finalized, config);
            await EmitToolResultMessageAsync(message, emit);
            finalizedCalls.Add(finalized);
            messages.Add(message);

            if (ct.IsCancellationRequested) break;
        }

        return new ToolBatch(messages, ShouldTerminateBatch(finalizedCalls));
    }

    /// <summary>
    /// Prepare every call in order, run the runnable ones concurrently, emit ToolExecutionEnd in
    /// completion order, then emit tool-result messages in assistant source order.
    /// </summary>
    private static async Task<ToolBatch> ExecuteParallelAsync(
        AgentContext context,
        IReadOnlyList<ToolCallContent> toolCalls,
        AgentLoopConfig config,
        Emit emit,
        CancellationToken ct)
    {
        var tasks = new List<Task<FinalizedToolCall>>();
        using var concurrency = new SemaphoreSlim(4);

        foreach (var toolCall in toolCalls)
        {
            await emit(new ToolExecutionStart(toolCall.Id, toolCall.Name, toolCall.Arguments));

            var preparation = PrepareToolCall(context, toolCall, config.ToolGuard, ct, config.ActionReviewer is not null, config.RecordGuard);
            if (preparation is FinalizedToolCall immediate)
            {
                await EmitToolExecutionEndAsync(immediate, emit);
                tasks.Add(Task.FromResult(immediate));
                if (ct.IsCancellationRequested) break;
                continue;
            }

            var prepared = (PreparedToolCall)preparation;
            tasks.Add(Task.Run(async () =>
            {
                await concurrency.WaitAsync().ConfigureAwait(false);
                try
                {
                if (ct.IsCancellationRequested)
                {
                    var abortedCall = new FinalizedToolCall(toolCall, AgentToolResult.Error("Operation aborted"), IsError: true);
                    await EmitToolExecutionEndAsync(abortedCall, emit);
                    return abortedCall;
                }
                var finalized = await ExecutePreparedAsync(prepared, emit, ct, config.ToolCallTimeout, config, context);
                await EmitToolExecutionEndAsync(finalized, emit);
                return finalized;
                }
                finally { concurrency.Release(); }
            }, CancellationToken.None));

            if (ct.IsCancellationRequested) break;
        }

        var ordered = await Task.WhenAll(tasks);
        var messages = new List<ToolResultMessage>(ordered.Length);
        foreach (var finalized in ordered)
        {
            var message = CreateToolResultMessage(finalized, config);
            await EmitToolResultMessageAsync(message, emit);
            messages.Add(message);
        }

        return new ToolBatch(messages, ShouldTerminateBatch(ordered));
    }

    /// <summary>UserPromptSubmit hooks (Codex): each arriving user or inbox message is shown to the hooks; a block withholds it
    /// with its reason, an added context follows it as a note.</summary>
    private static async Task<IReadOnlyList<Message>> UserPromptHooksAsync(IReadOnlyList<Message> pending, AgentLoopConfig config, CancellationToken ct)
    {
        if (config.CommandHooks is not { } hooks || !hooks.Has(CommandHookEvent.UserPromptSubmit)) return pending;
        var result = new List<Message>();
        foreach (var message in pending)
        {
            if (message is not UserMessage user) { result.Add(message); continue; }
            var decision = await hooks.InvokeAsync(CommandHookEvent.UserPromptSubmit, new System.Text.Json.Nodes.JsonObject
            {
                ["session_id"] = config.HookSessionId,
                ["task_key"] = config.HookTaskKey,
                ["prompt"] = ContentText.Of(user.Content),
            }, ct).ConfigureAwait(false);
            result.Add(decision.Block
                ? new UserMessage("[hook] A message was withheld by a UserPromptSubmit hook: " + (decision.Reason ?? "no reason given"))
                : message);
            if (!decision.Block && decision.AdditionalContext is { Length: > 0 } context) result.Add(new UserMessage("[hook] " + context));
        }
        return result;
    }

    /// <summary>PostToolUse hooks (Codex): they see the call and its output and may add feedback to the result; they cannot
    /// undo what ran.</summary>
    private static async Task<AgentToolResult> PostToolHooksAsync(PreparedToolCall prepared, AgentToolResult result, AgentLoopConfig config, CancellationToken ct)
    {
        if (config.CommandHooks is not { } hooks || !hooks.Has(CommandHookEvent.PostToolUse)) return result;
        var output = string.Concat(result.Content.OfType<TextContent>().Select(c => c.Text));
        var decision = await hooks.InvokeAsync(CommandHookEvent.PostToolUse, new System.Text.Json.Nodes.JsonObject
        {
            ["session_id"] = config.HookSessionId,
            ["task_key"] = config.HookTaskKey,
            ["tool_name"] = prepared.ToolCall.Name,
            ["tool_input"] = System.Text.Json.Nodes.JsonNode.Parse(prepared.Arguments.ToJsonString()),
            ["tool_output"] = output.Length <= 8_000 ? output : output[..4_000] + "\n...\n" + output[^4_000..],
            ["is_error"] = result.IsError,
        }, ct, prepared.ToolCall.Name).ConfigureAwait(false);
        var feedback = decision.Block ? decision.Reason ?? decision.AdditionalContext : decision.AdditionalContext;
        return feedback is { Length: > 0 } note ? result with { Content = [.. result.Content, new TextContent("\n[hook] " + note)] } : result;
    }

    private static bool ShouldTerminateBatch(IReadOnlyList<FinalizedToolCall> finalized) =>
        finalized.Count > 0 && finalized.All(f => f.Result.Terminate);

    /// <summary>Returns a <see cref="PreparedToolCall"/> or, on lookup/validation failure, a <see cref="FinalizedToolCall"/> error.</summary>
    private static object PrepareToolCall(AgentContext context, ToolCallContent toolCall, ToolGuard? toolGuard, CancellationToken ct, bool canReview = false,
        Action<ToolCallContent, GuardVerdict, string>? recordGuard = null)
    {
        var tool = context.FindTool(toolCall.Name);
        if (tool is null)
        {
            return new FinalizedToolCall(toolCall, AgentToolResult.Error($"Tool {toolCall.Name} not found"), IsError: true);
        }

        try
        {
            var raw = tool.PrepareArguments(toolCall.Arguments);
            var validated = ToolArgumentValidator.Validate(tool, raw);
            // toolkit op=call: the named tool is prepared, validated, guarded and run as if called by name; the
            // result keeps this call's id and name (adr-visual-toolkits section 3).
            var called = toolCall;
            if (tool is ToolDiscovery discovery && ToolDiscovery.IsCall(validated))
            {
                var (inner, innerArgs, error) = discovery.Dispatch(validated);
                if (inner is null)
                    return new FinalizedToolCall(toolCall, AgentToolResult.Error(error!), IsError: true);
                tool = context.FindTool(inner.Name) ?? inner;
                validated = ToolArgumentValidator.Validate(tool, tool.PrepareArguments(innerArgs));
                called = toolCall with { Name = tool.Name };
            }
            if (tool.Name == "bash" && validated["command"]?.GetValue<string>() is { } shellCommand
                && ShellPatch.Extract(shellCommand) is { } shellPatch && context.FindTool("apply_patch") is { } patchTool)
            {
                // A patch typed into the shell runs as the apply_patch tool (Codex does the same).
                tool = patchTool;
                validated = ToolArgumentValidator.Validate(tool, tool.PrepareArguments(new JsonObject { ["input"] = shellPatch }));
                called = toolCall with { Name = tool.Name };
            }
            if (ct.IsCancellationRequested)
            {
                return new FinalizedToolCall(toolCall, AgentToolResult.Error("Operation aborted"), IsError: true);
            }
            if (context.PendingCleanup.HasUnfinished && !ParallelSafeTools.IsParallelSafe(tool.Name)
                && tool.Access(validated)?.ReadOnly != true)
                return new FinalizedToolCall(toolCall, new AgentToolResult([new TextContent("A cancelled tool is still stopping. Mutations remain suspended until its cleanup finishes.")],
                    Terminate: true, IsError: true), IsError: true);
            var verdict = toolGuard?.Evaluate(called with { Arguments = validated });
            if (verdict is { Action: GuardAction.Block } || verdict is { Action: GuardAction.Ask } && !canReview)
            {
                Record(recordGuard, toolCall, verdict, verdict.Action == GuardAction.Block ? "blocked" : "unreviewed");
                return new FinalizedToolCall(toolCall, AgentToolResult.Error(verdict.Action == GuardAction.Block ? verdict.Message
                    : ToolGuard.Refusal(verdict.Rule, "It needs a review first and this run has no reviewer.")), IsError: true);
            }
            context.Tools.OfType<ToolDiscovery>().FirstOrDefault()?.NoteUse(tool.Name);
            return new PreparedToolCall(toolCall, tool, validated, verdict);
        }
        catch (Exception ex)
        {
            return new FinalizedToolCall(toolCall, AgentToolResult.Error(ex.Message), IsError: true);
        }
    }

    /// <summary>Tells the seat a guard decision (telemetry); never fails the call.</summary>
    private static void Record(Action<ToolCallContent, GuardVerdict, string>? recordGuard, ToolCallContent call, GuardVerdict verdict, string decision)
    {
        try { recordGuard?.Invoke(call, verdict, decision); } catch { }
    }

    private static async Task<FinalizedToolCall> ExecutePreparedAsync(PreparedToolCall prepared, Emit emit, CancellationToken ct, TimeSpan defaultTimeout, AgentLoopConfig config, AgentContext context)
    {
        // Calls in one batch may have been prepared before an earlier call exceeded cleanup.
        if (context.PendingCleanup.HasUnfinished && !ParallelSafeTools.IsParallelSafe(prepared.Tool.Name)
            && prepared.Tool.Access(prepared.Arguments)?.ReadOnly != true)
            return new FinalizedToolCall(prepared.ToolCall, new AgentToolResult([new TextContent("A cancelled tool is still stopping. Mutations remain suspended until its cleanup finishes.")],
                Terminate: true, IsError: true), IsError: true);
        if (prepared.Review is { } review && config.ActionReviewer is { } reviewer)
        {
            // Codex auto-review: a reviewer decides an Ask before it runs; a denial is not an error to retry around.
            var outcome = await reviewer.ReviewAsync(prepared.ToolCall with { Arguments = prepared.Arguments }, review, context, ct).ConfigureAwait(false);
            Record(config.RecordGuard, prepared.ToolCall, review, outcome.Allowed ? "allowed" : outcome.TimedOut ? "no answer" : outcome.StopTurn ? "denied, turn stopped" : "denied");
            if (!outcome.Allowed)
            {
                var text = ToolGuard.Refusal(review.Rule, "Denied by the reviewer: " + outcome.Rationale)
                    + (outcome.StopTurn ? " The reviewer has denied repeatedly, so this turn stops here: say in the work item's thread what you need and who must decide it." : "");
                return new FinalizedToolCall(prepared.ToolCall, new AgentToolResult([new TextContent(text)], IsError: true, Terminate: outcome.StopTurn), IsError: true);
            }
        }
        var acceptingUpdates = true;
        var updates = new List<ValueTask>();
        var gate = new object();

        void OnUpdate(AgentToolResult partialResult)
        {
            lock (gate)
            {
                if (!acceptingUpdates) return;
                updates.Add(emit(new ToolExecutionUpdate(prepared.ToolCall.Id, prepared.ToolCall.Name, prepared.ToolCall.Arguments, partialResult)));
            }
        }

        try
        {
            if (config.CommandHooks is not null)
            {
                var hookInput = new System.Text.Json.Nodes.JsonObject
                {
                    ["session_id"] = config.HookSessionId,
                    ["task_key"] = config.HookTaskKey,
                    ["tool_name"] = prepared.ToolCall.Name,
                    ["tool_input"] = System.Text.Json.Nodes.JsonNode.Parse(prepared.Arguments.ToJsonString()),
                };
                var hook = await config.CommandHooks.InvokeAsync(CommandHookEvent.PreToolUse, hookInput, ct, prepared.ToolCall.Name).ConfigureAwait(false);
                if (hook.Block)
                    return new FinalizedToolCall(prepared.ToolCall, AgentToolResult.Error(hook.Reason ?? hook.AdditionalContext ?? "Tool call blocked by PreToolUse hook."), IsError: true);
            }
            var deadline = prepared.Tool.MaxDuration ?? defaultTimeout;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            async Task<AgentToolResult> ExecuteReadAsync()
            {
                var key = await prepared.Tool.ReadCacheKeyAsync(prepared.Arguments, timeout.Token).ConfigureAwait(false);
                if (key is not null && context.ReadCache.Find(key, context.Messages) is { } cached
                    && await prepared.Tool.AcceptCachedReadAsync(prepared.Arguments, key, timeout.Token).ConfigureAwait(false))
                { context.ReadCache.RecordHit(); return cached; }
                var result = await prepared.Tool.ExecuteAsync(prepared.ToolCall.Id, prepared.Arguments, OnUpdate, timeout.Token).ConfigureAwait(false);
                if (key is not null && !result.IsError && !result.Terminate
                    && key == await prepared.Tool.ReadCacheKeyAsync(prepared.Arguments, timeout.Token).ConfigureAwait(false))
                    context.ReadCache.Remember(key, prepared.ToolCall.Id);
                return result;
            }
            var execution = ExecuteReadAsync();
            var delay = Task.Delay(deadline, ct);
            if (await Task.WhenAny(execution, delay).ConfigureAwait(false) == execution)
            {
                var result = await PostToolHooksAsync(prepared, await execution.ConfigureAwait(false), config, ct).ConfigureAwait(false);
                return new FinalizedToolCall(prepared.ToolCall, result, IsError: result.IsError);
            }
            if (ct.IsCancellationRequested)
            {
                timeout.Cancel();
                await CleanupAsync(execution, config.ToolCleanupTimeout, context.PendingCleanup).ConfigureAwait(false);
                return new FinalizedToolCall(prepared.ToolCall, AgentToolResult.Error("Operation aborted"), IsError: true);
            }

            timeout.Cancel();
            await emit(new ToolExecutionDeadline(prepared.ToolCall.Id, prepared.ToolCall.Name, deadline)).ConfigureAwait(false);
            var message = $"tool {prepared.ToolCall.Name} aborted after {deadline.TotalSeconds:0.###} s (deadline)";
            await CleanupAsync(execution, config.ToolCleanupTimeout, context.PendingCleanup).ConfigureAwait(false);
            return new FinalizedToolCall(prepared.ToolCall, AgentToolResult.Error(message), IsError: true);
        }
        catch (Exception ex)
        {
            var text = ex is OperationCanceledException && ct.IsCancellationRequested ? "Operation aborted" : ex.Message;
            return new FinalizedToolCall(prepared.ToolCall, AgentToolResult.Error(text), IsError: true);
        }
        finally
        {
            List<ValueTask> pendingUpdates;
            lock (gate)
            {
                acceptingUpdates = false;
                pendingUpdates = updates;
            }
            foreach (var update in pendingUpdates) await update;
        }
    }

    private static async Task CleanupAsync(Task execution, TimeSpan bound, PendingToolCleanup pending)
    {
        if (await Task.WhenAny(execution, Task.Delay(bound)).ConfigureAwait(false) == execution)
        { try { await execution.ConfigureAwait(false); } catch (Exception) { } }
        else
        {
            pending.Retain(execution);
            _ = execution.ContinueWith(t => _ = t.Exception, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            Console.Error.WriteLine("[tool] cancellation cleanup exceeded its bound; late task remains observed");
        }
    }

    private static ValueTask EmitToolExecutionEndAsync(FinalizedToolCall finalized, Emit emit) =>
        emit(new ToolExecutionEnd(finalized.ToolCall.Id, finalized.ToolCall.Name, finalized.Result, finalized.IsError));

    private static ToolResultMessage CreateToolResultMessage(FinalizedToolCall finalized, AgentLoopConfig? config = null) => new()
    {
        ToolCallId = finalized.ToolCall.Id,
        ToolName = finalized.ToolCall.Name,
        Content = ToolResultBudget.Apply(finalized.Result.Content, config?.ToolResultTokenBudget ?? 10_000),
        Details = ToolResultBudget.ApplyDetails(finalized.Result.Details),
        Usage = finalized.Result.Usage,
        AddedToolNames = finalized.Result.AddedToolNames is { Count: > 0 } names ? names : null,
        IsError = finalized.IsError,
    };

    private static async ValueTask EmitToolResultMessageAsync(ToolResultMessage message, Emit emit)
    {
        await emit(new MessageStart(message));
        await emit(new MessageEnd(message));
    }
}
