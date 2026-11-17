using OpenUL.Windowing.Events;

namespace OpenUL.Windowing.GLFW.Events;

internal sealed class WindowVisibilityEventArgs : WindowEventArgs, IWindowVisibilityEventArgs
{
  public bool IsVisible { get; set; }
}