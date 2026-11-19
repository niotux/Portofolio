// Measurement layer for the per-turn prompt cache, shaped after the reference pi's cache-stats.ts.
// Scoped by adr-pi-sharp-cache-stats: miss detection per assistant turn, a noise floor, a sticky
// "reported" flag for providers that never report prompt_tokens_details, and a TTL on the idle
// window. No prompt-shape, model or compactor change - numbers only.
namespace PiSharp.Llm;

/// <summary>One cache-stats observation for an assistant turn.</summary>
public sealed record CacheStatsSample(
    int Turn,
    int MissedTokens,
    long IdleMs,
    bool ModelChanged,
    bool Reported,
    bool CacheMiss)
{
    /// <summary>
    /// Builds the one-line record the seat writes to the cubicle:
    /// <c>cache_miss seat=&lt;id&gt; host=&lt;.101|.104&gt; missed=&lt;tokens&gt; idle=&lt;ms&gt;
    /// model_changed=&lt;bool&gt; reported=&lt;bool&gt;</c>.
    /// </summary>
    public string FormatLine(string seat, string host) =>
        $"cache_miss seat={seat} host={host} missed={MissedTokens} idle={IdleMs} " +
        $"model_changed={ModelChanged.ToString().ToLowerInvariant()} reported={Reported.ToString().ToLowerInvariant()}";
}

/// <summary>
/// Tracks prompt-cache health turn by turn. <see cref="Record"/> is called once per assistant turn
/// with that turn's reported cache-read token count; it measures how much of the context was NOT
/// served from the provider's prompt cache and how long the seat sat idle before the turn.
/// </summary>
public sealed class CacheStats
{
    /// <summary>
    /// A reported cache read below this floor is noise, not a hit: tiny reads are reported even when
    /// the provider serves nothing meaningful from cache.
    /// </summary>
    public const int NoiseFloorTokens = 1024;

    /// <summary>
    /// The reference pi's idle window beyond which the provider's prompt cache is assumed cold.
    /// Recorded as an assumption: llama.cpp and vLLM do not document a TTL (adr-pi-sharp-cache-stats).
    /// </summary>
    public const int CacheTtlMs = 5 * 60 * 1000;

    private readonly Func<DateTimeOffset> _clock;
    private readonly object _gate = new();
    private string? _lastModel;
    private DateTimeOffset? _lastCallAt;
    private int _turns;
    private readonly Queue<CacheStatsSample> _samples = new();

    public CacheStats(Func<DateTimeOffset>? clock = null)
    {
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Sticky: once any turn reports cache activity, <see cref="Reported"/> stays true for the process
    /// lifetime. Keeps a vLLM/llama.cpp seat (no prompt_tokens_details) from reading as 100% miss:
    /// the seat simply does not report, which is not the same as missing.
    /// </summary>
    public bool Reported { get; private set; }

    /// <summary>The latest observation; the CLI reads it to format the seat's <c>cache_miss</c> line. Null before the first turn.</summary>
    public CacheStatsSample? CurrentLine { get; private set; }

    /// <summary>Observations so far, oldest first. Measurement only: nothing is aggregated here.</summary>
    public IReadOnlyList<CacheStatsSample> Samples { get { lock (_gate) return _samples.ToArray(); } }

    /// <summary>
    /// Records one assistant turn. <paramref name="cacheReadTokens"/> is what the provider reported as
    /// a cache read (0 when it reports no prompt_tokens_details at all); <paramref name="contextTokens"/>
    /// is the size of the context that call sent; <paramref name="modelId"/> is the model id the call
    /// used, for change detection between turns.
    /// </summary>
    public CacheStatsSample Record(int cacheReadTokens, int contextTokens, string? modelId)
    {
        lock (_gate)
        {
            var now = _clock();
            var idleMs = _lastCallAt is { } last
                ? (long)Math.Max(0, (now - last).TotalMilliseconds)
                : 0;
            _lastCallAt = now;

            var modelChanged = _lastModel is { } prior
                ? !string.Equals(prior, modelId, StringComparison.Ordinal)
                : modelId is not null;
            _lastModel = modelId;

            var hit = cacheReadTokens >= NoiseFloorTokens;
            if (hit) Reported = true;
            var missed = hit ? Math.Max(0, contextTokens - cacheReadTokens) : contextTokens;
            var sample = new CacheStatsSample(
                Turn: ++_turns,
                MissedTokens: missed,
                IdleMs: idleMs,
                ModelChanged: modelChanged,
                Reported: Reported,
                CacheMiss: !hit || idleMs > CacheTtlMs);
            _samples.Enqueue(sample);
            if (_samples.Count > 1024) _samples.Dequeue();
            CurrentLine = sample;
            return sample;
        }
    }
}
