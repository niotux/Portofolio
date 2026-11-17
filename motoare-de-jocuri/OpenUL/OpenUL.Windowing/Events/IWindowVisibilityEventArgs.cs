namespace OpenUL.Windowing.Events;

public interface IWindowVisibilityEventArgs
{
  bool IsVisible { get; }
}

public delegate void WindowVisibilityEventHandler(bool isVisible);