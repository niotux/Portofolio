using OpenUL.Windowing.Desktop;
using OpenUL.Windowing.Mobile;

namespace OpenUL.Windowing;

public static class Window
{
  public static IDesktopWindow OpenDesktop<T>(DesktopWindowProps props)
  where T : IWindowingBackendProvider<IDesktopWindow, DesktopWindowProps>
  {
    return T.Backend.OpenWindow(props);
  }

  public static IWindow OpenMobile<T>(MobileWindowProps props)
  where T : IWindowingBackendProvider<IMobileWindow, MobileWindowProps>
  {
    return T.Backend.OpenWindow(props);
  }

  public static void PollEvents<T>()
  where T : IWindowingBackendProvider<IDesktopWindow, DesktopWindowProps>
  {
    T.Backend.PollEvents();
  }

  public static void CloseAll<T>()
  where T : IWindowingBackendProvider<IDesktopWindow, DesktopWindowProps>
  {
    T.Backend.CloseAll();
  }

  public static void DestroyAll<T>()
  where T : IWindowingBackendProvider<IDesktopWindow, DesktopWindowProps>
  {
    T.Backend.DisposeAll();
  }

  public static void UpdateAllClients<T>()
  where T : IWindowingBackendProvider<IDesktopWindow, DesktopWindowProps>
  {
    T.Backend.UpdateAllClients();
  }

  public static void WaitEvents<T>()
  where T : IWindowingBackendProvider<IDesktopWindow, DesktopWindowProps>
  {
    T.Backend.WaitEvents();
  }
}