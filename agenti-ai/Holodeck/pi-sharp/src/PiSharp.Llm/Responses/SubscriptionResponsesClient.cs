// Direct SSE transport port from pi packages/ai/src/api/{openai,openai-codex}-responses.ts @ 71dca871b.
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using PiSharp.Core.Agent;
using PiSharp.Core.Messages;
using PiSharp.Core.Models;
using PiSharp.Llm.Auth;
using PiSharp.Llm.Http;

namespace PiSharp.Llm.Responses;

public sealed class SubscriptionResponsesClient(HttpClient http, ISubscriptionCredentialSource? credentials = null)
{
    /// <summary>The broker's reason for refusing a request itself (no provider was called), e.g. a changed assignment.</summary>
    public const string RefusalHeader = "X-Pi-Refusal";

    public Uri? BrokerUrl { get; init; }
    public string? BrokerToken { get; init; }
    public TimeSpan HeadersTimeout { get; init; } = TimeSpan.FromMinutes(2);
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromMinutes(2);

    public static SubscriptionProvider Provider(string name) => name switch
    {
        "openai-codex" => SubscriptionProvider.Codex, "xai" => SubscriptionProvider.Xai,
        _ => throw new ArgumentException("Unknown subscription provider."),
    };

    public static Uri Endpoint(SubscriptionProvider provider) => new(provider switch
    {
        SubscriptionProvider.Codex => "https://chatgpt.com/backend-api/codex/responses",
        SubscriptionProvider.Xai => "https://api.x.ai/v1/responses",
        _ => throw new ArgumentOutOfRangeException(nameof(provider)),
    });

