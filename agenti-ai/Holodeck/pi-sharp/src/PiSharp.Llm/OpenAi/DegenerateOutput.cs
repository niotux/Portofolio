// pi-sharp only, no pi counterpart. A local vLLM serving Qwen3.x-27B now and then starts answering
// every request with nothing but '!' until it is restarted (digital-scribe, 2026-10-14). Reports of
// the same shape point at the server, not the prompt: Qwen3.6-27B-NVFP4 on vLLM with an FP8 KV cache
// emitting only exclamation marks (huggingface.co/nvidia/Qwen3.6-27B-NVFP4 discussion 10), and an FP8
// KV cache running on the default q_scale=1.0 producing garbage (ai-muninn.com, DGX Spark FP8 KV cache).
// Nothing the agent says fixes that, so the client stops reading, waits for the model to be healthy
// again, and only then re-issues the request.
using System.Text.Json.Nodes;

namespace PiSharp.Llm.OpenAi;

/// <summary>Counts the trailing run of <c>'!'</c> across a stream's content deltas.</summary>
internal sealed class DegenerateRun
{
    public int Length { get; private set; }

    /// <summary>Feeds one delta (text, thinking or tool-call arguments) and returns the run after it.</summary>
    public int Feed(string delta)
    {
        foreach (var c in delta) Length = c == '!' ? Length + 1 : 0;
        return Length;
    }

    /// <summary>True when <paramref name="text"/> contains a run of at least <paramref name="limit"/> '!'.</summary>
    public static bool Contains(string text, int limit)
    {
        var run = 0;
        foreach (var c in text)
        {
            run = c == '!' ? run + 1 : 0;
            if (run >= limit) return true;
        }
        return false;
    }
}

/// <summary>What the client tells its owner about the model while it waits on degenerate output.</summary>
/// <param name="Healthy">False when the wait starts (the probe came back degenerate or failed); true when a
/// probe came back sane after such a wait.</param>
/// <param name="Since">When the model was first seen degenerate in this wait.</param>
/// <param name="Detail">One line for a log or a status.</param>
public sealed record ModelHealthEvent(bool Healthy, DateTimeOffset Since, string Detail);

internal static class DegenerateProbe
{
    public const string Prompt = "Reply with the single word: ready";

    /// <summary>A probe answer is sane when it holds a letter or digit and no run of 8 '!'.</summary>
    public static bool IsSane(string? answer) =>
        answer is not null && answer.Any(char.IsLetterOrDigit) && !DegenerateRun.Contains(answer, 8);

    /// <summary>Text and reasoning of a non-streaming chat completion, or null when the body has neither.</summary>
    public static string? ReadAnswer(string body)
    {
        try
        {
            if (JsonNode.Parse(body) is not JsonObject json) return null;
            if (json["choices"] is not JsonArray { Count: > 0 } choices || choices[0]?["message"] is not JsonObject message) return null;
            var parts = new[] { "content", "reasoning_content", "reasoning" }
                .Select(key => message[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null)
                .Where(s => !string.IsNullOrEmpty(s));
            var answer = string.Concat(parts);
            return answer.Length > 0 ? answer : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
