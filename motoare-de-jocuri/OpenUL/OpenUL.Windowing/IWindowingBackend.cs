namespace OpenUL.Windowing;

public interface IWindowingBackend<out TWindowContract, in TWindowProps> : IDisposable
where TWindowContract : IWindow
where TWindowProps : IWindowProps
{
  event Action<Exception>? Error;
  bool IsSupported { get; }
  int OpenCount { get; }
  void PollEvents();
  void WaitEvents();
  void UpdateAllClients();
  void CloseAll();
  void DisposeAll();
  TWindowContract OpenWindow(TWindowProps properties);
}