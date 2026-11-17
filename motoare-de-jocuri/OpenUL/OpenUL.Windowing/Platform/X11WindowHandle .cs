namespace OpenUL.Windowing.Platform;

public readonly record struct X11WindowHandle : IWindowHandle<X11WindowHandle>
{
  public nint Display { get; init; }
  public ulong Window { get; init; }

  public static X11WindowHandle Create(nint display, ulong window)
    => new()
    {
      Display = display,
      Window = window,
    };

  static X11WindowHandle IWindowHandle<X11WindowHandle>.FromWindowHandle(ulong loWord, ulong hiWord)
    => Create((nint)loWord, hiWord);

  public static implicit operator WindowHandle(X11WindowHandle handle)
    => WindowHandle.Create<X11WindowHandle>((ulong)handle.Display, handle.Window);
}