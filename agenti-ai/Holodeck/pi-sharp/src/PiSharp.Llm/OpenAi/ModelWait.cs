namespace PiSharp.Llm.OpenAi;

/// <summary>
/// What a request to a model waits on (<see cref="OpenAiCompletionsClient.OnWait"/>): since when, and the state: "waiting"
/// (nothing yet), "queued" (the router holds it in a backend's queue, <see cref="Position"/> its place), "working" (the
/// backend has it), "receiving" (the answer streams in: <see cref="Chars"/> so far, <see cref="Thinking"/> while the model
/// reasons), "done" (the request ended).
/// </summary>
public sealed record ModelWait(string Model, DateTimeOffset Since, string State, int? Position = null, long Chars = 0, bool Thinking = false)
{
    /// <summary>"waiting on qwen3-8-flash since 14:03Z (queued #3)", or "receiving from miniclaude since 14:03Z: ~12k tokens so
    /// far (thinking)", for a seat's status line; null once done.</summary>
    public string? Line => State switch
    {
        "done" => null,
        "receiving" => FormattableString.Invariant(
            $"receiving from {Model} since {Since.UtcDateTime:HH:mm}Z: ~{Tokens(Chars)} tokens so far{(Thinking ? " (thinking)" : "")}"),
        _ => FormattableString.Invariant(
            $"waiting on {Model} since {Since.UtcDateTime:HH:mm}Z{State switch
            {
                "queued" => Position is { } at ? $" (queued #{at})" : " (queued)",
                "working" => " (the model has it)",
                _ => "",
            }}"),
    };

    /// <summary>About four characters to a token: "300", "1.2k", "12k".</summary>
    private static string Tokens(long chars)
    {
        var tokens = chars / 4;
        return tokens >= 10_000 ? FormattableString.Invariant($"{tokens / 1000}k")
            : tokens >= 1_000 ? FormattableString.Invariant($"{tokens / 1000.0:0.0}k")
            : FormattableString.Invariant($"{tokens}");
    }
}
