using PiSharp.Core.Messages;

namespace PiSharp.Core.Agent;

/// <summary>Only references results still present in this context; compaction makes old entries ineligible.</summary>
public sealed class ToolReadCache
{
    private readonly Dictionary<string, string> _results = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    public long Hits { get; private set; }

    public AgentToolResult? Find(string key, IReadOnlyList<Message> messages)
    {
        lock (_gate)
        {
            if (!_results.TryGetValue(key, out var id)) return null;
            var previous = messages.OfType<ToolResultMessage>().Where(m => m.ToolCallId == id).ToArray();
            if (previous.Length != 1 || previous[0].IsError || previous[0].Content.OfType<TextContent>().Sum(t => t.Text.Length) < 256) return null;
            return AgentToolResult.FromText($"[Unchanged file version and read range; contents are in earlier tool result {id}. Use read refresh=true to retrieve them again.]");
        }
    }
    public void RecordHit() { lock (_gate) Hits++; }

    public void Remember(string key, string id)
    {
        lock (_gate)
        {
            if (_results.Count >= 64) _results.Remove(_results.Keys.First());
            _results[key] = id;
        }
    }
}
