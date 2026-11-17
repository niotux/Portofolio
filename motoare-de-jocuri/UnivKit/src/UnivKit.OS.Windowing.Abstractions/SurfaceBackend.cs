using System.Runtime.Versioning;

namespace UnivKit.OS.Windowing;

[Flags]
public enum SurfaceBackend : byte
{
  None = 0,
  Win32 = 1 << 0,
  Cocoa = 1 << 1,
  Browser = 1 << 2,
  Android = 1 << 3,
  X11 = 1 << 4,
  Wayland = 1 << 5,
}

public static class SurfaceSystemBackend
{
  public static SurfaceBackend Type
  {
    get
    {
      var mask = SurfaceBackend.None;

      if (IsWin32())
      {
        mask |= SurfaceBackend.Win32;
      }
      if (IsWayland())
      {
        mask |= SurfaceBackend.Wayland;
      }
      if (IsX11())
      {
        mask |= SurfaceBackend.X11;
      }
      if (IsAndroid())
      {
        mask |= SurfaceBackend.Android;
      }
      if (IsCocoa())
      {
        mask |= SurfaceBackend.Cocoa;
      }
      if (IsBrowser())
      {
        mask |= SurfaceBackend.Browser;
      }

      return mask;
    }
  }

  [SupportedOSPlatformGuard("windows")]
  public static bool IsWin32() => OperatingSystem.IsWindows();


  [SupportedOSPlatformGuard("android")]
  public static bool IsAndroid() => OperatingSystem.IsAndroid();


  [SupportedOSPlatformGuard("browser")]
  public static bool IsBrowser() => OperatingSystem.IsBrowser();


  [SupportedOSPlatformGuard("macos")]
  public static bool IsCocoa() => OperatingSystem.IsMacOS();


  [SupportedOSPlatformGuard("linux")]
  public static bool IsX11()
  {
    if (!OperatingSystem.IsLinux())
    {
      return false;
    }
    string? envVar = Environment.GetEnvironmentVariable("DISPLAY");
    return !string.IsNullOrWhiteSpace(envVar);
  }


  [SupportedOSPlatformGuard("linux")]
  public static bool IsWayland()
  {
    if (!OperatingSystem.IsLinux())
    {
      return false;
    }
    string? envVar = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
    return !string.IsNullOrWhiteSpace(envVar);
  }
}