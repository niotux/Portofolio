// Ported from pi: packages/ai/src/utils/provider-retry.ts @ 71dca871b
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;

namespace PiSharp.Llm.Http;

/// <summary>Decides whether a failed HTTP response should be retried and how long to wait.</summary>
public sealed class RetryPolicy
{
    public static readonly RetryPolicy None = new() { MaxRetries = 0 };

    /// <summary>
    /// Local-server default: two retries on 429/5xx/transport reset. pi's client default is 0;
    /// Phase 7 turns this on because llama.cpp and vLLM flake under load.
    /// </summary>
    public static readonly RetryPolicy Default = new() { MaxRetries = 2 };

    /// <summary>
    /// What the CLI uses. Local models are slow, not broken: a busy shared server answering 5xx, a dropped
    /// connection or a header timeout is waited out rather than ending the run (@digital-scribe, 2026-09-15:
    /// "if you see timeouts we increase those timeouts"). Backoff grows to two minutes per attempt, and a
    /// server may ask for up to fifteen.
    /// </summary>
    public static readonly RetryPolicy Patient = new()
    {
        MaxRetries = 8,
        MaxBackoff = TimeSpan.FromMinutes(2),
        MaxRetryDelay = TimeSpan.FromMinutes(15),
    };

    /// <summary>Number of retries after the first attempt. pi's default is 0.</summary>
    public int MaxRetries { get; init; }

    /// <summary>Upper bound for a server-requested delay; larger values fail immediately.</summary>
    public TimeSpan MaxRetryDelay { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Cap on the exponential backoff used when the server names no delay.</summary>
    public TimeSpan MaxBackoff { get; init; } = TimeSpan.FromSeconds(8);

    public Random Random { get; init; } = Random.Shared;

    public static bool IsRetryableStatus(HttpStatusCode status, HttpResponseHeaders? headers = null)
    {
        if (headers is not null && headers.TryGetValues("x-should-retry", out var values))
        {
            var flag = values.FirstOrDefault();
            if (string.Equals(flag, "true", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(flag, "false", StringComparison.OrdinalIgnoreCase)) return false;
        }

        var code = (int)status;
        return code is 408 or 409 or 429 || code >= 500;
    }

    /// <summary>Transport failures worth retrying (connection reset, timeout before headers).</summary>
    public static bool IsRetryableException(Exception exception) => exception switch
    {
        RetryDelayTooLongException => false,
        OperationCanceledException => false,
        HttpRequestException => true,
        IOException => true,
        _ => exception.InnerException is not null && IsRetryableException(exception.InnerException),
    };

    /// <summary>
    /// Delay before attempt <paramref name="attempt"/> (0-based retry index). Honors
    /// <c>retry-after-ms</c> and <c>retry-after</c> when present.
    /// </summary>
    /// <exception cref="RetryDelayTooLongException">The server asked for more than <see cref="MaxRetryDelay"/>.</exception>
    public TimeSpan GetDelay(int attempt, HttpResponseHeaders? headers)
    {
        var requested = ParseRetryAfter(headers);
        if (requested is { } delay)
        {
            if (delay > MaxRetryDelay)
            {
                throw new RetryDelayTooLongException(
                    $"Server requested {Math.Round(delay.TotalSeconds)}s retry delay (max: {Math.Round(MaxRetryDelay.TotalSeconds)}s). Try again later.");
            }
            return delay;
        }

        var seconds = Math.Min(0.5 * Math.Pow(2, attempt), MaxBackoff.TotalSeconds);
        var jitter = 1 - Random.NextDouble() * 0.25;
        return TimeSpan.FromSeconds(seconds * jitter);
    }

    public static TimeSpan? ParseRetryAfter(HttpResponseHeaders? headers)
    {
        if (headers is null) return null;

        if (headers.TryGetValues("retry-after-ms", out var msValues)
            && double.TryParse(msValues.FirstOrDefault(), NumberStyles.Float, CultureInfo.InvariantCulture, out var ms)
            && ms >= 0)
        {
            return TimeSpan.FromMilliseconds(ms);
        }

        if (headers.RetryAfter is { } retryAfter)
        {
            if (retryAfter.Delta is { } delta) return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
            if (retryAfter.Date is { } date)
            {
                var wait = date - DateTimeOffset.UtcNow;
                return wait < TimeSpan.Zero ? TimeSpan.Zero : wait;
            }
        }

        if (headers.TryGetValues("retry-after", out var rawValues))
        {
            var raw = rawValues.FirstOrDefault();
            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var s) && s >= 0)
            {
                return TimeSpan.FromSeconds(s);
            }
        }

        return null;
    }
}

public sealed class RetryDelayTooLongException(string message) : Exception(message);
