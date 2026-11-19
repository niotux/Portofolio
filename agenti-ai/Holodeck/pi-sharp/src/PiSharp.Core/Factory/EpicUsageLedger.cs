namespace PiSharp.Core.Factory;

/// <summary>Binds usage to its originating task, including helper completions after the parent changes tasks.</summary>
public sealed class EpicUsageLedger(EpicBudget budget)
{
    public const int RetainedScopes = 128;
    private readonly Dictionary<string, (string Slug, long? Budget)?> _bindings = new(StringComparer.Ordinal);
    private readonly Queue<string> _order = new();
    private readonly object _gate = new();

    public void Bind(string scope, (string Slug, long? Budget)? epic)
    {
        lock (_gate)
        {
            if (!_bindings.ContainsKey(scope)) _order.Enqueue(scope);
            _bindings[scope] = epic;
            while (_order.Count > RetainedScopes) _bindings.Remove(_order.Dequeue());
        }
    }

    public string? Charge(string scope, long tokens)
    {
        (string Slug, long? Budget)? epic;
        lock (_gate) epic = _bindings.GetValueOrDefault(scope);
        return epic is { } owner ? budget.Charge(owner.Slug, tokens, owner.Budget) : null;
    }
}
