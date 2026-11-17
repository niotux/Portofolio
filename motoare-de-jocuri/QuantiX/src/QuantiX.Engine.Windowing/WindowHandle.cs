using System.Drawing;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace QuantiX.Engine.Windowing;


[StructLayout(LayoutKind.Explicit, Size = sizeof(BackendType) + HandleStructSize)]
public struct WindowHandle
{
  public const int HandleStructSize = 128;



  public enum BackendType : byte
  {
    Unknown = 0,
    Win32 = 1,
    X11 = 2,
    Wayland = 3,
    Cocos = 4,
  }



  [FieldOffset(0)]
  public readonly BackendType Backend = BackendType.Unknown;



  [SupportedOSPlatform("windows")]
  [FieldOffset(sizeof(BackendType))]
  public readonly Win32.Win32WindowHandle Win32 = default;

  [SupportedOSPlatform("linux")]
  [FieldOffset(sizeof(BackendType))]
  public readonly X11.X11WindowHandle X11 = default;



  public WindowHandle() { }


  [SupportedOSPlatform("windows")]
  public WindowHandle(in Win32.Win32WindowHandle win32)
  {
    this.Backend = BackendType.Win32;
    this.Win32 = win32;
  }

  [SupportedOSPlatform("linux")]
  public WindowHandle(in X11.X11WindowHandle x11)
  {
    this.Backend = BackendType.X11;
    this.X11 = x11;
  }
}