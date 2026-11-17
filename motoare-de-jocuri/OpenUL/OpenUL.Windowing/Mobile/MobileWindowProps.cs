namespace OpenUL.Windowing.Mobile;

public struct MobileWindowProps : IWindowProps
{
  public MobileWindowProps()
  {

  }

  public bool Visible { get; init; }
  public bool Focused { get; init; }
  public ScreenOrientation Orientation { get; init; } = ScreenOrientation.Auto;
  public bool Fullscreen { get; init; } = true;
  public bool KeepScreenOn { get; init; } = false;
  public bool ShowStatusBar { get; init; } = true;
  public bool ShowNavigationBar { get; init; } = true;
  public bool EdgeToEdge { get; init; } = true;

  public static MobileWindowProps Default => new();
}