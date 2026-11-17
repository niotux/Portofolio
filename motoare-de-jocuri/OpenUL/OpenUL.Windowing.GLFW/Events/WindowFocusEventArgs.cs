using OpenUL.Windowing.Events;

namespace OpenUL.Windowing.GLFW.Events;

internal sealed class WindowFocusEventArgs : WindowEventArgs, IWindowFocusEventArgs
{
  public bool IsFocused { get; set; }
}