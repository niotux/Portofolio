namespace UnivKit.OS.Windowing;

public interface IWindowManager
{
  IWindow CreateWindow(string title, uint width, uint height, int? x = null, int? y = null);

  void CloseAll();

  int CreatedCount { get; }
  int OpenCount { get; }
  SurfaceBackend Backend { get; }

  event WindowEventHandler? OnEvent;
}