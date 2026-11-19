using System.Globalization;

namespace PiSharp.Core.Notebook;

/// <summary>Source records close themselves. A source is an obligation until something closes it, and nothing did:
/// every wake left one more open source behind, the briefing grew, and the oldest gave way to the budget unread
/// (agent-maker-2, 213 open sources on 2026-09-25). Three closers now:
/// <list type="bullet">
/// <item>the message behind the source is acknowledged in chat (<see cref="CompleteForMessages"/>, called from the
/// factory steering's ack, which follows every answer);</item>
/// <item>a prompt the seat has answered (a later assistant turn that ended normally) is completed as it is captured
/// (<see cref="NotebookContext.Capture"/>);</item>
/// <item>at startup, open sources older than <see cref="StaleAfter"/> are cancelled (<see cref="ExpireStale"/>): a
/// message nobody acted on for two days is not an obligation the next session can honour, and the record, its
/// text and its history stay readable with notebook get.</item>
/// </list></summary>
public static class NotebookSources
{
    public static readonly TimeSpan StaleAfter = TimeSpan.FromHours(48);

    /// <summary>Completes the open sources that belong to acknowledged chat messages <paramref name="ids"/>: the
    /// <c>chat-source-{id}</c> record, anything filed under task <c>chat-{id}</c>, and the inbox prompt copy of it.
    /// Failures are reported through <paramref name="report"/>, never thrown: an ack must not fail on memory.</summary>
    /// <returns>The IDs completed.</returns>
    public static IReadOnlyList<string> CompleteForMessages(NotebookStore store, IReadOnlyCollection<long> ids, Action<string>? report = null)
    {
        if (ids.Count == 0) return [];
        var markers = ids.Select(id => (Id: "chat-source-" + id.ToString(CultureInfo.InvariantCulture),
            Task: "chat-" + id.ToString(CultureInfo.InvariantCulture),
            Marker: FormattableString.Invariant($", message {id},"))).ToArray();
        var completed = new List<string>();
        foreach (var row in store.List(kind: "source", status: "open"))
        {
            var rid = row["id"]!.GetValue<string>();
            var text = row["text"]!.GetValue<string>();
            var task = row["task"]!.GetValue<string>();
            if (!markers.Any(m => rid == m.Id || task == m.Task
                    || (text.StartsWith("[inbox from @", StringComparison.Ordinal) && text.Contains(m.Marker, StringComparison.Ordinal)))) continue;
            if (TryTransition(store, rid, "completed", report)) completed.Add(rid);
        }
        return completed;
    }

    /// <summary>Cancels open sources whose last update is older than <paramref name="maxAge"/> (default
    /// <see cref="StaleAfter"/>). Blocked sources are left alone: somebody said what they wait for.</summary>
    /// <returns>The IDs cancelled.</returns>
    public static IReadOnlyList<string> ExpireStale(NotebookStore store, DateTimeOffset now, TimeSpan? maxAge = null, Action<string>? report = null)
    {
        var cutoff = now - (maxAge ?? StaleAfter);
        var expired = new List<string>();
        foreach (var row in store.List(kind: "source", status: "open"))
        {
            if (row["updatedAt"]?.GetValue<string>() is not { } stamp
                || !DateTimeOffset.TryParse(stamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var updated)
                || updated >= cutoff) continue;
            var rid = row["id"]!.GetValue<string>();
            if (TryTransition(store, rid, "cancelled", report)) expired.Add(rid);
        }
        return expired;
    }

    private static bool TryTransition(NotebookStore store, string id, string status, Action<string>? report)
    {
        try { store.Transition(id, status); return true; }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IOException or KeyNotFoundException)
        {
            report?.Invoke(status == "completed"
                ? $"factory: completing notebook source {id} failed: {ex.Message}"
                : $"notebook: expiring stale source {id} failed: {ex.Message}");
            return false;
        }
    }
}
