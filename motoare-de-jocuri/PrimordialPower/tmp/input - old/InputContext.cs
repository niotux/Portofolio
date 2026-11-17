namespace PrimordialPower.Core;

public class InputContext
{
  private Keyboard _Keyboard = new();
  private Mouse _Mouse = new();
  private IViewport _ViewportContext;
  public InputContext(IViewport viewportContext)
  {
    _ViewportContext = viewportContext;
  }


  private ConcurrentEvent.Action<Keyboard, Mouse> _OnInputEvent = new();
  public event Action<Keyboard, Mouse>? OnInputEvent
  {
    add => _OnInputEvent += value;
    remove => _OnInputEvent -= value;
  }
  internal void InputEvents()
  {
    if (_ViewportContext.IsFocused)
      _OnInputEvent.Invoke(_Keyboard, _Mouse);
  }
}
