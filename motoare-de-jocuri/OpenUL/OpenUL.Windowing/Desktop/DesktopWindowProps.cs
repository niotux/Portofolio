using System.Diagnostics.CodeAnalysis;

namespace OpenUL.Windowing.Desktop;

public struct DesktopWindowProps : IWindowProps
{
  [SetsRequiredMembers]
  public DesktopWindowProps(string title, int width, int height, int? x = null, int? y = null)
  {
    Title = title;
    Size = (width, height);
    Position = x == null || y == null
      ? null
      : (x.Value, y.Value);
  }

  public required (int Width, int Height) Size { get; init; }
  public required string Title { get; init; }

  public (int X, int Y)? Position { get; init; } = null;
  public bool Visible { get; init; } = false;
  public bool Focused { get; init; } = true;
  public bool Resizable { get; init; } = false;
  public bool Decorated { get; init; } = true;
  public bool FocusOnShow { get; init; } = false;
  public bool Transparent { get; init; } = false;

  public static DesktopWindowProps DefaultUntitled => new()
  {
    Size = (800, 600),
    Title = string.Empty,
  };

  public static DesktopWindowProps DefaultResizeable => new()
  {
    Size = (800, 600),
    Title = string.Empty,
    Resizable = true,
  };

  public static DesktopWindowProps DefaultTransparent => new()
  {
    Size = (800, 600),
    Title = string.Empty,
    Decorated = false,
    Transparent = true,
  };

  public static DesktopWindowProps DefaultUndecorated => new()
  {
    Size = (800, 600),
    Title = string.Empty,
    Decorated = false,
  };
}
