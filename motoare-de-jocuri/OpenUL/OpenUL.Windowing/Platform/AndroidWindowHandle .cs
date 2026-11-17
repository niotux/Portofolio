namespace OpenUL.Windowing.Platform;

public readonly record struct AndroidWindowHandle : IWindowHandle<AndroidWindowHandle>
{
  public nint NativeWindow { get; init; }

  public static AndroidWindowHandle Create(nint nativeWindow)
    => new()
    {
      NativeWindow = nativeWindow,
    };

  static AndroidWindowHandle IWindowHandle<AndroidWindowHandle>.FromWindowHandle(ulong loWord, ulong hiWord)
    => Create((nint)loWord);

  public static implicit operator WindowHandle(AndroidWindowHandle handle)
    => WindowHandle.Create<AndroidWindowHandle>((ulong)handle.NativeWindow, default);
}