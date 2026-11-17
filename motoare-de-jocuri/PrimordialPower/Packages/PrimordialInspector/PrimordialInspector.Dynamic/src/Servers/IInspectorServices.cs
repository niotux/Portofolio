namespace PrimordialInspector.Scripting.Servers;

/// <summary>
/// Service interface for inspector operations.
/// </summary>
public interface IInspectorServices
{
  /// <summary>
  /// Thread-safely switches focus to a different instance and using statements.
  /// </summary>
  void SetFocus(object instance, string[]? usings = null);
}
