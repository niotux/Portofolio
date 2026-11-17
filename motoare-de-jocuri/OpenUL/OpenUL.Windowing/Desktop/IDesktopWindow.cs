using System.Runtime.CompilerServices;
using OpenUL.Windowing.Events;

namespace OpenUL.Windowing.Desktop;

public interface IDesktopWindow : IWindow
{
  new (int Width, int Height) Size { get; set; }
  (int Width, int Height) IWindow.Size
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => Size;
  }

  (int X, int Y) Position { get; set; }
  string Title { get; set; }
  bool IsResizeable { get; }

  event WindowMovedEventHandler? Moved;
  event WindowTitleEventHandler? TitleChanged;
}
