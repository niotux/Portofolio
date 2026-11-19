// What an attached (factory) seat adds around an automatic compaction. A plain CLI run passes none: its
// compaction is summary-only, exactly as before.
namespace PiSharp.Core.Session;

public sealed class CompactionHooks
{
    /// <summary>The instruction for the one save-before-compact turn. When the compaction threshold is reached
    /// the model first gets this as a user message and one turn to write partial work down; the compaction runs
    /// right after that turn. Null = compact at once (no save turn).</summary>
    public string? SavePrompt { get; init; }

    /// <summary>Called after every automatic compaction. Non-empty text becomes a user message placed after the
    /// summary and after the next turn's steering messages, so it is the last thing the model reads (open inbox
    /// tasks, live facts). Must not throw; a throw is reported through <see cref="AgentSession.Notice"/> and
    /// nothing is appended.</summary>
    public Func<string?>? CarryForward { get; init; }

    /// <summary>How many inbox tasks are open right now, for the compaction log line. Null = not tracked.</summary>
    public Func<int>? OpenTaskCount { get; init; }

    /// <summary>Called once per completed automatic compaction with the open-tasks note the seat is carrying
    /// across it (null when nothing is open), so the seat's steering can mark the items it lists as carried
    /// across a compaction. Must not throw; a throw is reported through <see cref="AgentSession.Notice"/> and
    /// never fails the compaction.</summary>
    public Action<string?>? OnCompactionOccurred { get; init; }

    /// <summary>Called on every failed automatic compaction with the reason, so the seat's steering can say so on its
    /// status line once failures repeat. A seat that cannot compact keeps taking turns and looks busy from outside;
    /// agent-maker-2 failed 82 times on 2026-10-02 and nothing showed it. Must not throw.</summary>
    public Action<string>? OnCompactionFailed { get; init; }

    /// <summary>Who this seat is, for the compaction log line.</summary>
    public string? Actor { get; init; }

    /// <summary>The default <see cref="SavePrompt"/> for attached seats. It names the exact call and asks for
    /// conclusions: seats told to "write partial results to your notes" pasted transcripts into .md files, which
    /// nothing reads back, instead of the few lines the next context needs (and which recall brings back).</summary>
    public const string DefaultSavePrompt =
        "[harness] Your context is about to be compacted: older messages will be replaced by a summary. "
        + "Before that, save what you would lose with one call: notebook op=write id=<topic key> title=\"<one line>\" tags=[<task>, <area>] "
        + "text=\"<conclusions, decisions and why, next steps, file paths and commit shas>\" (op=batch for several). "
        + "Conclusions, not transcripts: no pasted logs, diffs or tool output. Findings other agents need go to the KB. "
        + "Update your chat status. Keep it short: this is one turn. Reply 'saved' when done. You continue your work right after the compaction, "
        + "and your notes are recalled for you.";

    /// <summary>The user message that keeps the loop going after a save turn that made no tool calls.</summary>
    public const string ContinueAfterSave = "[harness] Context compacted. Continue your current work.";
}
