namespace PiSharp.Core.Agent;

/// <summary>A cancelled tool retains mutation ownership until it actually stops, across subsequent runs/contexts.</summary>
public sealed class PendingToolCleanup
{
    private readonly HashSet<Task> _pending = [];
    private readonly object _gate = new();
    public bool HasUnfinished
    {
        get { lock (_gate) { _pending.RemoveWhere(t => t.IsCompleted); return _pending.Count > 0; } }
    }
    public void Retain(Task task) { lock (_gate) _pending.Add(task); }
}
