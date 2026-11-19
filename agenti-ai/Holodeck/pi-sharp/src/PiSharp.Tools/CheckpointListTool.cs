using System.Text.Json.Nodes;
using PiSharp.Core.Agent;
using PiSharp.Core.Checkpoints;
using PiSharp.Core.Session;

namespace PiSharp.Tools;

/// <summary>
/// pi-sharp-cp6 (decision pi-sharp-cp6-list): the read-only list surface for the current
/// workspace anchor. Lists the session's CheckpointEntry rows for the anchor, newest-first,
/// default 20; fields only: id, ts, green, partial, commit prefix (8), head. Never writes
/// the store and never shells git (the anchor is compared in stored form, no path resolution);
/// the shadow store (CP1/CP2) owns persistence.
/// </summary>
public sealed class CheckpointListTool : BuiltinTool<CheckpointListArgs>
{
    /// <summary>Default number of recent snapshots listed.</summary>
    public const int DefaultLimit = 20;

    private static readonly JsonObject ArgsSchema = Schema.Parse("""
        {
          "type": "object",
          "properties": {
            "limit": { "type": "number", "default": 20, "description": "How many recent snapshots to list (default 20)" }
          }
        }
        """);

    private readonly SessionManager _session;
    private readonly string _anchor;

    public CheckpointListTool(string cwd, CheckpointStore store, SessionManager session)
        : base(
            "checkpoint_list",
            "checkpoint_list",
            $"List the recent checkpoints for the current workspace, newest-first, up to {DefaultLimit} by default. Read-only: shows id, time, green, partial, commit prefix and head. Use the entry id with rewind.",
            ArgsSchema,
            ToolsJsonContext.Default.CheckpointListArgs,
            cwd)
    {
        _session = session;
        // The store's Cwd is already the canonical work-tree root (resolved at construction),
        // and entries store WorkspaceRoot in the same form: the anchor comparison needs no
        // path resolution and therefore never shells git.
        _anchor = store.Cwd;
    }

    protected override Task<AgentToolResult> ExecuteAsync(
        string toolCallId,
        CheckpointListArgs args,
        Action<AgentToolResult>? onUpdate,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var limit = args.Limit is { } requested && requested > 0 ? (int)requested : DefaultLimit;

        var entries = _session.Entries.OfType<CheckpointEntry>()
            .Where(entry => string.Equals(entry.WorkspaceRoot, _anchor, StringComparison.Ordinal))
            .ToArray();

        if (entries.Length == 0)
            return Task.FromResult(AgentToolResult.FromText($"No checkpoints recorded for {_anchor}."));

        var shown = entries.AsEnumerable().TakeLast(limit).Reverse().ToArray();
        var lines = new List<string>(shown.Length + 1);
        lines.Add($"{shown.Length} of {entries.Length} recent checkpoints for {_anchor} (newest first):");
        foreach (var entry in shown)
        {
            lines.Add(
                $"- id={entry.Id} ts={entry.Timestamp:yyyy-MM-ddTHH:mm:sszzz} green={(entry.Green ? "yes" : "no")} " +
                $"partial={(entry.Partial ? "yes" : "no")} commit={CommitPrefix(entry.Commit)} head={entry.Head}");
        }

        return Task.FromResult(AgentToolResult.FromText(string.Join('\n', lines)));
    }

    private static string CommitPrefix(string commit) => commit.Length <= 8 ? commit : commit[..8];
}
