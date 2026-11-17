using OpenUL.Windowing.Events;

namespace OpenUL.Windowing.GLFW.Events;

public sealed class WindowTitleEventArgs : WindowEventArgs, IWindowTitleEventArgs
{
  public string Title { get; set; } = string.Empty;
}