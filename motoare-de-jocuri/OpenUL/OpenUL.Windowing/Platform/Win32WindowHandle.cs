namespace OpenUL.Windowing.Platform;

public readonly record struct Win32WindowHandle : IWindowHandle<Win32WindowHandle>
{
  public nint HWnd { get; init; }
  public nint HInstance { get; init; }

  public static Win32WindowHandle Create(nint hWnd, nint hInstance)
    => new()
    {
      HWnd = hWnd,
      HInstance = hInstance,
    };

  static Win32WindowHandle IWindowHandle<Win32WindowHandle>.FromWindowHandle(ulong loWord, ulong hiWord)
    => Create((nint)loWord, (nint)hiWord);

  public static implicit operator WindowHandle(Win32WindowHandle handle)
    => WindowHandle.Create<Win32WindowHandle>((ulong)handle.HWnd, (ulong)handle.HInstance);
}