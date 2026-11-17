namespace OpenUL.Windowing.Events;

public interface IWindowResizedEventArgs
{
  (int Width, int Height) Size { get; }
}

public delegate void WindowResizedEventHandler(int width, int height);