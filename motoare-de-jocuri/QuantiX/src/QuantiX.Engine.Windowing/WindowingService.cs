using System.Runtime.Versioning;
using QuantiX.Engine.Platform.Linux;
using QuantiX.Engine.Platform.Windows;

namespace QuantiX.Engine.Windowing;

public sealed class WindowingService : IAppService
{
  private static readonly WindowHandle.BackendType _backendUsed;
  static WindowingService()
  {
    if (OperatingSystem.IsWindows())
    {
      _backendUsed = WindowHandle.BackendType.Win32;
    }
    else if (OperatingSystem.IsLinux())
    {
      _backendUsed = WindowHandle.BackendType.X11;
    }
    else
    {
      throw new PlatformNotSupportedException("Windows are not supported on this operating system.");
    }
  }

  [SupportedOSPlatformGuard("windows")]
  public static bool IsWin32 => _backendUsed == WindowHandle.BackendType.Win32;


  [SupportedOSPlatformGuard("linux")]
  public static bool IsX11 => _backendUsed == WindowHandle.BackendType.X11;


  [SupportedOSPlatformGuard("linux")]
  public static bool IsWayland => _backendUsed == WindowHandle.BackendType.Wayland;



  private readonly WindowManagerServerBase server;
  public WindowingService()
  {
    if (IsWin32)
    {
      this.server = new Win32.Win32WindowManagerServer();
    }
    else if (IsX11)
    {
      this.server = new X11.X11WindowManagerServer();
    }
    else
    {
      throw new PlatformNotSupportedException("Windows are not supported on this operating system.");
    }
  }



  public AppServer Server => this.server;
  public IWindowManager<Window> Manager => this.server;
}
