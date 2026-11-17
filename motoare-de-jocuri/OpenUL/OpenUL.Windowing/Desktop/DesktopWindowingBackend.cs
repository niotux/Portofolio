namespace OpenUL.Windowing.Desktop;

public static class DesktopWindowingBackend
{
  public static IWindowingBackend<IDesktopWindow, DesktopWindowProps> Select
  (params ReadOnlySpan<IWindowingBackend<IDesktopWindow, DesktopWindowProps>> selectionList)
    => WindowingBackend.Select(selectionList);
}