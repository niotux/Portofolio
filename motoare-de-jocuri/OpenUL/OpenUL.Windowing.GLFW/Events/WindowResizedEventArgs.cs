using OpenUL.Windowing.Events;

namespace OpenUL.Windowing.GLFW.Events;

internal sealed class WindowResizedEventArgs : WindowEventArgs, IWindowResizedEventArgs
{
  public (int Width, int Height) Size { get; set; }
}