using System.Text.Json.Nodes;
using PiSharp.Core.Agent;
using PiSharp.Core.Checkpoints;
using PiSharp.Core.Session;

namespace PiSharp.Tools;

/// <summary>Preview or restore a session checkpoint through the isolated CheckpointStore API.</summary>
public sealed class RewindTool : BuiltinTool<RewindArgs>
{
    private static readonly JsonObject ArgsSchema = Schema.Parse("""
        {
          "type": "object",
          "properties": {
            "to": { "type": "string", "description": "last-green, a checkpoint entry id, or before:<toolCallId>" },
            "dryRun": { "type": "boolean", "default": true, "description": "Preview changes only" }
          },
          "required": ["to"]
        }
        """);

    private readonly CheckpointStore _store;
    private readonly SessionManager _session;
    private readonly string? _checkpointsDir;

    public RewindTool(string cwd, CheckpointStore store, SessionManager session, FileReadTracker tracker)
        : base(
            "rewind",
            "rewind",
            "Preview or restore a checkpoint. Select last-green, a checkpoint entry id, or before:<toolCallId>. Dry run defaults to true.",
            ArgsSchema,
            ToolsJsonContext.Default.RewindArgs,
            cwd)
    {
        _store = store;
        _session = session;
        ReadTracker = tracker;
    }

    public RewindTool(string cwd, string checkpointsDir, SessionManager session, FileReadTracker tracker)
        : this(cwd, new CheckpointStore(cwd, checkpointsDir), session, tracker)
    {
        _checkpointsDir = Path.GetFullPath(checkpointsDir);
    }

    protected override async Task<AgentToolResult> ExecuteAsync(
        string toolCallId,
        RewindArgs args,
        Action<AgentToolResult>? onUpdate,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var target = CheckpointSelector.Resolve(args.To, _session.Entries);
        var store = target.WorkspaceRoot is { } root
            ? _checkpointsDir is { } dir
                ? new CheckpointStore(root, dir)
                : string.Equals(CheckpointStore.ResolveWorkTreeRoot(root), _store.Cwd, StringComparison.Ordinal)
                    ? _store
                    : throw new InvalidOperationException("Checkpoint belongs to a different workspace; rewind it from a session with that workspace store.")
            : _store;
        if (target.WorkspaceRoot is { } expectedRoot &&
            !string.Equals(store.Cwd, CheckpointStore.ResolveWorkTreeRoot(expectedRoot), StringComparison.Ordinal))
            throw new InvalidOperationException("Cannot rewind across workspace roots.");
        if (target.Partial)
            throw new InvalidOperationException($"Cannot rewind to partial checkpoint {target.Id}.");

        if (args.DryRun ?? true)
        {
            var plan = await store.PlanAsync(target.Commit, cancellationToken).ConfigureAwait(false);
            if (!plan.Ok)
                return AgentToolResult.Error($"Cannot rewind to {target.Id}: {plan.Reason ?? "restore plan refused"}.");

            var paths = plan.Paths.Take(40).ToArray();
            var details = paths.Length == 0
                ? "No path list was returned."
                : "Paths:\n" + string.Join("\n", paths.Select(path => "- " + path));
            return AgentToolResult.FromText(
                $"Dry run to checkpoint {target.Id}: {plan.Changed} changed, {plan.Deleted} deleted.\n{details}");
        }

        var outcome = await store.RestoreAsync(target.Commit, target.Head, cancellationToken).ConfigureAwait(false);
        if (!outcome.Applied)
            return AgentToolResult.Error($"Rewind refused: {outcome.Reason ?? "restore failed"}.");

        var touchedCount = 0;
        foreach (var relativePath in outcome.Touched)
        {
            ReadTracker?.Forget(ResolveRepoPath(store.Cwd, relativePath));
            touchedCount++;
        }

        _session.AppendCheckpoint(new CheckpointEntry
        {
            Id = "",
            Commit = target.Commit,
            Head = target.Head,
            RewoundTo = target.Id,
        });

        return AgentToolResult.FromText($"Rewound to checkpoint {target.Id}; {touchedCount} paths restored or deleted.");
    }

    private static string ResolveRepoPath(string cwd, string relativePath) =>
        Path.GetFullPath(Path.Combine(cwd, relativePath.Replace('/', Path.DirectorySeparatorChar)));
}
