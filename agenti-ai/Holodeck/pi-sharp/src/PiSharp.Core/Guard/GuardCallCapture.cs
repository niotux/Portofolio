using System.Text.Json;
using System.Text.Json.Nodes;
using PiSharp.Core.Notebook;

namespace PiSharp.Core.Guard;

/// <summary>Per-run, seat-local capture of successful tool calls (the guard-rules-3 replay
/// baseline) and the last tool calls before each turn (for correction capture). Writes
/// <c>guard-call-*</c> records to the notebook; one per successful call, capped. Never throws
/// into the tool path.</summary>
public sealed class GuardCallCapture
{
    private readonly NotebookStore _notebook;
    private readonly string _task;
    private readonly int _window;
    private readonly List<(string Name, string Args)> _recent = [];
    private int _good;

    public GuardCallCapture(NotebookStore notebook, string? taskKey, int goodCallWindow = GuardReplay.GoodCallWindow)
    {
        _notebook = notebook;
        _task = taskKey ?? "seat";
        _window = goodCallWindow;
    }

    public GuardCallCapture(NotebookStore notebook, Func<string?> taskKey, int goodCallWindow = GuardReplay.GoodCallWindow)
        : this(notebook, taskKey(), goodCallWindow) { }

    /// <summary>Called for every successfully completed tool call.</summary>
    public void RecordSuccess(string toolName, string argumentsJson)
    {
        try
        {
            _recent.Add((toolName, argumentsJson));
            if (_recent.Count > 10) _recent.RemoveRange(0, _recent.Count - 10);
            _good++;
            if (_good > _window) return;
            var id = "guard-call-" + _task + "-" + _good;
            var body = new JsonObject { ["name"] = toolName, ["arguments"] = argumentsJson };
            _notebook.Write(id, body.ToJsonString(), title: "successful tool call " + toolName,
                tags: ["guard"], kind: "guard-call");
        }
        catch { /* capture must never disturb the tool path */ }
    }

    /// <summary>The last recorded tool calls (up to 10), newest last. Used by correction capture:
    /// the first entry of the recorded list is the offending call the correction answers.</summary>
    public IReadOnlyList<(string Name, string Args)> RecentCalls() => _recent.ToArray();
}
