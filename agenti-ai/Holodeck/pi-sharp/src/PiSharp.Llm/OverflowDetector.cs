// Ported from pi: packages/ai/src/utils/overflow.ts @ 71dca871b
//
// Recognition order (wi-8): the parsed machine-readable signal comes first, the response's own
// usage counters second, free-text patterns last. The counter test is gated on the locally
// configured context window, so it is a fallback, not the primary signal: when the window is
// misconfigured (too large) the counters can never fire and recognition must come from the
// provider's own answer. That is why the parsed error code leads.
using System.Text.RegularExpressions;
using PiSharp.Core.Messages;

namespace PiSharp.Llm;

/// <summary>Recognizes "context window exceeded" failures so the caller can compact and retry.</summary>
public static class OverflowDetector
{
    /// <summary>
    /// Machine-readable error codes and types that mean "your prompt did not fit", as reported in
    /// the <c>error.code</c>/<c>error.type</c> of an OpenAI-compatible error body (OpenAI, vLLM,
    /// llama.cpp, and most proxies). Compared case-insensitively.
    /// </summary>
    public static readonly HashSet<string> OverflowCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "context_length_exceeded",
        "context_window_exceeded",
        "context_window_size_exceeded",
        "max_context_length_exceeded",
        "context_length",
        "model_context_window_exceeded",
        "input_length_too_long",
        "prompt_too_long",
        "request_too_large",
    };

    private static readonly Regex[] OverflowPatterns =
    [
        // llama.cpp server, verbatim: this is what a real 400 from server 8082 says, and until it
        // was added a run that overflowed against llama.cpp died unrecognised (wi-8 / run 4638).
        new(@"context size has been exceeded", RegexOptions.IgnoreCase),
        new(@"prompt is too long", RegexOptions.IgnoreCase),
        new(@"exceeds the available context size", RegexOptions.IgnoreCase), // llama.cpp, prompt too long
        new(@"context size has been exceeded", RegexOptions.IgnoreCase), // llama.cpp, generation reached n_ctx
        new(@"exceeds the context window", RegexOptions.IgnoreCase),
        new(@"exceeds the model's maximum context length", RegexOptions.IgnoreCase),
        new(@"maximum context length is \d+ tokens", RegexOptions.IgnoreCase),
        new(@"context[_ ]length[_ ]exceeded", RegexOptions.IgnoreCase),
        new(@"context window exceeded", RegexOptions.IgnoreCase),
        new(@"input is too long for requested model", RegexOptions.IgnoreCase),
        new(@"too many tokens", RegexOptions.IgnoreCase),
        new(@"token limit exceeded", RegexOptions.IgnoreCase),
        new(@"input token count.*exceeds", RegexOptions.IgnoreCase),
        new(@"request too large", RegexOptions.IgnoreCase),
        new(@"exceeds the limit of \d+ tokens", RegexOptions.IgnoreCase),
        new(@"reduce the length of the messages", RegexOptions.IgnoreCase),
    ];

    private static readonly Regex[] NonOverflowPatterns =
    [
        new(@"^(Throttling error|Service unavailable):", RegexOptions.IgnoreCase),
        new(@"rate limit", RegexOptions.IgnoreCase),
        new(@"too many requests", RegexOptions.IgnoreCase),
    ];

    /// <summary>
    /// Free-text recognition only. Kept for callers that have nothing but an error string; the
    /// machine-readable code is the primary signal and is checked in <see cref="IsContextOverflow"/>.
    /// </summary>
    public static bool IsOverflowErrorMessage(string? message, IReadOnlyList<string>? extraPatterns = null)
    {
        if (string.IsNullOrEmpty(message)) return false;
        if (NonOverflowPatterns.Any(p => p.IsMatch(message))) return false;
        if (OverflowPatterns.Any(p => p.IsMatch(message))) return true;
        if (extraPatterns is { Count: > 0 })
        {
            foreach (var pattern in extraPatterns)
            {
                if (string.IsNullOrWhiteSpace(pattern)) continue;
                if (Matches(pattern, message)) return true;
            }
        }
        return false;
    }

    /// <summary>
    /// True when the assistant message indicates the prompt did not fit. Decided in order:
    /// the parsed provider error code, then the response's own usage counters against the
    /// configured window, then (only for a free-text error with no code) the pattern fallback.
    /// <paramref name="extraPatterns"/> are model-specific patterns from models.json.
    /// </summary>
    public static bool IsContextOverflow(AssistantMessage message, int? contextWindow = null, IReadOnlyList<string>? extraPatterns = null)
    {
        if (message.StopReason == StopReason.Error)
        {
            // 1. Machine-readable signal: the provider told us, in a field, what went wrong.
            if (IsOverflowErrorCode(message.ErrorCode)) return true;
            // 2. A known code that is explicitly *not* overflow decides before we read tea leaves.
            if (!string.IsNullOrEmpty(message.ErrorCode)) return IsOverflowErrorMessage(message.ErrorMessage, extraPatterns);
            // 3. No code at all: last resort, the free-text patterns.
            return IsOverflowErrorMessage(message.ErrorMessage, extraPatterns);
        }

        if (contextWindow is not { } window || window <= 0) return false;
        var promptTokens = message.Usage.Input + message.Usage.CacheRead;

        if (message.StopReason == StopReason.Stop)
        {
            return promptTokens > window;
        }

        if (message.StopReason == StopReason.Length)
        {
            return message.Usage.Output == 0 && promptTokens >= window * 0.99;
        }

        return false;
    }

    /// <summary>True for a machine-readable error code/type that means the prompt did not fit.</summary>
    public static bool IsOverflowErrorCode(string? code) =>
        !string.IsNullOrEmpty(code) && OverflowCodes.Contains(code);

    /// <summary>A length stop that can be continued because the model has output room left.</summary>
    public static bool IsRecoverableLength(AssistantMessage message, int desiredMaxOutputTokens) =>
        message.StopReason == StopReason.Length && message.Usage.Output < desiredMaxOutputTokens;

    private static bool Matches(string pattern, string input)
    {
        try
        {
            return Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        }
        catch (RegexParseException)
        {
            // A malformed pattern in models.json is a literal phrase match, never a crash.
            return input.Contains(pattern, StringComparison.OrdinalIgnoreCase);
        }
    }
}