    public static void Authorize(HttpRequestMessage request, SubscriptionCredential credential)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.AccessToken);
        request.Headers.Accept.ParseAdd("text/event-stream");
        request.Headers.UserAgent.ParseAdd("pi-sharp/0.5");
        if (credential.Provider == SubscriptionProvider.Codex)
        {
            request.Headers.Add("chatgpt-account-id", credential.AccountId);
            request.Headers.Add("originator", "pi-sharp");
            request.Headers.Add("OpenAI-Beta", "responses=experimental");
        }
    }

    /// <summary>Where a retried failure is written (the seat log).</summary>
    public Action<string> Diagnostics { get; init; } = line => Console.Error.WriteLine(line);

    /// <summary>True when the seat can run the turn elsewhere (a hybrid seat's own model). Then a failure on the provider's or
    /// the broker's side is retried only <see cref="HandBackAfter"/> times before the turn is handed back, instead of
    /// waiting through every <see cref="RetryDelays"/> step (about 23 minutes). 2026-10-25: Grok answered "The model is
    /// currently at capacity due to high demand" for over twenty minutes, and four hybrid seats waited on it.</summary>
    public Func<bool>? HandBackOnProviderTrouble { get; init; }
    /// <summary>Retries before a turn is handed back, when <see cref="HandBackOnProviderTrouble"/> says the seat can.</summary>
    public int HandBackAfter { get; init; } = 1;
    /// <summary>Told the failure's code when a turn is handed back, so the seat runs its next turns elsewhere for a while.</summary>
    public Action<string>? OnProviderTrouble { get; init; }

    /// <summary>Waits before each retry of a request that failed before its first token. Empty = never retry. About
    /// 22 minutes in all: a provider that answers "server_is_overloaded" stays so for minutes, and 25 s (the old
    /// 5 s + 20 s) ended 73 seat turns from 10-11 to 10-14, each leaving its seat idle with work in hand until
    /// something woke it. Each wait is spread by +/-10% (Codex's jitter): four Grok seats retried within the same three
    /// seconds on 2026-10-25.</summary>
    public IReadOnlyList<TimeSpan> RetryDelays { get; init; } =
    [
        TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(20), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5),
    ];

    /// <summary>The longest wait between two reconnects when the broker cannot be reached (Codex: 5 s doubling to 60 s,
    /// without limit, "Reconnecting... waiting for network").</summary>
    public TimeSpan MaxReconnectDelay { get; init; } = TimeSpan.FromSeconds(60);
    public TimeSpan FirstReconnectDelay { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Stream retries (Codex's stream_max_retries): a stream that broke before its first token after the broker took
    /// the request is sent again up to this many times, 1 s doubling, apart from <see cref="RetryDelays"/>.</summary>
    public int StreamRetries { get; init; } = 5;
    public TimeSpan FirstStreamRetryDelay { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>The longest Retry-After honoured; a longer one waits this long and tries again.</summary>
    public TimeSpan MaxRetryAfter { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>A failure to reach the broker at all (refused, reset, unreachable): retried until it comes back, apart from
    /// the provider's own failures.</summary>
    public const string ConnectionErrorCode = "subscription_connection_error";

    internal static TimeSpan Jitter(TimeSpan delay) =>
        delay <= TimeSpan.Zero ? delay : TimeSpan.FromMilliseconds(delay.TotalMilliseconds * (0.9 + Random.Shared.NextDouble() * 0.2));

    /// <summary>Failures worth one more try: the provider or the broker in between broke, not the request. Limits,
    /// auth, certificate and cancellation never retry. A broker 502 once ended the turn with the seat's process
    /// (2026-09-27); a request that produced nothing yet can be sent again without replaying anything.</summary>
    /// <summary>The code of a refusal for a spent allowance (HTTP 402 or 429 from the broker).</summary>
    public const string QuotaErrorCode = "subscription_limit_reached";

    public static bool Retriable(string? code) => code is "subscription_server_error" or "subscription_timeout" or "subscription_stream_incomplete"
        || ProviderTransient(code);

    /// <summary>Codes the provider itself reports in a failed stream (response.failed / error event, HTTP 200) for a
    /// failure on its side: "Subscription provider returned an error." Seen on the seats 10-11..10-14:
    /// server_is_overloaded 43, response_failed 25, server_error 3. Quota, auth, invalid requests and context length
    /// are not here: sending the same request again cannot help those.</summary>
    public static bool ProviderTransient(string? code) => code is "server_is_overloaded" or "server_error" or "response_failed"
        or "internal_error" or "service_unavailable" or "overloaded" or "engine_overloaded";

    /// <summary>The header a seat passes its cache key to the broker in; the broker maps it to the provider's own.</summary>
    public const string CacheKeyHeader = "X-Pi-Cache-Key";

    /// <summary>A key the providers accept: the Codex backend refuses a session id over 64 characters.</summary>
    public static bool ValidCacheKey(string? key) => key is { Length: > 0 and <= 64 } && key.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    /// <summary>Routes the request to the conversation's prompt cache. The Codex backend keys its cache affinity on
    /// the session_id header, not on prompt_cache_key alone: without it every request got a fresh cache (luna seats
    /// hit 4-7% cached input, 2026-09-27, against 85% on grok). xAI keys it on x-grok-conv-id.</summary>
    public static void CacheAffinity(HttpRequestMessage request, SubscriptionProvider provider, string? key)
    {
        if (!ValidCacheKey(key)) return;
        if (provider == SubscriptionProvider.Codex) request.Headers.TryAddWithoutValidation("session_id", key);
        else request.Headers.TryAddWithoutValidation("x-grok-conv-id", key);
    }

    public async IAsyncEnumerable<AssistantMessageEvent> Stream(ModelConfig model, LlmContext context, StreamOptions options,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        int reconnects = 0, streamRetries = 0;
        for (var attempt = 0; ; attempt++)
        {
            var content = false;
            AssistantError? failed = null;
            var info = new AttemptInfo();
            await foreach (var evt in StreamOnce(model, context, options, attempt == 0 && reconnects == 0 && streamRetries == 0, info, ct).ConfigureAwait(false))
            {
                if (evt is AssistantError error && !content && !ct.IsCancellationRequested
                    && (error.Error.ErrorCode == ConnectionErrorCode || StreamDropped(info, error.Error.ErrorCode) && streamRetries < StreamRetries
                        || attempt < RetryDelays.Count && Retriable(error.Error.ErrorCode)
                        || error.Error.ErrorCode == QuotaErrorCode && HandBackOnProviderTrouble?.Invoke() == true))
                { failed = error; break; }
                if (evt is not AssistantStart and not AssistantError) content = true;
                yield return evt;
            }
            if (failed is null) yield break;
            TimeSpan wait;
            if (failed.Error.ErrorCode == QuotaErrorCode && HandBackOnProviderTrouble?.Invoke() == true)
            {
                // A spent allowance does not come back by retrying: a seat that can run the turn on its own model does so at
                // once (pi13, 2026-10-27: Grok's weekly allowance ran out at 07:06Z and each resume went to xai again).
                Diagnostics(FormattableString.Invariant($"[model] {model.Id}: {failed.Error.ErrorMessage ?? QuotaErrorCode}; handing the turn back to run elsewhere"));
                OnProviderTrouble?.Invoke(QuotaErrorCode);
                yield return failed;
                yield break;
            }
            if (failed.Error.ErrorCode == ConnectionErrorCode && HandBackOnProviderTrouble?.Invoke() != true)
            {
                // Codex's unbounded connection retries: the broker or the box's forward is down, not the request; a seat with
                // nowhere else to run waits for it, saying so, and does not spend its retries or fail the turn.
                wait = Jitter(TimeSpan.FromMilliseconds(Math.Min(MaxReconnectDelay.TotalMilliseconds,
                    FirstReconnectDelay.TotalMilliseconds * Math.Pow(2, Math.Min(reconnects, 16)))));
                reconnects++;
                attempt--; // a reconnect is not a retry of the request
                Diagnostics(FormattableString.Invariant(
                    $"[model] {model.Id}: reconnecting to the subscription broker ({reconnects}) in {wait.TotalSeconds:0}s: {failed.Error.ErrorMessage}"));
            }
            else if (StreamDropped(info, failed.Error.ErrorCode) && streamRetries < StreamRetries)
            {
                // Codex keeps stream retries apart from request retries: the request was taken and its stream broke before
                // the first token, so it is sent again soon, without spending the request's (long) retries.
                wait = Jitter(TimeSpan.FromMilliseconds(FirstStreamRetryDelay.TotalMilliseconds * Math.Pow(2, streamRetries)));
                streamRetries++;
                attempt--;
                Diagnostics(FormattableString.Invariant(
                    $"[model] {model.Id}: {failed.Error.ErrorMessage ?? failed.Error.ErrorCode} (stream retry {streamRetries} of {StreamRetries}); retrying in {wait.TotalSeconds:0.#}s"));
            }
            else if (attempt >= HandBackAfter && HandBackOnProviderTrouble?.Invoke() == true)
            {
                var code = failed.Error.ErrorCode ?? "provider_error";
                Diagnostics(FormattableString.Invariant(
                    $"[model] {model.Id}: {failed.Error.ErrorMessage ?? code} (attempt {attempt + 1}); handing the turn back to run elsewhere"));
                OnProviderTrouble?.Invoke(code);
                yield return failed;
                yield break;
            }
            else
            {
                // Every retried failure goes to the seat log with the provider's words: the retries of one request can take
                // twenty minutes, and a seat that only shows its last error looks frozen meanwhile (2026-10-25).
                if (attempt >= RetryDelays.Count) { yield return failed; yield break; }
                // Retry-After first (Codex): the server's own word on when to come back, within MaxRetryAfter.
                wait = info.RetryAfter is { } asked && asked > TimeSpan.Zero ? (asked < MaxRetryAfter ? asked : MaxRetryAfter) : Jitter(RetryDelays[attempt]);
                Diagnostics(FormattableString.Invariant(
                    $"[model] {model.Id}: {failed.Error.ErrorMessage ?? failed.Error.ErrorCode} (attempt {attempt + 1} of {RetryDelays.Count + 1}); retrying in {wait.TotalSeconds:0}s{(info.RetryAfter is not null ? " (Retry-After)" : "")}"));
            }
            var waited = true;
            try { await Task.Delay(wait, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { waited = false; }
            if (!waited) { yield return failed; yield break; }
        }
    }

    /// <summary>What one attempt learned besides its events: whether the broker answered (the stream had started) and
    /// the Retry-After it gave.</summary>
    private sealed class AttemptInfo
    {
        public bool Answered;
        public TimeSpan? RetryAfter;
    }

    /// <summary>The stream broke after the broker answered and before it completed: idle timeout or an early end.</summary>
    private static bool StreamDropped(AttemptInfo info, string? code) =>
        info.Answered && code is "subscription_stream_incomplete" or "subscription_timeout";

    private async IAsyncEnumerable<AssistantMessageEvent> StreamOnce(ModelConfig model, LlmContext context, StreamOptions options,
        bool announce, AttemptInfo info, [EnumeratorCancellation] CancellationToken ct)
    {
        var assembler = new ResponsesAssembler(model);
        if (announce) yield return new AssistantStart(assembler.Snapshot());
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        HttpResponseMessage? response = null;
        try
        {
            deadline.CancelAfter(HeadersTimeout);
            var provider = Provider(model.Provider);
            var body = ResponsesRequest.Build(model, context, options);
            using var request = new HttpRequestMessage(HttpMethod.Post, BrokerUrl is null ? Endpoint(provider) : new Uri(BrokerUrl, "responses"))
            { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
            if (BrokerUrl is not null)
            {
                ValidateBroker(BrokerUrl);
                if (string.IsNullOrWhiteSpace(BrokerToken)) throw new SubscriptionOAuthException("Factory identity is required for the subscription broker.");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", BrokerToken);
                request.Headers.Add("X-Pi-Provider", model.Provider);
                request.Headers.Add("X-Pi-Connection", model.Connection);
                if (ValidCacheKey(options.CacheKey)) request.Headers.Add(CacheKeyHeader, options.CacheKey);
            }
            else
            {
                if (credentials is null || string.IsNullOrWhiteSpace(model.Connection))
                    throw new SubscriptionOAuthException("A named subscription connection is required.");
                Authorize(request, await credentials.GetAsync(model.Connection, provider, deadline.Token).ConfigureAwait(false));
                CacheAffinity(request, provider, options.CacheKey);
            }
            response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            info.Answered = response.IsSuccessStatusCode;
            if (!response.IsSuccessStatusCode)
            {
                info.RetryAfter = PiSharp.Llm.Http.RetryPolicy.ParseRetryAfter(response.Headers);
                var code = (int)response.StatusCode >= 500 ? "subscription_server_error" : "subscription_http_error";
                if ((int)response.StatusCode is 401 or 403) code = "subscription_reconnect_required";
                if ((int)response.StatusCode is 402 or 429) code = QuotaErrorCode;
                var quotaReason = response.Headers.TryGetValues("X-RateLimit-Reason", out var reasons)
                    ? reasons.FirstOrDefault(v => v == "quota") : null;
                var refusal = response.Headers.TryGetValues(RefusalHeader, out var refusals) ? refusals.FirstOrDefault() : null;
                assembler.Fail(code, refusal is { Length: > 0 }
                        ? $"Subscription request refused by the broker (HTTP {(int)response.StatusCode}): {refusal}."
                        : $"Subscription request stopped (HTTP {(int)response.StatusCode}). Check account access or allowance; no paid fallback was attempted.",
                    httpStatus: (int)response.StatusCode, providerRequestFailed: true, rateLimitReason: quotaReason);
            }
        }
        catch (Exception ex) { Fail(assembler, ex, ct); }
        using (response)
        {
            if (response is not null && !assembler.Terminal)
            {
                IAsyncEnumerator<string>? reader = null;
                try
                {
                    deadline.CancelAfter(IdleTimeout);
                    var stream = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
                    reader = SseReader.ReadDataAsync(stream, deadline.Token).GetAsyncEnumerator();
                }
                catch (Exception ex) { Fail(assembler, ex, ct); }
                if (reader is not null)
                {
                    await using (reader)
                    {
                        while (!assembler.Terminal)
                        {
                            IReadOnlyList<AssistantMessageEvent> events = [];
                            bool hasNext = false;
                            try
                            {
                                deadline.CancelAfter(IdleTimeout);
                                hasNext = await reader.MoveNextAsync().ConfigureAwait(false);
                                if (hasNext) events = assembler.Push(JsonNode.Parse(reader.Current)!.AsObject());
                            }
                            catch (Exception ex) { Fail(assembler, ex, ct); }
                            foreach (var evt in events) yield return evt;
                            if (!hasNext) break;
                        }
                    }
                }
            }
        }
        if (!assembler.Terminal) assembler.Fail("subscription_stream_incomplete", "Subscription stream ended before completion.");
        if (assembler.Reason is StopReason.Error or StopReason.Aborted)
            yield return new AssistantError(assembler.Reason, assembler.Snapshot());
        else yield return new AssistantDone(assembler.Reason, assembler.Snapshot());
    }

    public static void ValidateBroker(Uri uri)
    {
        if (!uri.IsAbsoluteUri || (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)) || uri.UserInfo.Length > 0)
            throw new ArgumentException("Subscription broker requires HTTPS (HTTP allowed only on loopback).");
    }

    /// <summary>A failure to connect (refused, reset, unreachable, name not found), not an answer the broker gave.</summary>
    internal static bool IsConnectionFailure(Exception exception)
    {
        for (var e = exception; e is not null; e = e.InnerException)
            if (e is System.Net.Sockets.SocketException or IOException { InnerException: System.Net.Sockets.SocketException }) return true;
        return exception is HttpRequestException { HttpRequestError: HttpRequestError.ConnectionError or HttpRequestError.NameResolutionError };
    }

    private static void Fail(ResponsesAssembler assembler, Exception exception, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) assembler.Fail("cancelled", "Subscription request cancelled.", aborted: true);
        else if (exception is SubscriptionOAuthException) assembler.Fail("subscription_auth_error", exception.Message);
        else if (exception is OperationCanceledException) assembler.Fail("subscription_timeout", "Subscription request timed out.", providerRequestFailed: true);
        else if (exception is HttpRequestException { StatusCode: null } && IsConnectionFailure(exception))
            assembler.Fail(ConnectionErrorCode, "The subscription broker could not be reached: " + exception.Message, providerRequestFailed: true);
        // The cause goes in the message: "check connection configuration" alone sent demo-03's failures (2026-10-30, a part of
        // .NET missing under a running seat) to the broker and the provider, which had answered every call.
        else assembler.Fail("subscription_protocol_error", $"Subscription request failed ({Cause(exception)}). Check connection configuration and provider availability.",
            providerRequestFailed: exception is HttpRequestException or IOException or System.Text.Json.JsonException);
    }

    /// <summary>The exception's type and first line, short enough for the seat's log and status.</summary>
    internal static string Cause(Exception exception)
    {
        var line = exception.Message.Split('\n', 2)[0].Trim();
        if (line.Length > 240) line = line[..240] + "...";
        return line.Length == 0 ? exception.GetType().Name : $"{exception.GetType().Name}: {line}";
    }
}
