// Ported from pi: packages/ai/src/api/openai-completions.ts (streamOpenAICompletions) @ 71dca871b
// with the retry loop from packages/ai/src/utils/provider-retry.ts. Uses only HttpClient and
// System.Text.Json; no SDK.
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using PiSharp.Core.Agent;
using PiSharp.Core.Messages;
using PiSharp.Core.Models;
using PiSharp.Llm.Http;

namespace PiSharp.Llm.OpenAi;

/// <summary>
/// Streams chat completions from any OpenAI-compatible server. <see cref="Stream"/> matches the
/// <see cref="StreamFunction"/> delegate and never throws: failures surface as <see cref="AssistantError"/>.
/// "Never throws" covers the provider's answer too, not just the parsing of a body: a non-2xx that
/// exhausts the retry policy is turned into the terminal error event like any other failure. Only a
/// caller cancellation or a server-named retry delay beyond the policy's cap leave as exceptions.
/// </summary>
public sealed class OpenAiCompletionsClient
{
    private static readonly HttpClient SharedHttp = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        ConnectTimeout = TimeSpan.FromSeconds(10),
        // wi-30 D2: the 2026-09-15 freeze left pooled sockets ESTABLISHED with empty queues and
        // no timer. The ping turns a dead pooled connection into a retryable IOException within one
        // interval instead of a request hanging on it.
        KeepAlivePingDelay = TimeSpan.FromMinutes(10),
        KeepAlivePingTimeout = TimeSpan.FromSeconds(15),
    })
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };

    private readonly HttpClient _http;

    public OpenAiCompletionsClient(HttpClient? http = null)
    {
        _http = http ?? SharedHttp;
    }

    public RetryPolicy Retry { get; init; } = RetryPolicy.Default;

    /// <summary>
    /// pi-sharp-hh-7: in-turn stream retries. When the transport dies after partial content
    /// already left this iterator (a connection reset, the body bound, a 5xx mid-stream), the
    /// partial assistant message is dropped and the same request re-issued up to this many times
    /// with the policy's backoff (first wait = the policy's attempt-0 delay, ~2 s on
    /// <see cref="RetryPolicy.Patient"/>) and <c>retry-after</c> honoured. Zero disables the
    /// feature: a transport death after partial content is exactly today's error turn. Not a
    /// shared budget: the headers/transport leg keeps its own retries inside
    /// <c>SendWithRetryAsync</c> and the before-content body resend keeps <c>Retry.MaxRetries</c>;
    /// this knob only governs after-content reconnects.
    /// </summary>
    public int StreamRetries { get; init; } = 3;

    /// <summary>
    /// How long to wait for response headers. Local servers may spend minutes on prompt processing
    /// before the first byte, so this is generous. Reading the body is bounded by
    /// <see cref="BodyTimeout"/>.
    /// 3 hours: the shared hosts queue requests behind other tenants' long prompts, 10 minutes killed
    /// unattended runs that would have been served (coder-06, 2026-09-15), and a slow answer is still an
    /// answer (@digital-scribe, same day: "I wanna get the most out of these models even if takes ages").
    /// </summary>
    public TimeSpan HeadersTimeout { get; init; } = TimeSpan.FromHours(3);

    /// <summary>
    /// While it waits for response headers, how often the client asks the server whether it still works on the request
    /// (<c>GET {base}/requests/{id}</c>, the id it sent as <see cref="RequestIdHeader"/>; the factory router answers it, hub
    /// 1.0.293). Every seat the hub's stall recovery restarted between 2026-10-22 and 10-28 sat 30-35 minutes on an open
    /// connection to the router with no answer, while the router had no request running that long: the answer was lost on
    /// the way, and <see cref="HeadersTimeout"/> would have waited three hours. A request the router has finished, or does not
    /// know twice in a row, is sent again; one it still works on is waited for, however slow (@digital-scribe, 2026-09-15:
    /// "I wanna get the most out of these models even if takes ages"). A server that does not answer the question is not
    /// asked again. Zero disables.
    /// </summary>
    public TimeSpan RequestProbeEvery { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>The wait between <see cref="RequestProbeEvery"/> questions; tests replace it.</summary>
    public Func<TimeSpan, CancellationToken, Task> RequestProbeDelay { get; init; } = Task.Delay;

    /// <summary>The header that names a request to the router, so the client can ask about it.</summary>
    public const string RequestIdHeader = "X-Request-Id";

    /// <summary>
    /// How long to wait for the SSE stream once headers arrived, measured from response headers
    /// (total-time semantics, not per-chunk idle). The 21:00Z freeze on 2026-09-15 is this leg:
    /// headers arrived, no body byte ever did, and nothing bounded the read, so the seat hung
    /// forever. Architect ruling wi-30/slice-a-numbers: one knob, 10 minutes. On expiry the read
    /// throws an <see cref="HttpRequestException"/> naming the bound and the URL; that type is
    /// retryable (<c>RetryPolicy.IsRetryableException</c>). Reset on each content event, so a slow
    /// but live generation is never killed. The chosen gate: a stall that starts after content
    /// already streamed always dies at this call and is never resent mid-turn (mid-body dies,
    /// headers-only retries).
    /// Set to <see cref="Timeout.InfiniteTimeSpan"/> to disable the bound.
    /// </summary>
    public TimeSpan BodyTimeout { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// pi-sharp-hh-7: a body-idle bound for streams that only dribble noise. <see cref="BodyTimeout"/>
    /// is re-armed by every data event, so a server that sends only keep-alives or bare role
    /// deltas keeps the bound from ever firing and can idle for hours on a dead stream. When set,
    /// any read that comes back without a <em>content</em> event (no text, thinking or tool-call
    /// bytes) for this long turns the stream into the same retryable transport death as the
    /// bound. Zero (the default) disables it; the item's 2-3 minute bound for remote providers
    /// is set per provider.
    /// </summary>
    public TimeSpan BodyIdleTimeout { get; init; } = TimeSpan.Zero;

    /// <summary>
    /// Degenerate output: a run of this many consecutive <c>'!'</c> in the streamed text, thinking or
    /// tool-call arguments means the server's model is broken (see <see cref="DegenerateRun"/>), not that the
    /// answer is long. The client drops the connection (vLLM aborts the generation), discards the partial,
    /// and probes the model with a tiny request: while the probe is degenerate too, it waits with
    /// <see cref="DegenerateProbeBackoff"/> doubling up to <see cref="DegenerateProbeMaxBackoff"/>, without
    /// limit, until the caller cancels; once a probe is sane it re-issues the same request. Zero disables.
    /// </summary>
    public int DegenerateRunLimit { get; init; } = 64;

    /// <summary>First wait between probes of a degenerate model.</summary>
    public TimeSpan DegenerateProbeBackoff { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Longest wait between probes of a degenerate model.</summary>
    public TimeSpan DegenerateProbeMaxBackoff { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How often the same request may come back degenerate while a probe answers sanely before the turn
    /// fails. Then the run is tied to this prompt (a poisoned prefix-cache entry, say), and waiting on a
    /// healthy probe would wait forever.
    /// </summary>
    public int DegenerateResends { get; init; } = 1;

    /// <summary>Told when a degenerate model starts and stops being waited on.</summary>
    public Action<ModelHealthEvent>? OnModelHealth { get; init; }

    /// <summary>The wait between probes; tests replace it.</summary>
    public Func<TimeSpan, CancellationToken, Task> Delay { get; init; } = Task.Delay;

    /// <summary>Extra headers sent with every request.</summary>
    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();

    /// <summary>Receives the request body of every attempt. Handy for logging and tests.</summary>
    public Action<JsonObject>? OnRequest { get; init; }

    /// <summary>
    /// Told what a request waits on once it has waited <see cref="WaitNoticeAfter"/> without content: first "waiting", then
    /// what the router answers about it ("queued" with its place, "working"), and "done" when content came or the request
    /// ended. team-lead-2 waited 14:03-14:41 on 2026-10-29 for one routine turn and its seat said nothing; the seat's status
    /// line says it now (digital-scribe, 2026-10-29: "this is what I expected you to catch").
    /// </summary>
    public Action<ModelWait>? OnWait { get; init; }

    /// <summary>How long a request waits without content before <see cref="OnWait"/> is told.</summary>
    public TimeSpan WaitNoticeAfter { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>The seat log. Every re-send says why, with the request's id: until 2026-10-29 a stalled request was sent
    /// again at once and without a line, which hid a seat going round a one-slot model's queue for forty minutes.</summary>
    public Action<string> Log { get; init; } = line => Console.Error.WriteLine(line);

    /// <summary>A router's 503 with <c>retry-after</c> (its backend stayed busy for the wait the caller allowed,
    /// <see cref="QueueTimeoutHeader"/>) ends the request at once instead of being retried: the caller has somewhere else
    /// to run the turn (a hybrid seat's subscription).</summary>
    public bool BusyIsFinal { get; init; }

    /// <summary>The server is the factory router, which answers <c>GET {base}/requests/{id}</c> (hub 1.0.293+): before the body
    /// bound gives up on a response that has given nothing yet, the router is asked whether it still works on the request
    /// (<see cref="BodyBound"/>). Off for other servers, which do not answer the question and would only be kept waiting.</summary>
    public bool AskRouterBeforeResend { get; init; }

    /// <summary>The longest wait, in milliseconds, the caller allows in a router backend's queue (router 1.0.305+).</summary>
    public const string QueueTimeoutHeader = "x-ai-queue-timeout";

    public StreamFunction AsStreamFunction() => Stream;

    public async IAsyncEnumerable<AssistantMessageEvent> Stream(
        ModelConfig model,
        LlmContext context,
        StreamOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var assembler = new ChunkAssembler(model);
        yield return new AssistantStart(assembler.Snapshot());

        var url = model.BaseUrl.TrimEnd('/') + "/chat/completions";

        Exception? failure = null;
        var requestBody = RequestBuilder.Build(model, context, options);
        OnRequest?.Invoke(requestBody);
        var payload = requestBody.ToJsonString();
        var apiKey = options.ApiKey ?? model.ResolveApiKey();

        // wi-30 D1: bound the body leg. Headers arrived during the 21:00Z freeze and no body byte
        // ever did; nothing bounded the read, so the seat hung on an ESTABLISHED socket forever.
        // The bound is armed at the first body read and re-armed on every SSE data event the stream
        // produces (the reset-per-event alternative the architect ruling allows): a stream that
        // produces nothing dies after BodyTimeout with a message naming the bound; a slow but live
        // generation never dies.
        //
        // wi-30 D1 retry gate, the trade-off the ruling and the PO accepted in place of the full
        // gate, named here per the PO's condition: a stalled body always dies at this Stream(...)
        // call and the client resends while nothing of the response reached the caller. A stall
        // after content already streamed is never resent - it would produce a second assistant
        // message over the partial the agent loop already accumulated. Mid-body dies, headers-only
        // retries. A non-content data event (a keep-alive comment, a bare role delta) does NOT
        // re-arm: the stall we measured emitted nothing, and a stream that only dribbles noise is
        // a stall.
        var bodyBoundEnabled = BodyTimeout != Timeout.InfiniteTimeSpan && BodyTimeout > TimeSpan.Zero;
        var idleBoundEnabled = BodyIdleTimeout != Timeout.InfiniteTimeSpan && BodyIdleTimeout > TimeSpan.Zero;
        var callerHasContent = false;
        // pi-sharp-hh-7: how many reconnects were consumed by this logical turn. Each failed
        // after-content send consumes one; the headers leg and the before-content body resend
        // keep their own budgets and do not touch this.
        // Resend budget: the headers/transport leg consumes its own retries inside
        // SendWithRetryAsync and returns only on success; the body bound is the only failure that
        // can still arrive after a success, and it is what this budget covers. It is deliberately
        // separate from the headers budget (architect ruling: "on each expiry the caller retries"),
        // and unlike the headers path it can only fire before any of the response reached a caller,
        // so a resend never duplicates an answer.
        var bodyResendsLeft = Retry.MaxRetries;
        var streamReconnectsUsed = 0;
        using var wait = new WaitNotice(OnWait, model.Id, WaitNoticeAfter);
        var degenerateResendsUsed = 0;
        for (var send = 0; ; send++)
        {
            // The headers leg answers either with a success response or with the failure it could
            // not retry. That failure must never leave this iterator as a thrown exception: Stream
            // promises AssistantStart ... terminal-event, and a throw here kills the consumer
            // mid-enumeration instead of delivering an AssistantError. A non-2xx that exhausts the
            // retry policy (llama.cpp answers the overflow probe with HTTP 400 before any SSE body;
            // a bad key gets a 401) is such a case - it used to escape as LlmHttpException
            // (pi-sharp-wi-tests-clean-seat-default).
            var (response2, failure2, requestId) = await SendWithRetryAsync(url, payload, apiKey, wait, cancellationToken).ConfigureAwait(false);
            // The server counts the prompt exactly; the agent loop only estimates it before asking for min(cap, window - prompt).
            // A server that says the request overflows its window by our estimate's error gets it once more, asking for exactly
            // the output it says is left (demo-01, 2026-10-21: "you requested 131072 output tokens and your prompt contains at
            // least 68929 input tokens, for a total of at least 200001 tokens").
            if (response2 is null && failure2 is LlmHttpException { StatusCode: 400 } overflow
                && OutputRoom(overflow.Message) is int fit && requestBody[model.Compat.MaxTokensField] is not null)
            {
                requestBody[model.Compat.MaxTokensField] = fit;
                OnRequest?.Invoke(requestBody);
                payload = requestBody.ToJsonString();
                (response2, failure2, requestId) = await SendWithRetryAsync(url, payload, apiKey, wait, cancellationToken).ConfigureAwait(false);
            }
            if (response2 is null)
            {
                yield return Fail(assembler, failure2!, cancellationToken);
                yield break;
            }
            var response = response2;
            using var _ = response;
            var bodyCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            using (bodyCts)
            {
                var idleCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                using (idleCts)
                {
                    // The idle bound cancels the body read when it fires: the read is blocked on
                    // bodyCts.Token, so the idle bound must trip bodyCts to interrupt it. The
                    // catch block then diagnoses the failure as the idle bound (not the total
                    // bound) by checking idleCts.IsCancellationRequested.
                    var idleRegistration = idleCts.Token.Register(() => bodyCts.Cancel());
                    using (idleRegistration)
                {
                // The bound asks the router before it gives up on a response that has given nothing yet (BodyBound).
                var probeUrl = AskRouterBeforeResend && requestId is not null ? ProbeUrl(url, requestId) : null;
                using var bound = bodyBoundEnabled
                    ? new BodyBound(bodyCts, BodyTimeout, probeUrl is null ? null : c => StillWorkingAsync(probeUrl, apiKey, requestId!, model.Id, wait, c), cancellationToken)
                    : null;
                await using var content = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var chunks = SseReader.ReadDataAsync(content, bodyCts.Token).GetAsyncEnumerator(cancellationToken);
                var bodyWasBound = false;
                var run = new DegenerateRun();
                var degenerate = false;

                while (true)
                {
                    string data;
                    try
                    {
                        bound?.Arm(); // arm / re-arm
                        if (idleBoundEnabled) idleCts.CancelAfter(BodyIdleTimeout);
                        if (!await chunks.MoveNextAsync().ConfigureAwait(false)) break;
                        data = chunks.Current;
                    }
                    catch (Exception ex)
                    {
                        // The bound fires by cancelling, so any transport exception surfacing while
                        // it is already tripped belongs to the bound, whatever the socket called it
                        // ("Unable to read data from the transport connection", a premature end):
                        // the diagnosis must name the bound the caller is waiting on. A genuine
                        // caller cancellation is excluded by the caller-token check.
                        if (idleCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                        {
                            // pi-sharp-hh-7: the stream only dribbled noise (keep-alives, bare role
                            // deltas) for BodyIdleTimeout - the total-time bound is being re-armed
                            // by noise it should not count, so this is the same dead stream the
                            // bound exists to kill. The diagnosis is the bound's shape: an
                            // HttpRequestException naming a bound, so it is retryable like any
                            // transport death.
                            failure = new HttpRequestException(
                                $"No SSE content for {BodyIdleTimeout.TotalSeconds:0}s from {url}");
                        }
                        else
                        {
                            failure = MapBodyTimeout(ex, url, bodyCts, cancellationToken);
                        }
                        // Only the bound's own diagnosis is resendable. An exception thrown while
                        // the caller was suspended (its token fired but was observed late) is not.
                        var boundFired = (bodyCts.IsCancellationRequested || idleCts.IsCancellationRequested)
                            && !cancellationToken.IsCancellationRequested;
                        bodyWasBound = boundFired && failure is HttpRequestException;
                        break;
                    }

                    IReadOnlyList<AssistantMessageEvent> events;
                    try
                    {
                        events = assembler.Apply(data);
                    }
                    catch (Exception ex)
                    {
                        failure = ex;
                        break;
                    }

                    // Content arrived: the caller owns bytes of this response now; re-arm the bound
                    // and mark the gate closed for any later stall.
                    if (assembler.Snapshot().Content.Count > 0)
                    {
                        bound?.ContentReached();
                        bound?.Arm();
                    }
                    // What streams in, for the seat's status line while a long answer comes (WaitNotice.Receiving).
                    foreach (var e in events)
                    {
                        switch (e)
                        {
                            case TextDelta t: wait.Receiving(t.Delta.Length, thinking: false); break;
                            case ThinkingDelta t: wait.Receiving(t.Delta.Length, thinking: true); break;
                            case ToolCallDelta t: wait.Receiving(t.Delta.Length, thinking: false); break;
                        }
                    }

                    // pi-sharp-hh-7: only content re-arms the idle bound; a data event that
                    // produced no content events (a keep-alive comment, a bare role delta) does
                    // not, which is exactly what the bound is for.
                    if (idleBoundEnabled
                        && (events.Any(e => e is TextDelta { Delta.Length: > 0 })
                            || events.Any(e => e is ThinkingDelta { Delta.Length: > 0 })
                            || events.Any(e => e is ToolCallDelta { Delta.Length: > 0 })))
                    {
                        idleCts.CancelAfter(BodyIdleTimeout);
                    }

                    if (DegenerateRunLimit > 0)
                    {
                        foreach (var e in events)
                        {
                            var delta = e switch
                            {
                                TextDelta t => t.Delta,
                                ThinkingDelta t => t.Delta,
                                ToolCallDelta t => t.Delta,
                                _ => null,
                            };
                            if (delta is not null && run.Feed(delta) >= DegenerateRunLimit) degenerate = true;
                        }
                        if (degenerate) break; // this chunk's events never reach the caller
                    }

                    foreach (var e in events)
                    {
                        // The gate closes the moment content leaves this iterator, not when the
                        // assembler accumulates it: content the caller never pulled cannot be
                        // duplicated by a resend. Thinking counts as content here; a bare
                        // AssistantStart does not (it exists before a single byte arrived).
                        if (e is TextStart or TextDelta or ThinkingStart or ThinkingDelta
                            or ToolCallStart or ToolCallDelta) callerHasContent = true;
                        yield return e;
                    }
                }

                if (degenerate && !cancellationToken.IsCancellationRequested)
                {
                    // Closing the response is what stops the server generating more of it.
                    response.Dispose();
                    if (callerHasContent)
                    {
                        yield return new AssistantDiscardPartial(assembler.Snapshot() with
                        {
                            Content = [],
                            StopReason = StopReason.Pending,
                            ErrorMessage = null,
                        });
                    }
                    Console.Error.WriteLine($"Degenerate output: {DegenerateRunLimit} consecutive '!' from {model.Id}; dropped the reply, probing the model");
                    bool modelWasSane;
                    try
                    {
                        modelWasSane = await WaitForSaneModelAsync(model, url, apiKey, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        failure = ex;
                        break;
                    }
                    // A sane probe at once ties the run to this request: resend it a bounded number of times.
                    // A model that had to be waited on is a new model; its resend is free.
                    if (modelWasSane && degenerateResendsUsed++ >= DegenerateResends)
                    {
                        failure = new LlmProtocolException(
                            $"Degenerate output: {DegenerateRunLimit} consecutive '!' from {model.Id} on {degenerateResendsUsed} sends of this request, while a short probe was answered sanely",
                            "degenerate_output");
                        break;
                    }
                    assembler = new ChunkAssembler(model);
                    callerHasContent = false;
                    continue;
                }

                if (failure is null && cancellationToken.IsCancellationRequested)
                {
                    failure = new OperationCanceledException(cancellationToken);
                }

                if (failure is null)
                {
                    IReadOnlyList<AssistantMessageEvent> tail;
                    try
                    {
                        tail = assembler.Finish();
                    }
                    catch (Exception ex)
                    {
                        failure = ex;
                        tail = [];
                    }
                    foreach (var e in tail) yield return e;
                    break; // stream completed (or finished badly): one send was enough
                }

                // Failed. Resend only if the body bound fired before any content of this response
                // reached the caller and the policy still allows a retry (transport-class errors
                // like the bound's own HttpRequestException; the backoff is the policy's).
                // The resend gate: the bound must have fired with nothing of the response having
                // reached the caller. "Reached the caller" is tracked explicitly: the caller's
                // consumption is lazy, so by the time this runs the assembler may hold content the
                // caller never saw, and an error from a dead socket is not a content event.
                if (bodyWasBound && !callerHasContent
                    && bodyResendsLeft > 0
                    && RetryPolicy.IsRetryableException(failure!))
                {
                    bodyResendsLeft--;
                    Log(FormattableString.Invariant(
                        $"[model] {model.Id}: {failure!.Message} (request {requestId}); the router could not say it still works on it, sending it again ({Retry.MaxRetries - bodyResendsLeft}/{Retry.MaxRetries})"));
                    try
                    {
                        // The resend wait follows the policy's backoff (the first resend is
                        // immediate: a lost response deserves no penalty), capped at the body
                        // bound so the retry starts well before the next one could fire.
                        var backoff = bodyResendsLeft == Retry.MaxRetries - 1
                            ? TimeSpan.Zero
                            : Retry.GetDelay(Retry.MaxRetries - bodyResendsLeft - 1, null);
                        if (bodyBoundEnabled && backoff > BodyTimeout) backoff = BodyTimeout;
                        await Task.Delay(backoff, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        failure = ex;
                        break;
                    }
                    failure = null;
                    continue;
                }

                // pi-sharp-hh-7: the transport died after partial content reached the caller
                // (connection reset, the body bound, a 5xx surfacing mid-stream). Today that is an
                // error turn that ends the run and costs a run-level resume; instead drop the
                // partial, tell the mirror/tracer the content was discarded, and re-issue the same
                // request with the policy's backoff and retry-after. The retry gate is the same
                // retryability test as the headers leg - transport-class deaths only. A length
                // stop, a content filter and a context overflow finish cleanly (or surface as
                // non-transport errors) and never reach this gate.
                if (callerHasContent && StreamRetries > 0
                    && streamReconnectsUsed < StreamRetries
                    && RetryPolicy.IsRetryableException(failure!))
                {
                    TimeSpan delay;
                    try
                    {
                        // The wait follows the policy's backoff on the reconnect index: ~2/4/8 s
                        // (jittered, capped at the policy's MaxBackoff), and a server-named
                        // retry-after is honoured through the same path as the headers leg.
                        delay = Retry.GetDelay(streamReconnectsUsed, response!.Headers);
                    }
                    catch (RetryDelayTooLongException ex)
                    {
                        failure = ex;
                        break;
                    }
                    // The discard event precedes the wait and any re-issued content: the mirror
                    // and the tracer see the partial's text was dropped before the retry starts,
                    // so the session record and the trace hold no duplicated text.
                    yield return new AssistantDiscardPartial(assembler.Snapshot() with
                    {
                        Content = [],
                        StopReason = StopReason.Pending,
                        ErrorMessage = null,
                    });
                    try
                    {
                        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                        Console.Error.WriteLine($"Reconnecting {streamReconnectsUsed + 1}/{StreamRetries} (stream died after partial content: {failure.Message})");
                    }
                    catch (Exception ex)
                    {
                        failure = ex;
                        break;
                    }
                    // A fresh assembler so the retry accumulates a new assistant message; the
                    // caller's last partial state is the discard event, which cleared it.
                    assembler = new ChunkAssembler(model);
                    callerHasContent = false;
                    streamReconnectsUsed++;
                    failure = null;
                    continue;
                }
                break;
                    } // idleRegistration
                } // idleCts
            }
        }

        if (failure is not null)
        {
            yield return Fail(assembler, failure, cancellationToken);
        }
    }

    /// <summary>
    /// Turns the cancellation of the body-bound token into the exception that names its own bound,
    /// exactly like the headers path does. A token source records cancellation even after it was
    /// disposed, so the diagnosis still works once the iterator's <c>using</c> has run; a caller
    /// <see cref="OperationCanceledException"/> from a dead body read is therefore also mapped when
    /// the bound (not the caller's token) fired it. Passes everything else through unchanged.
    /// </summary>
    private Exception MapBodyTimeout(Exception failure, string url, CancellationTokenSource bodyCts, CancellationToken ct)
    {
        if (bodyCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            return new HttpRequestException($"No SSE progress within {BodyTimeout.TotalSeconds:0}s from {url}");
        }
        return failure;
    }

    /// <summary>
    /// Probes the model until it answers sanely. Returns true when the first probe already did (the model is
    /// fine, the degenerate run belongs to the request), false after a wait. Unbounded: a broken model needs a
    /// person to restart the server, and the seat has nothing better to do than wait for it. Only the caller's
    /// cancellation ends the wait.
    /// </summary>
    private async Task<bool> WaitForSaneModelAsync(ModelConfig model, string url, string? apiKey, CancellationToken ct)
    {
        var body = RequestBuilder.Build(model, new LlmContext("", [new UserMessage(DegenerateProbe.Prompt)], []),
            new StreamOptions { MaxTokens = 16, Reasoning = ThinkingLevel.Off });
        body["stream"] = false;
        body.Remove("stream_options");
        var payload = body.ToJsonString();

        DateTimeOffset? since = null;
        var backoff = DegenerateProbeBackoff;
        for (var probe = 1; ; probe++)
        {
            var (answer, error) = await ProbeAsync(url, payload, apiKey, ct).ConfigureAwait(false);
            if (DegenerateProbe.IsSane(answer))
            {
                if (since is { } s)
                {
                    var line = $"model {model.Id} answers sanely again after {probe - 1} degenerate probe(s); resending the request";
                    Console.Error.WriteLine(line);
                    OnModelHealth?.Invoke(new ModelHealthEvent(true, s, line));
                }
                return since is null;
            }
            var what = error ?? (answer is null ? "an empty answer" : "degenerate output (!!!)");
            if (since is null)
            {
                since = DateTimeOffset.UtcNow;
                OnModelHealth?.Invoke(new ModelHealthEvent(false, since.Value, $"degenerate output (!!!) from {model.Id}"));
            }
            Console.Error.WriteLine($"Model {model.Id} still broken (probe {probe}: {what}); waiting {backoff.TotalSeconds:0}s. It needs a restart on the server side.");
            await Delay(backoff, ct).ConfigureAwait(false);
            backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, DegenerateProbeMaxBackoff.Ticks));
        }
    }

    /// <summary>One non-streaming probe request, bounded at two minutes. Returns the answer, or the failure.</summary>
    private async Task<(string? Answer, string? Error)> ProbeAsync(string url, string payload, string? apiKey, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
            if (!string.IsNullOrEmpty(apiKey)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            foreach (var (name, value) in Headers) request.Headers.TryAddWithoutValidation(name, value);
            using var bounded = CancellationTokenSource.CreateLinkedTokenSource(ct);
            bounded.CancelAfter(TimeSpan.FromMinutes(2));
            using var response = await _http.SendAsync(request, bounded.Token).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync(bounded.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return (null, $"HTTP {(int)response.StatusCode}");
            return (DegenerateProbe.ReadAnswer(text), null);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            return (null, ex.Message);
        }
    }

    private static AssistantError Fail(ChunkAssembler assembler, Exception failure, CancellationToken ct)
    {
        var aborted = ct.IsCancellationRequested;
        var message = aborted ? "Request was aborted" : failure.Message;
        var errorCode = failure switch
        {
            LlmHttpException http => http.ErrorCode,
            LlmProtocolException protocol => protocol.ErrorCode,
            _ => null,
        };
        var providerFailure = !aborted && failure is LlmHttpException or LlmProtocolException or HttpRequestException or IOException or OperationCanceledException;
        return assembler.Fail(aborted ? StopReason.Aborted : StopReason.Error, message, errorCode,
            (failure as LlmHttpException)?.StatusCode, providerFailure);
    }

    /// <summary>
    /// Headers leg of one send. Returns the success response, or (null, failure) once nothing is
    /// left to retry. Never throws for an exhausted retry: the failure object is handed to the
    /// caller, which turns it into an AssistantError. Cancellation still throws - an abort is not
    /// a provider diagnosis, and the caller's own token is what cancelled the enumeration.
    /// </summary>
    private async Task<(HttpResponseMessage?, Exception?, string?)> SendWithRetryAsync(string url, string payload, string? apiKey, WaitNotice wait, CancellationToken ct)
    {
        string? requestId = null;
        for (var attempt = 0; ; attempt++)
        {
            HttpResponseMessage? response = null;
            Exception? transport = null;
            try
            {
                requestId = "pi-" + Guid.NewGuid().ToString("N");
                using var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
                if (!string.IsNullOrEmpty(apiKey)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                foreach (var (name, value) in Headers) request.Headers.TryAddWithoutValidation(name, value);
                request.Headers.TryAddWithoutValidation(RequestIdHeader, requestId);

                using var headersCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                headersCts.CancelAfter(HeadersTimeout);
                using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var lost = RequestProbeEvery > TimeSpan.Zero && ProbeUrl(url, requestId) is { } probeUrl
                    ? RequestLostAsync(probeUrl, apiKey, headersCts, wait, probeCts.Token) : Task.FromResult(false);
                try
                {
                    response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headersCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    if (lost.IsCompletedSuccessfully && lost.Result)
                        throw new HttpRequestException($"{url} no longer had request {requestId}: its answer was lost on the way; sending it again");
                    throw new HttpRequestException($"No response headers within {HeadersTimeout.TotalSeconds:0}s from {url}");
                }
                finally
                {
                    probeCts.Cancel();
                    try { await lost.ConfigureAwait(false); } catch (OperationCanceledException) { }
                }
            }
            catch (HttpRequestException ex)
            {
                transport = ex;
            }

            if (response is { IsSuccessStatusCode: true }) return (response, null, requestId);

            // A router's busy answer: its backend's queue outlasted the wait this caller allows, and the caller can run the
            // turn elsewhere (BusyIsFinal), so it is not sent to the same queue again.
            var busy = BusyIsFinal && response is { StatusCode: System.Net.HttpStatusCode.ServiceUnavailable } && response.Headers.RetryAfter is not null;
            var canRetry = !busy && attempt < Retry.MaxRetries
                && (transport is not null
                    ? RetryPolicy.IsRetryableException(transport)
                    : response is not null && RetryPolicy.IsRetryableStatus(response.StatusCode, response.Headers));

            if (!canRetry)
            {
                ct.ThrowIfCancellationRequested();
                if (transport is not null)
                {
                    return (null, new HttpRequestException($"Request to {url} failed: {transport.Message}", transport), requestId);
                }
                return (null, await HttpErrorAsync(response!, ct).ConfigureAwait(false), requestId);
            }
            Log(FormattableString.Invariant(
                $"[model] request {requestId} to {url}: {(transport is not null ? transport.Message : $"HTTP {(int)response!.StatusCode}")}; sending it again ({attempt + 1}/{Retry.MaxRetries})"));

            TimeSpan delay;
            try
            {
                delay = Retry.GetDelay(attempt, response?.Headers);
            }
            catch (RetryDelayTooLongException)
            {
                response?.Dispose();
                throw;
            }
            response?.Dispose();
            await Task.Delay(delay, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Where a request sent to <c>{base}/chat/completions</c> is asked about: <c>{base}/requests/{id}</c>.</summary>
    internal static string? ProbeUrl(string url, string requestId) =>
        url.EndsWith("/chat/completions", StringComparison.Ordinal) ? url[..^"/chat/completions".Length] + "/requests/" + requestId : null;

    /// <summary>
    /// Asks every <see cref="RequestProbeEvery"/> whether the server still works on the request; true (and the headers wait
    /// cancelled) once it says it finished it, or twice in a row that it does not know it. False, and no more asking, when the
    /// server does not answer the question; an unreachable server is asked again later. A request the router keeps in a
    /// backend's queue (router 1.0.305+: "queued", with its place) is waited on like one it works on; until 2026-10-29 an
    /// answer this client did not know ended the asking.
    /// </summary>
    private async Task<bool> RequestLostAsync(string probeUrl, string? apiKey, CancellationTokenSource headers, WaitNotice wait, CancellationToken ct)
    {
        var unknown = 0;
        while (true)
        {
            await RequestProbeDelay(RequestProbeEvery, ct).ConfigureAwait(false);
            var (answered, state, position) = await AskRouterAsync(probeUrl, apiKey, ct).ConfigureAwait(false);
            if (!answered) continue; // unreachable for now: ask again next time
            if (state is null or not ("working" or "queued" or "finished" or "unknown")) return false;
            if (state is "working" or "queued")
            {
                unknown = 0;
                wait.Tell(state, position);
                continue;
            }
            if (state == "finished" || ++unknown >= 2)
            {
                headers.Cancel();
                return true;
            }
        }
    }

    /// <summary>One question to the router about a request it was sent: (false, ...) when it could not be reached; (true,
    /// state, place in the queue) otherwise, state null when the answer was not the router's.</summary>
    private async Task<(bool Answered, string? State, int? Position)> AskRouterAsync(string probeUrl, string? apiKey, CancellationToken ct,
        TimeSpan? within = null)
    {
        try
        {
            using var probe = new HttpRequestMessage(HttpMethod.Get, probeUrl);
            if (!string.IsNullOrEmpty(apiKey)) probe.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            foreach (var (name, value) in Headers) probe.Headers.TryAddWithoutValidation(name, value);
            using var answerCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            answerCts.CancelAfter(within ?? TimeSpan.FromSeconds(30));
            using var answer = await _http.SendAsync(probe, answerCts.Token).ConfigureAwait(false);
            var text = await answer.Content.ReadAsStringAsync(answerCts.Token).ConfigureAwait(false);
            try
            {
                var json = JsonNode.Parse(text) as JsonObject;
                var state = json?["state"] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
                var position = json?["position"] is JsonValue p && p.TryGetValue<int>(out var n) ? n : (int?)null;
                return (true, state, position);
            }
            catch (System.Text.Json.JsonException) { return (true, null, null); }
        }
        catch (Exception ex) when (ex is HttpRequestException || ex is OperationCanceledException && !ct.IsCancellationRequested)
        {
            return (false, null, null);
        }
    }

    /// <summary>
    /// The body bound of a response that has given the caller nothing yet: a llama.cpp server opens the stream at once and
    /// keeps the request in its own queue, so ten minutes without content can be a queue, not a dead stream (team-lead-2,
    /// 2026-10-29: it gave up at the bound and was sent again to the back of the same queue, eight times, without a line).
    /// The router is asked first; true while it still works on the request or holds it in a backend's queue.
    /// </summary>
    private async Task<bool> StillWorkingAsync(string probeUrl, string? apiKey, string requestId, string model, WaitNotice wait, CancellationToken ct)
    {
        var (answered, state, position) = await AskRouterAsync(probeUrl, apiKey, ct, TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        if (!answered || state is not ("working" or "queued")) return false;
        wait.Tell(state, position);
        Log(FormattableString.Invariant(
            $"[model] {model}: nothing from request {requestId} for {BodyTimeout.TotalMinutes:0} min; the router says {state}{(position is { } at ? $" #{at}" : "")}, so it is waited on, not sent again"));
        return true;
    }

    /// <summary>
    /// <see cref="BodyTimeout"/> for one response: cancels the read when it fires, unless nothing reached the caller yet and
    /// <c>stillWorking</c> says the server still works on the request; then it waits another <see cref="BodyTimeout"/>.
    /// </summary>
    private sealed class BodyBound : IDisposable
    {
        private readonly CancellationTokenSource _body;
        private readonly TimeSpan _after;
        private readonly Func<CancellationToken, Task<bool>>? _stillWorking;
        private readonly CancellationToken _stop;
        private readonly Timer _timer;
        private volatile bool _content;
        private int _asking;

        public BodyBound(CancellationTokenSource body, TimeSpan after, Func<CancellationToken, Task<bool>>? stillWorking, CancellationToken stop)
        {
            _body = body;
            _after = after;
            _stillWorking = stillWorking;
            _stop = stop;
            _timer = new Timer(_ => Fire(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        public void Arm()
        {
            try { _timer.Change(_after, Timeout.InfiniteTimeSpan); }
            catch (ObjectDisposedException) { }
        }

        public void ContentReached() => _content = true;

        private void Fire()
        {
            if (_content || _stillWorking is null) { Cancel(); return; }
            if (Interlocked.Exchange(ref _asking, 1) == 1) return;
            _ = Task.Run(async () =>
            {
                bool working;
                try { working = await _stillWorking(_stop).ConfigureAwait(false); }
                catch (Exception) { working = false; }
                Interlocked.Exchange(ref _asking, 0);
                if (working && !_content && !_stop.IsCancellationRequested) Arm(); else Cancel();
            });
        }

        private void Cancel()
        {
            try { _body.Cancel(); }
            catch (ObjectDisposedException) { }
        }

        public void Dispose() => _timer.Dispose();
    }

    /// <summary>What one request waits on, for <see cref="OnWait"/>: "waiting" once it waited <see cref="WaitNoticeAfter"/>
    /// without content, each answer of the router after that, then "receiving" with what has streamed in so far, at once when
    /// the first content comes to a request that was told and every <see cref="WaitNoticeAfter"/> while it streams, and "done"
    /// when the request ended. A local model that thinks for 40 minutes looked frozen: its seat said it was working on a
    /// message, wrote nothing to its log, and was restarted (pi13, 2026-10-29 23:30 and 00:07; digital-scribe, 2026-10-30:
    /// "2 is also reasonable").</summary>
    private sealed class WaitNotice : IDisposable
    {
        private readonly Action<ModelWait>? _on;
        private readonly string _model;
        private readonly DateTimeOffset _since = DateTimeOffset.UtcNow;
        private readonly Timer? _timer;
        private int _told;
        private int _receiving;
        private long _chars;
        private int _thinking;

        public WaitNotice(Action<ModelWait>? on, string model, TimeSpan after)
        {
            _on = on;
            _model = model;
            if (on is not null && after > TimeSpan.Zero) _timer = new Timer(_ => Tick(), null, after, after);
        }

        private void Tick()
        {
            if (Volatile.Read(ref _receiving) == 1) Tell("receiving", null);
            else if (Volatile.Read(ref _told) == 0) Tell("waiting", null);
        }

        public void Tell(string state, int? position)
        {
            if (_on is null) return;
            Interlocked.Exchange(ref _told, 1);
            try { _on(new ModelWait(_model, _since, state, position, Interlocked.Read(ref _chars), Volatile.Read(ref _thinking) == 1)); }
            catch (Exception) { }
        }

        /// <summary>Characters of an answer came in (text, thinking or a tool call's arguments).</summary>
        public void Receiving(int chars, bool thinking)
        {
            if (chars <= 0) return;
            Interlocked.Add(ref _chars, chars);
            Volatile.Write(ref _thinking, thinking ? 1 : 0);
            // The first content of a request whose wait was told replaces "waiting on" at once, not at the next tick.
            if (Interlocked.Exchange(ref _receiving, 1) == 0 && Volatile.Read(ref _told) == 1) Tell("receiving", null);
        }

        /// <summary>The request ended: a wait that was told is told done.</summary>
        public void Done()
        {
            try { _timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan); }
            catch (ObjectDisposedException) { }
            if (Interlocked.Exchange(ref _told, 0) == 1)
            {
                try { _on?.Invoke(new ModelWait(_model, _since, "done", null)); }
                catch (Exception) { }
            }
        }

        public void Dispose()
        {
            Done();
            _timer?.Dispose();
        }
    }

    /// <summary>The output a server says is left beside the prompt, from its context-length refusal (vLLM: "maximum context
    /// length is W tokens. However, you requested R output tokens and your prompt contains at least P input tokens"), or
    /// null when the message says no such thing or nothing is left.</summary>
    internal static int? OutputRoom(string message)
    {
        var m = System.Text.RegularExpressions.Regex.Match(message,
            @"maximum context length is (\d+) tokens.*?requested (\d+) output tokens.*?prompt contains (?:at least )?(\d+) input tokens",
            System.Text.RegularExpressions.RegexOptions.Singleline);
        if (!m.Success || !long.TryParse(m.Groups[1].Value, out var window) || !long.TryParse(m.Groups[2].Value, out var asked)
            || !long.TryParse(m.Groups[3].Value, out var prompt)) return null;
        var room = window - prompt;
        return room >= 1 && room < asked ? (int)room : null;
    }

    private static async Task<LlmHttpException> HttpErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        string body;
        try
        {
            body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch (Exception)
        {
            body = "";
        }
        finally
        {
            response.Dispose();
        }

        var detail = body.Trim();
        string? errorCode = null;
        try
        {
            if (detail.Length > 0 && JsonNode.Parse(detail) is JsonObject json && json["error"] is { } error)
            {
                detail = ChunkAssembler.DescribeError(error);
                errorCode = ChunkAssembler.ExtractErrorCode(error);
            }
        }
        catch (Exception)
        {
            // keep raw body
        }
        if (detail.Length > 2000) detail = detail[..2000] + "…";

        var status = (int)response.StatusCode;
        var message = detail.Length > 0 ? $"HTTP {status} {response.ReasonPhrase}: {detail}" : $"HTTP {status} {response.ReasonPhrase}";
        return new LlmHttpException(status, message, errorCode);
    }
}

/// <summary>
/// Non-2xx response from the completions endpoint. Carries the machine-readable error code/type
/// parsed out of the JSON error body (<c>error.code</c>/<c>error.type</c>, including the nested
/// <c>error.error.*</c> some proxies use) when present, so callers can recognize a context
/// overflow from the response itself rather than from locally configured numbers.
/// </summary>
public sealed class LlmHttpException(int statusCode, string message, string? errorCode = null) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    /// <summary>Parsed <c>error.code</c> / <c>error.type</c> from the response body, when present.</summary>
    public string? ErrorCode { get; } = errorCode;
}
