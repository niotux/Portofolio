namespace PrimordialPower.Platform;

internal interface IViewportInternal : IViewport
{
  void UpdateInternalSize(uint width, uint height);
  void UpdateInternalPosition(int x, int y);
  void UpdateInternalActivation(bool focus);
  void Update();
  bool ConfirmDestroy();
}