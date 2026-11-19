using System.Security.Cryptography.X509Certificates;
using PiSharp.Core.Agent;
using PiSharp.Core.Config;
using PiSharp.Core.Messages;
using PiSharp.Core.Models;
using PiSharp.Llm.Auth;
using PiSharp.Llm.OpenAi;
using PiSharp.Llm.Responses;

namespace PiSharp.Llm;

/// <summary>Dispatch per call, including model changes and compaction; never retry subscriptions with an API key.</summary>
public sealed class SubscriptionRouter : IDisposable
{
    private readonly HttpClient _http;
    /// <summary>
    /// pi-sharp-hh-7: the body-idle bound a completions client gets. Local providers (loopback
    /// and this machine's LAN) are slow, not dead, and the item's 2-3 minute bound is for remote
    /// providers, so only a remote BaseUrl gets one: a loopback/LAN server that only dribbles
    /// keep-alives is a local bug the seat can restart, not a dead remote stream.
    /// </summary>
    internal static TimeSpan IdleBoundFor(string baseUrl)
    {
        var host = new Uri(baseUrl).Host;
        if (host is "localhost" or "127.0.0.1" or "::1" or "[::1]" or "0.0.0.0") return TimeSpan.Zero;
        if (System.Net.IPAddress.TryParse(host, out var ip))
        {
            if (System.Net.IPAddress.IsLoopback(ip)) return TimeSpan.Zero;
            if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                // The 10/8, 172.16/12 and 192.168/16 ranges are where the seats keep their
                // local model servers; anything else is a remote provider.
                var b = ip.GetAddressBytes();
                if (b[0] is 10 or 192 || (b[0] == 172 && (b[1] & 0xF0) == 16)) return TimeSpan.Zero;
            }
        }
        return TimeSpan.FromMinutes(3);
    }
    private readonly Lazy<SubscriptionResponsesClient> _subscription;
    private readonly Func<bool> _brokerEnabled;

    /// <param name="runsSubscription">For a seat whose assignment can change while it runs: whether it is on a
    /// subscription model right now. A seat on its local model may call it; one on a subscription model may not
    /// fall back to it. Null = the broker's presence decides, once.</param>
    public SubscriptionRouter(string? configDir, Uri? broker = null, string? brokerToken = null, X509Certificate2? brokerCa = null,
        Func<bool>? runsSubscription = null)
    {
        var hasBroker = broker is not null;
        _brokerEnabled = runsSubscription ?? (() => hasBroker);
        var handler = new SocketsHttpHandler { AllowAutoRedirect = false, ConnectTimeout = TimeSpan.FromSeconds(15) };
        // The pin applies only while this client talks to the broker. Direct subscription OAuth keeps the system trust store.
        if (broker is not null && brokerCa is not null)
            SubscriptionBrokerTrust.Pin(handler, brokerCa);
        _http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        _subscription = new(() =>
        {
            var store = broker is null ? new SubscriptionConnectionStore(
                Path.Combine(ConfigPaths.GlobalConfigDir(configDir), "subscriptions"), new SubscriptionOAuth(_http)) : null;
            return new(_http, store)
            {
                BrokerUrl = broker, BrokerToken = brokerToken,
                HandBackOnProviderTrouble = HandBackOnProviderTrouble, OnProviderTrouble = OnProviderTrouble,
            };
        });
    }

    /// <summary>See <see cref="SubscriptionResponsesClient.HandBackOnProviderTrouble"/>: the seat can run the turn elsewhere.</summary>
    public Func<bool>? HandBackOnProviderTrouble { get; init; }
    /// <summary>See <see cref="SubscriptionResponsesClient.OnProviderTrouble"/>.</summary>
    public Action<string>? OnProviderTrouble { get; init; }

    /// <summary>Told when the local model starts and stops answering with degenerate output (a run of '!').</summary>
    public Action<OpenAi.ModelHealthEvent>? OnModelHealth { get; init; }

    /// <summary>Told what a local request waits on (<see cref="OpenAiCompletionsClient.OnWait"/>), for the seat's status line.</summary>
    public Action<OpenAi.ModelWait>? OnWait { get; init; }

    /// <summary>
    /// The seat can run a turn its local model fails on its subscription instead (a hybrid seat with room left on it,
    /// <c>SeatAssignmentSwitch.CanFailOver</c>). Then a local request waits at most <see cref="LocalQueueLimit"/> in a router
    /// backend's queue, the router's busy answer is final, and a failed turn is told (<see cref="OnLocalTrouble"/>) rather
    /// than sent to the same model again (digital-scribe, 2026-10-29: "as a fallback... if local models fail repeatedly...
    /// we can use openai sub down to 10% max").
    /// </summary>
    public Func<bool>? FailOverOnLocalTrouble { get; init; }

    /// <summary>Told the failure's code when a local turn failed before any content, on a seat that can fail over.</summary>
    public Action<string>? OnLocalTrouble { get; init; }

    /// <summary>Told when a local turn ended with an answer.</summary>
    public Action? OnLocalAnswer { get; init; }

    /// <summary>The longest a fail-over seat's local request waits in a router backend's queue: a busy local model is a
    /// failure for a seat that has somewhere else to go.</summary>
    public TimeSpan LocalQueueLimit { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>Tests: the HTTP client local requests go through (null = the completions client's shared one).</summary>
    internal HttpClient? LocalHttp { get; init; }

    public StreamFunction Stream => (model, context, options, ct) => model.Provider switch
    {
        "openai-completions" when !_brokerEnabled() => LocalAsync(model, context, options, ct),
        "openai-codex" or "xai" => _subscription.Value.Stream(model, context, options, ct),
        _ => throw new InvalidOperationException("Unsupported provider or forbidden local fallback from a subscription seat."),
    };

    private async IAsyncEnumerable<AssistantMessageEvent> LocalAsync(ModelConfig model, LlmContext context, StreamOptions options,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var canFailOver = FailOverOnLocalTrouble?.Invoke() == true;
        var client = new OpenAiCompletionsClient(LocalHttp)
        {
            Retry = Http.RetryPolicy.Patient,
            BodyIdleTimeout = IdleBoundFor(model.BaseUrl),
            OnModelHealth = OnModelHealth,
            OnWait = OnWait,
            // A seat's local model is a route on the factory router, which says where a request stands.
            AskRouterBeforeResend = true,
            BusyIsFinal = canFailOver,
            Headers = canFailOver
                ? new Dictionary<string, string> { [OpenAiCompletionsClient.QueueTimeoutHeader] = ((long)LocalQueueLimit.TotalMilliseconds).ToString(System.Globalization.CultureInfo.InvariantCulture) }
                : new Dictionary<string, string>(),
        };
        var content = false;
        await foreach (var e in client.Stream(model, context, options, ct).ConfigureAwait(false))
        {
            if (e is AssistantError error && !content && !ct.IsCancellationRequested && error.Reason == StopReason.Error)
            {
                if (canFailOver) OnLocalTrouble?.Invoke(error.Error.ErrorCode ?? (error.Error.ProviderHttpStatus is { } status ? "http_" + status : "local_error"));
            }
            else if (e is AssistantDone) OnLocalAnswer?.Invoke();
            if (e is not AssistantStart and not AssistantError) content = true;
            yield return e;
        }
    }

    public void Dispose() => _http.Dispose();
}
