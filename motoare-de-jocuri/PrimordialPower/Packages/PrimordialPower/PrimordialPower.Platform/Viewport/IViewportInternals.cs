namespace PrimordialPower.Platform;

public interface IViewportInternals<TSelf>
where TSelf : IViewportInternals<TSelf>, IViewport
{
  void ChangeInternalSize(uint width, uint height);
  void ChangeInternalPosition(int x, int y);
  void ChangeInternalOffset(int x, int y);
  void ChangeInternalFocus(bool isFocused);
  void ChangeInternalVisibility(bool isVisible);
  void ChangeInternalManger(IViewportManager<TSelf>? manager);
  bool ConfirmDestroy();
  void Validate(in ViewportHandle handle);
  void Invalidate();
  void CheckAlive();
  void CheckBoundTo(IViewportManager<TSelf> manager);
}