using System.Drawing;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PrimordialPower.Platform;


[StructLayout(LayoutKind.Explicit, Size = sizeof(BackendType) + HandleStructSize)]
public struct ViewportHandle
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
  public readonly Windows.Win32WindowHandle Win32 = default;

  [SupportedOSPlatform("linux")]
  [FieldOffset(sizeof(BackendType))]
  public readonly Linux.X11WindowHandle X11 = default;



  public ViewportHandle() { }


  [SupportedOSPlatform("windows")]
  public ViewportHandle(in Windows.Win32WindowHandle win32)
  {
    this.Backend = BackendType.Win32;
    this.Win32 = win32;
  }

  [SupportedOSPlatform("linux")]
  public ViewportHandle(in Linux.X11WindowHandle x11)
  {
    this.Backend = BackendType.X11;
    this.X11 = x11;
  }
}