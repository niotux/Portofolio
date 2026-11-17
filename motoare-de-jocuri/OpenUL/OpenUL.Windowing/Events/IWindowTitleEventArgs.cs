namespace OpenUL.Windowing.Events;

public interface IWindowTitleEventArgs
{
  string Title { get; }
}

public delegate void WindowTitleEventHandler(string title);