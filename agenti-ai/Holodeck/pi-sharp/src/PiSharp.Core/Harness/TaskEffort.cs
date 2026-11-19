// Harness-side effort counter per task (owner-direct 2026-10-12): tokens, turns, tool calls and wall time, counted from
// the agent's own events with no model involvement. The tracer ships one "effort" delta whenever a task's counts are
// closed (its run ended, or another task took the seat); the hub sums the deltas onto the work item.
using System.Diagnostics;
using System.Text.Json.Nodes;
using PiSharp.Core.Messages;

namespace PiSharp.Core.Harness;

public sealed class TaskEffort
{
    private readonly object _gate = new();
    private string? _task;
    private string? _workItem;
    private int _turns;
    private int _toolCalls;
    private int _toolErrors;
    private Usage _usage = Usage.Zero;
    private long _wallTicks;
    private long? _activeSince;
    private string? _model;

    /// <summary>The task the open counts belong to; null before the first event.</summary>
    public string? Task { get { lock (_gate) return _task; } }

    /// <summary>The work item the open counts belong to, once known.</summary>
    public string? WorkItem { get { lock (_gate) return _workItem; } }

    /// <summary>Switches to <paramref name="task"/>. When it differs from the open task and something was counted,
    /// returns the closed delta of the old one; the new task starts from zero.</summary>
    public JsonObject? Enter(string? task)
    {
        lock (_gate)
        {
            if (task is null || string.Equals(task, _task, StringComparison.Ordinal)) return null;
            var closed = TakeLocked("task_switch");
            _task = task;
            _workItem = null;
            return closed;
        }
    }

    /// <summary>Binds the open task to a work item. The first binding wins over later observations; a harness binding
    /// (<paramref name="authoritative"/>) replaces an observed one.</summary>
    public void Bind(string? workItem, bool authoritative)
    {
        if (string.IsNullOrWhiteSpace(workItem)) return;
        lock (_gate)
            if (_workItem is null || authoritative) _workItem = workItem;
    }

    /// <summary>The seat is working: starts the wall clock if it is stopped.</summary>
    public void Active()
    {
        lock (_gate) _activeSince ??= Stopwatch.GetTimestamp();
    }

    /// <summary>A turn ended: stops the wall clock, so waiting for a follow-up between turns is not counted.</summary>
    public void Pause()
    {
        lock (_gate)
            if (_activeSince is { } since)
            {
                _wallTicks += Stopwatch.GetTimestamp() - since;
                _activeSince = null;
            }
    }

    public void Assistant(Usage usage, string? model = null)
    {
        lock (_gate)
        {
            if (!string.IsNullOrEmpty(model)) _model = model;
            _activeSince ??= Stopwatch.GetTimestamp();
            _turns++;
            _usage = _usage.Add(usage);
        }
    }

    public void ToolEnd(bool isError)
    {
        lock (_gate)
        {
            _toolCalls++;
            if (isError) _toolErrors++;
        }
    }

    /// <summary>Closes the open counts (the run stopped) and returns them, or null when nothing was counted. The task
    /// stays open: a follow-up run on the same task adds a further delta.</summary>
    public JsonObject? Close(string reason)
    {
        lock (_gate) return TakeLocked(reason);
    }

    private JsonObject? TakeLocked(string reason)
    {
        if (_activeSince is { } since)
        {
            _wallTicks += Stopwatch.GetTimestamp() - since;
            _activeSince = null;
        }
        if (_task is null || (_turns == 0 && _toolCalls == 0)) { Reset(); return null; }
        var delta = new JsonObject
        {
            ["task"] = _task,
            ["reason"] = reason,
            ["turns"] = _turns,
            ["tool_calls"] = _toolCalls,
            ["tool_errors"] = _toolErrors,
            ["wall_ms"] = (long)Stopwatch.GetElapsedTime(0, _wallTicks).TotalMilliseconds,
            ["usage"] = new JsonObject
            {
                ["input"] = _usage.Input,
                ["output"] = _usage.Output,
                ["cache_read"] = _usage.CacheRead,
                ["cache_write"] = _usage.CacheWrite,
                ["reasoning"] = _usage.Reasoning,
                ["total"] = _usage.TotalTokens,
            },
        };
        if (_workItem is not null) delta["work_item"] = _workItem;
        if (_model is not null) delta["model"] = _model;
        Reset();
        return delta;
    }

    private void Reset()
    {
        _turns = _toolCalls = _toolErrors = 0;
        _usage = Usage.Zero;
        _wallTicks = 0;
    }

    /// <summary>The work item a KB tool call names, for tasks the waking message did not bind: <c>kb_transition</c> and
    /// <c>start_task</c> (any MCP prefix) with a <c>work_item</c> or <c>slug</c> argument.</summary>
    public static string? WorkItemOf(string toolName, JsonNode? arguments)
    {
        if (arguments is not JsonObject args) return null;
        if (!(toolName.EndsWith("kb_transition", StringComparison.Ordinal) || toolName.EndsWith("start_task", StringComparison.Ordinal)))
            return null;
        foreach (var key in new[] { "work_item", "slug" })
            if (args[key] is JsonValue value && value.TryGetValue<string>(out var slug) && !string.IsNullOrWhiteSpace(slug))
                return slug.Trim();
        return null;
    }
}
