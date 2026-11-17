namespace OpenUL.Windowing.Mobile;

public interface IMobileWindow : IWindow
{
  ScreenOrientationState Orientation { get; }
}