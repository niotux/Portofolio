using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace PiSharp.Core.Factory;

/// <summary>One thing a seat did, as the hub keeps it (kb <c>POST /seat/events</c>): a model request, a tool call, a guard
/// decision, a turn's file changes or a compaction. The hub stamps the actor from the token and the epic the seat holds at the time.</summary>
public sealed record SeatEvent(DateTime At, string Kind, string? Name = null, bool? Ok = null, int? DurationMs = null,
    long TokensIn = 0, long TokensOut = 0, long TokensCached = 0, JsonObject? Detail = null)
{
    public const string Request = "request";
    public const string Tool = "tool";
    public const string Guard = "guard";
    public const string TurnDiff = "turn_diff";
    public const string Compaction = "compaction";
    /// <summary>A hybrid seat's turns moved to its subscription because its local model failed repeatedly (2026-10-29).</summary>
    public const string Fallback = "fallback";

    public JsonObject ToJson()
    {
        var json = new JsonObject { ["at"] = At.ToUniversalTime().ToString("O"), ["kind"] = Kind };
        if (Name is not null) json["name"] = Name;
        if (Ok is { } ok) json["ok"] = ok;
        if (DurationMs is { } ms) json["duration_ms"] = ms;
        if (TokensIn > 0) json["tokens_in"] = TokensIn;
        if (TokensOut > 0) json["tokens_out"] = TokensOut;
        if (TokensCached > 0) json["tokens_cached"] = TokensCached;
        if (Detail is not null) json["detail"] = Detail.DeepClone();
        return json;
    }
}

/// <summary>
/// Codex's telemetry (plan-pi-sharp-codex item 20; digital-scribe, 2026-10-25: "have all fancy ideas from there implmenented
/// in our pi-sharp"): what the seat does goes to the hub as events, so each seat's requests, tool calls, guard decisions and
/// turn diffs can be read there (<c>kb_observe view=telemetry</c>) and the epic's record keeps its turn diffs. Events queue in
/// memory and are sent every <see cref="FlushEvery"/> in batches; a hub that cannot be reached keeps them for the next try, up
/// to <see cref="MaxQueued"/>, oldest dropped first. Never blocks or fails the seat.
/// </summary>
public sealed class SeatTelemetry : IAsyncDisposable
{
    public const int MaxQueued = 2_000;
    public const int BatchSize = 200;
    public static readonly TimeSpan FlushEvery = TimeSpan.FromSeconds(30);

    private readonly Func<IReadOnlyList<SeatEvent>, CancellationToken, Task<bool>> _send;
    private readonly LinkedList<SeatEvent> _queue = new();
    private readonly object _gate = new();
    private readonly SemaphoreSlim _flushing = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private Task? _loop;
    private long _dropped, _sent;

    public SeatTelemetry(Func<IReadOnlyList<SeatEvent>, CancellationToken, Task<bool>> send) => _send = send;

    /// <summary>Sends to the hub's <c>/seat/events</c> under the seat's own token.</summary>
    public static SeatTelemetry ForHub(string kbUrl, string token, HttpMessageHandler? handler = null)
    {
        var http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        http.BaseAddress = new Uri(new Uri(kbUrl).GetLeftPart(UriPartial.Authority));
        http.Timeout = TimeSpan.FromSeconds(20);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        async Task<int> PostAsync(IEnumerable<SeatEvent> events, CancellationToken ct)
        {
            var body = new JsonObject { ["events"] = new JsonArray([.. events.Select(e => (JsonNode)e.ToJson())]) };
            using var response = await http.PostAsync("/seat/events", new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"), ct).ConfigureAwait(false);
            return (int)response.StatusCode;
        }
        return new SeatTelemetry(async (events, ct) =>
        {
            var status = await PostAsync(events, ct).ConfigureAwait(false);
            // A hub older than this seat (1.0.285) refuses a batch holding a kind it does not know yet: the batch goes again
            // with the kinds every hub takes. A batch refused as malformed would be refused again: dropped, not retried.
            if (status == 400 && events.Any(e => !FirstKinds.Contains(e.Kind)) && events.Any(e => FirstKinds.Contains(e.Kind)))
                status = await PostAsync(events.Where(e => FirstKinds.Contains(e.Kind)), ct).ConfigureAwait(false);
            return status is >= 200 and < 300 or 400;
        });
    }

    /// <summary>The kinds every hub with /seat/events takes (kb 1.0.304).</summary>
    public static readonly IReadOnlySet<string> FirstKinds = new HashSet<string>(StringComparer.Ordinal)
        { SeatEvent.Request, SeatEvent.Tool, SeatEvent.Guard, SeatEvent.TurnDiff };

    public Action<string> Log { get; init; } = _ => { };
    public int Queued { get { lock (_gate) return _queue.Count; } }
    public long Dropped => Interlocked.Read(ref _dropped);
    public long Sent => Interlocked.Read(ref _sent);

    public void Record(SeatEvent e)
    {
        lock (_gate)
        {
            _queue.AddLast(e);
            while (_queue.Count > MaxQueued) { _queue.RemoveFirst(); _dropped++; }
        }
    }

    /// <summary>Starts the background sender.</summary>
    public SeatTelemetry Start()
    {
        _loop ??= Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(FlushEvery);
            try { while (await timer.WaitForNextTickAsync(_stop.Token).ConfigureAwait(false)) await FlushAsync(_stop.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        });
        return this;
    }

    /// <summary>Sends everything queued, a batch at a time; a batch that fails goes back to the front and the flush stops.</summary>
    public async Task FlushAsync(CancellationToken ct = default)
    {
        if (!await _flushing.WaitAsync(0, ct).ConfigureAwait(false)) return;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                List<SeatEvent> batch;
                lock (_gate)
                {
                    batch = [.. _queue.Take(BatchSize)];
                    for (var i = 0; i < batch.Count; i++) _queue.RemoveFirst();
                }
                if (batch.Count == 0) return;
                bool ok;
                try { ok = await _send(batch, ct).ConfigureAwait(false); }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or InvalidOperationException)
                {
                    ok = false;
                    Log($"[telemetry] not sent yet ({batch.Count} events kept): {ex.Message}");
                }
                if (ok) { Interlocked.Add(ref _sent, batch.Count); continue; }
                lock (_gate)
                {
                    for (var i = batch.Count - 1; i >= 0; i--) _queue.AddFirst(batch[i]);
                    while (_queue.Count > MaxQueued) { _queue.RemoveFirst(); _dropped++; }
                }
                return;
            }
        }
        finally { _flushing.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        if (_loop is not null) await _loop.ConfigureAwait(false);
        using var last = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await FlushAsync(last.Token).ConfigureAwait(false);
        _stop.Dispose();
    }
}
