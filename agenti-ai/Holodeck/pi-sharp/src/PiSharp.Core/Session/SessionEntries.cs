// Ported from pi: packages/coding-agent/src/core/session-manager.ts (linear subset) @ 71dca871b
// Dropped: tree branching, labels, custom/custom_message, thinking_level_change, branch_summary.
using PiSharp.Core.Messages;

namespace PiSharp.Core.Session;

public static class SessionFormat
{
    public const int CurrentVersion = 3;
}

public sealed record SessionHeader(
    string Id,
    DateTimeOffset Timestamp,
    string Cwd,
    int Version = SessionFormat.CurrentVersion,
    string? ParentSession = null);

public abstract record SessionEntry
{
    public required string Id { get; init; }
    public string? ParentId { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record SessionMessageEntry : SessionEntry
{
    public required Message Message { get; init; }
}

public sealed record CompactionEntry : SessionEntry
{
    public required string Summary { get; init; }
    public required string FirstKeptEntryId { get; init; }
    public int TokensBefore { get; init; }
    public CompactionDetails? Details { get; init; }
    public Usage? Usage { get; init; }
}

/// <summary>One automatic compaction of an attached seat, for ops: who, when (the entry timestamp), what
/// triggered it, tokens before and after, how many inbox tasks were open and whether the save-before-compact
/// turn ran ("ran", "skipped", "off"). Carries no message; never part of the context.</summary>
public sealed record CompactionLogEntry : SessionEntry
{
    public string? Actor { get; init; }
    public required string Trigger { get; init; }
    public int TokensBefore { get; init; }
    public int TokensAfter { get; init; }
    public int? OpenTasks { get; init; }
    public required string SaveBeforeCompact { get; init; }
}

public sealed record ModelChangeEntry : SessionEntry
{
    public required string Provider { get; init; }
    public required string ModelId { get; init; }
}

/// <summary>pi-sharp-checkpoints-1: one shadow-store snapshot taken before a mutating batch.
/// No file lists (the design rule: entries stay tiny; the shadow git dir is the body).
/// <see cref="Commit"/> is the shadow commit; <see cref="Head"/> the real repo HEAD it was taken at.</summary>
public sealed record CheckpointEntry : SessionEntry
{
    public required string Commit { get; init; }
    /// <summary>Canonical Git work-tree root owning this checkpoint.</summary>
    public string? WorkspaceRoot { get; init; }
    public string Head { get; init; } = "";
    public string[] ToolCallIds { get; init; } = [];
    public bool Green { get; init; }
    public bool Partial { get; init; }
    public string? RewoundTo { get; init; }
}

public sealed record SessionInfoEntry : SessionEntry
{
    public string? Name { get; init; }
}

public sealed record CompactionDetails(IReadOnlyList<string> ReadFiles, IReadOnlyList<string> ModifiedFiles,
    string? NotebookState = null, string? SourceBoundary = null);

public sealed record SessionContext(
    IReadOnlyList<Message> Messages,
    string? ModelProvider,
    string? ModelId);
