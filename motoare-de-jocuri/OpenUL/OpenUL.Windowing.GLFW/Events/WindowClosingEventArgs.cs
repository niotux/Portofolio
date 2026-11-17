using OpenUL.Windowing.Events;

namespace OpenUL.Windowing.GLFW.Events;

internal sealed class WindowClosingEventArgs : WindowEventArgs, IWindowClosingEventArgs
{
  public bool Close { get; set; }
}