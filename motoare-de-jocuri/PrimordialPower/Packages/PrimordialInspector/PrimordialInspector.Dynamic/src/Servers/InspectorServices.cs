namespace PrimordialInspector.Scripting.Servers;

public class InspectorServices : IInspectorServices
{
  private readonly object _focusLock = new();
  private object _currentInstance;
  private string[] _currentUsings;

  public InspectorServices(object initialInstance)
  {
    _currentInstance = initialInstance;
    _currentUsings = [];
  }

  public void SetFocus(object instance, string[]? usings = null)
  {
    lock (_focusLock)
    {
      _currentInstance = instance;
      _currentUsings = usings ?? [];
    }
  }

  public (object instance, string[] usings) GetFocus()
  {
    lock (_focusLock)
    {
      return (_currentInstance, _currentUsings);
    }
  }
}
