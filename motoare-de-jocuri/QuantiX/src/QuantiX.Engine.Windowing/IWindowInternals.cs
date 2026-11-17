namespace QuantiX.Engine.Windowing;

public interface IWindowInternals<TSelf>
where TSelf : IWindowInternals<TSelf>, IWindow
{
  void ChangeInternalSize(uint width, uint height);
  void ChangeInternalPosition(int x, int y);
  void ChangeInternalFocus(bool isFocused);
  void ChangeInternalVisibility(bool isVisible);
  void ChangeInternalManger(IWindowManager<TSelf>? manager);
  bool ConfirmDestroy();
  void Validate(in WindowHandle handle);
  void Invalidate();
  void CheckAlive();
  void CheckBoundTo(IWindowManager<TSelf> manager);
}