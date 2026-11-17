namespace OpenUL.Windowing.Events;

public interface IWindowClosingEventArgs
{
  bool Close { set; }
}

public delegate void WindowClosingEventHandler(out bool close);