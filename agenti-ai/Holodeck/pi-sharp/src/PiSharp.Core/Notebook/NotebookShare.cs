using PiSharp.Core.Agent;
using PiSharp.Core.Messages;
using PiSharp.Core.Session;

namespace PiSharp.Core.Notebook;

/// <summary>
/// The notebook's share of one request, measured in <c>TransformContext</c>
/// (pi-sharp-memory-curation-1; epic doc <c>research</c>, "Measurement"). The notebook sends the
/// harness-injected messages (<c>&lt;notebook-state&gt;</c>, <c>&lt;notebook-state-update&gt;</c> and
/// <c>&lt;recalled-notes&gt;</c>); the request is everything else the model sees that turn. <see cref="Share"/>
/// is the notebook's fraction of what the model sees: the number that decides whether the native
/// curation budget (epic target: under 15%) is reached.
/// </summary>
public sealed record NotebookShare(
    int NotebookChars,
    int RequestChars,
    int Pinned,
    int Deltas,
    int Recalls)
{
    /// <summary>0.0 when the request is empty; otherwise notebook / (request + notebook).</summary>
    public double Share => NotebookChars + RequestChars == 0 ? 0.0 : (double)NotebookChars / (NotebookChars + RequestChars);
}

/// <summary>
/// Per-seat notebook-share tracker: one <see cref="NotebookShare"/> per model request, the most recent
/// ones kept, and the p50/p95 of their share over everything seen this process. Measurement only -
/// nothing here is sent to the model. Wired by the seat into <c>TransformContext</c> and shown by
/// <c>/session</c> (pi-sharp-memory-curation-1).
/// </summary>
public sealed class NotebookShareTracker
{
    /// <summary>Shares kept for the rolling p50/p95, oldest first.</summary>
    public const int Window = 1024;

    private readonly object _gate = new();
    private readonly Queue<double> _shares = new();
    private NotebookShare? _latest;

    /// <summary>
    /// The notebook's share of <paramref name="messages"/>: the harness-injected notebook messages against
    /// the rest of the context, counting the system prompt (the part of the request that is not the
    /// transcript). Measurement only: never throws.
    /// </summary>
    public static NotebookShare Measure(IReadOnlyList<Message> messages, string? systemPrompt = null, int toolCharacters = 0)
    {
        var pinned = 0;
        var deltas = 0;
        var recalls = 0;
        var notebookChars = 0;
        var requestChars = Math.Max(0, toolCharacters);
        foreach (var message in messages)
        {
            if (message is not UserMessage user)
            {
                requestChars += MessageChars.Of(message);
                continue;
            }
            var text = ContentText.Of(user.Content);
            if (text.StartsWith("<notebook-state>", StringComparison.Ordinal)) pinned++;
            else if (text.StartsWith("<notebook-state-update>", StringComparison.Ordinal)) deltas++;
            else if (text.StartsWith("<recalled-notes>", StringComparison.Ordinal)) recalls++;
            else { requestChars += text.Length; continue; }
            notebookChars += text.Length;
        }
        if (systemPrompt is { Length: > 0 }) requestChars += systemPrompt.Length;
        return new NotebookShare(notebookChars, requestChars, pinned, deltas, recalls);
    }

    /// <summary>Records one request's share. Returns the share for this request.</summary>
    public NotebookShare Record(NotebookShare share)
    {
        lock (_gate)
        {
            _latest = share;
            _shares.Enqueue(share.Share);
            if (_shares.Count > Window) _shares.Dequeue();
        }
        return share;
    }

    /// <summary>The most recent request's share, or null before the first.</summary>
    public NotebookShare? Current
    {
        get { lock (_gate) return _latest; }
    }

    /// <summary>p50/p95 of the share over everything recorded this process (0.0 with no samples).</summary>
    public (int Samples, double P50, double P95) Percentiles()
    {
        lock (_gate)
        {
            if (_shares.Count == 0) return (0, 0.0, 0.0);
            var sorted = _shares.OrderBy(x => x).ToArray();
            // Nearest-rank: the ceil(p/100 * n)-th smallest value (1-based), clamped to the last element.
            return (sorted.Length,
                sorted[Math.Clamp((int)Math.Ceiling(0.50 * sorted.Length) - 1, 0, sorted.Length - 1)],
                sorted[Math.Clamp((int)Math.Ceiling(0.95 * sorted.Length) - 1, 0, sorted.Length - 1)]);
        }
    }
}

/// <summary>Character half of the token estimator: the same per-message text the char/4 estimator
/// multiplies, without the division (pi-sharp-memory-curation-1).</summary>
internal static class MessageChars
{
    public static int Of(Message message) => message switch
    {
        UserMessage user => TextChars(user.Content),
        ToolResultMessage tool => TextChars(tool.Content),
        CompactionSummaryMessage compaction => compaction.Summary.Length,
        AssistantMessage assistant => AssistantChars(assistant),
        _ => 0,
    };

    private static int TextChars(IReadOnlyList<ContentPart> content)
    {
        var chars = 0;
        foreach (var block in content)
        {
            chars += block switch
            {
                TextContent text => text.Text.Length,
                ImageContent => TokenEstimate.EstimatedImageChars,
                _ => 0,
            };
        }
        return chars;
    }

    private static int AssistantChars(AssistantMessage assistant)
    {
        var chars = 0;
        foreach (var block in assistant.Content)
        {
            chars += block switch
            {
                TextContent text => text.Text.Length,
                ThinkingContent thinking => thinking.Thinking.Length,
                ImageContent => TokenEstimate.EstimatedImageChars,
                ToolCallContent call => call.Name.Length,
                _ => 0,
            };
        }
        return chars;
    }
}
