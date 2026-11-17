using OpenUL.Windowing.Events;

namespace OpenUL.Windowing.GLFW.Events;

internal sealed class WindowMovedEventArgs : WindowEventArgs, IWindowMovedEventArgs
{
  public (int X, int Y) Position { get; set; }
}