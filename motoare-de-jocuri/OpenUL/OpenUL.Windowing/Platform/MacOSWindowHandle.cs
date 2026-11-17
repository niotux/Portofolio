namespace OpenUL.Windowing.Platform;

public readonly record struct MacOSWindowHandle : IWindowHandle<MacOSWindowHandle>
{
  public nint Window { get; init; }

  public static MacOSWindowHandle Create(nint window)
    => new()
    {
      Window = window,
    };

  static MacOSWindowHandle IWindowHandle<MacOSWindowHandle>.FromWindowHandle(ulong loWord, ulong hiWord)
    => Create((nint)loWord);

  public static implicit operator WindowHandle(MacOSWindowHandle handle)
    => WindowHandle.Create<MacOSWindowHandle>((ulong)handle.Window, default);
}