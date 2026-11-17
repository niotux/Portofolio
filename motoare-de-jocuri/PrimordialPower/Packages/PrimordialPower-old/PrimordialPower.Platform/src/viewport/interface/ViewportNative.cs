using System.Drawing;
using System.Runtime.InteropServices;

namespace PrimordialPower.Platform;


[StructLayout(LayoutKind.Explicit, Size = sizeof(BackendType) + WindowInfoMaxSize)]
public struct ViewportNative
{
  public const int WindowInfoMaxSize = 128;



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


  [FieldOffset(sizeof(BackendType))]
  public readonly Win32.Win32WindowInfo Win32 = new();


  [FieldOffset(sizeof(BackendType))]
  public readonly X11.X11WindowInfo X11 = new();



  public ViewportNative() { }

  public ViewportNative(in Win32.Win32WindowInfo win32)
  {
    this.Backend = BackendType.Win32;
    this.Win32 = win32;
  }

  public ViewportNative(in X11.X11WindowInfo x11)
  {
    this.Backend = BackendType.X11;
    this.X11 = x11;
  }
}