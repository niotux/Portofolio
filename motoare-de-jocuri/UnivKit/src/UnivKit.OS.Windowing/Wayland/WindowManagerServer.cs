namespace UnivKit.OS.Windowing.Wayland;

internal class WindowManagerServer : WindowingNativeServiceBase
{
  protected override void PollEventsCore() => throw new PlatformNotSupportedException();
  protected override void WakeUpEventLoopCore() => throw new PlatformNotSupportedException();
  protected override void OpenCore(Window window) => throw new PlatformNotSupportedException();
  protected override void CloseCore(Window window) => throw new PlatformNotSupportedException();
  protected override ulong GetKeyFromHandleCore(in SurfaceHandle handle) => throw new PlatformNotSupportedException();

  internal override void SetFocusCore(Window window) => throw new PlatformNotSupportedException();
  internal override void SetSizeCore(Window window, uint width, uint height) => throw new PlatformNotSupportedException();
  internal override void SetPositionCore(Window window, int x, int y) => throw new PlatformNotSupportedException();
  internal override void SetTitleCore(Window window, string text) => throw new PlatformNotSupportedException();
  internal override void SetStateCore(Window window, WindowState state) => throw new PlatformNotSupportedException();

  protected override void InitCore()
  {
    throw new NotImplementedException();
  }
}
