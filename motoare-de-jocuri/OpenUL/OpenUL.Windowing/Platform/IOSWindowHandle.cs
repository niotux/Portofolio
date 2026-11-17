namespace OpenUL.Windowing.Platform;

public readonly record struct IOSWindowHandle : IWindowHandle<IOSWindowHandle>
{
  public nint Window { get; init; }
  public nint View { get; init; }

  public static IOSWindowHandle Create(nint window, nint view)
    => new()
    {
      Window = window,
      View = view,
    };

  static IOSWindowHandle IWindowHandle<IOSWindowHandle>.FromWindowHandle(ulong loWord, ulong hiWord)
    => Create((nint)loWord, (nint)hiWord);

  public static implicit operator WindowHandle(IOSWindowHandle handle)
    => WindowHandle.Create<IOSWindowHandle>((ulong)handle.Window, (ulong)handle.View);
}