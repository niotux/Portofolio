namespace OpenUL.Windowing.Platform;

public readonly record struct WaylandWindowHandle : IWindowHandle<WaylandWindowHandle>
{
  public nint Display { get; init; }
  public nint Surface { get; init; }

  public static WaylandWindowHandle Create(nint display, nint window)
    => new()
    {
      Display = display,
      Surface = window,
    };

  static WaylandWindowHandle IWindowHandle<WaylandWindowHandle>.FromWindowHandle(ulong loWord, ulong hiWord)
    => Create((nint)loWord, (nint)hiWord);

  public static implicit operator WindowHandle(WaylandWindowHandle handle)
    => WindowHandle.Create<WaylandWindowHandle>((ulong)handle.Display, (ulong)handle.Surface);
}