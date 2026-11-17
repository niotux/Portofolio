using OpenUL.Windowing.Events;

namespace OpenUL.Windowing;

public interface IWindow : IDisposable
{
  (int Width, int Height) Size { get; }
  WindowHandle Handle { get; }
  bool IsVisible { get; }
  bool IsFocused { get; }
  bool IsOpen { get; }

  void Show();
  void Hide();
  bool UpdateClient();
  void PostEvent(WindowEventArgs args);
  void Close();
  void Focus();

  event Action<WindowEventArgs>? Event;
  event Action<EventArgs>? OtherEvent;

  event WindowResizedEventHandler? Resized;
  event WindowVisibilityEventHandler? VisibilityChanged;
  event WindowFocusEventHandler? FocusChanged;
  event WindowUpdateEventHandler? Updated;
  event WindowClosingEventHandler? Closing;
  event WindowClosedEventHandler? Closed;
}
