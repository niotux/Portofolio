using PiSharp.Core.Session;

namespace PiSharp.Tools;

/// <summary>Resolves rewind selectors against the checkpoint records on this session's path.</summary>
public static class CheckpointSelector
{
    public static CheckpointEntry Resolve(string selector, IReadOnlyList<SessionEntry> entries)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selector);
        ArgumentNullException.ThrowIfNull(entries);

        var checkpoints = entries.OfType<CheckpointEntry>().ToArray();
        CheckpointEntry? match = selector switch
        {
            "last-green" => checkpoints.LastOrDefault(entry => entry.Green),
            _ when selector.StartsWith("before:", StringComparison.Ordinal) =>
                ResolveBefore(selector["before:".Length..], checkpoints),
            _ => checkpoints.LastOrDefault(entry => string.Equals(entry.Id, selector, StringComparison.Ordinal)),
        };

        return match ?? throw new InvalidOperationException($"No checkpoint matches '{selector}'.");
    }

    private static CheckpointEntry? ResolveBefore(string toolCallId, IReadOnlyList<CheckpointEntry> checkpoints)
    {
        if (string.IsNullOrWhiteSpace(toolCallId)) return null;
        return checkpoints.LastOrDefault(entry => entry.ToolCallIds.Contains(toolCallId, StringComparer.Ordinal));
    }
}
