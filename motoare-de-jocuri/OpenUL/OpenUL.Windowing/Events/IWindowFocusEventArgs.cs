namespace OpenUL.Windowing.Events;

public interface IWindowFocusEventArgs
{
  bool IsFocused { get; }
}

public delegate void WindowFocusEventHandler(bool isFocused);