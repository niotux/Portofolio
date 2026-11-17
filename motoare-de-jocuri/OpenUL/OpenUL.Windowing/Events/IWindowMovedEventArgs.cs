namespace OpenUL.Windowing.Events;

public interface IWindowMovedEventArgs
{
  (int X, int Y) Position { get; }
}

public delegate void WindowMovedEventHandler(int x, int y);