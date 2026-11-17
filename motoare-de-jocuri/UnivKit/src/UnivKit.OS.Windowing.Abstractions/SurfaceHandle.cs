using System.Runtime.InteropServices;

namespace UnivKit.OS.Windowing;

[StructLayout(LayoutKind.Explicit)]
public record struct SurfaceHandle
{
  public SurfaceHandle()
  {
    this.Backend = SurfaceBackend.None;
  }

  public SurfaceHandle(Win32.SurfaceNativeHandle handle)
  {
    this.Backend = SurfaceBackend.Win32;
    this.Win32 = handle;
  }

  public SurfaceHandle(X11.SurfaceNativeHandle handle)
  {
    this.Backend = SurfaceBackend.X11;
    this.X11 = handle;
  }

  public SurfaceHandle(Wayland.SurfaceNativeHandle handle)
  {
    this.Backend = SurfaceBackend.Wayland;
    this.Wayland = handle;
  }

  public SurfaceHandle(Cocoa.SurfaceNativeHandle handle)
  {
    this.Backend = SurfaceBackend.Cocoa;
    this.Cocos = handle;
  }

  public SurfaceHandle(Android.SurfaceNativeHandle handle)
  {
    this.Backend = SurfaceBackend.Android;
    this.Android = handle;
  }

  public SurfaceHandle(Browser.SurfaceNativeHandle handle)
  {
    this.Backend = SurfaceBackend.Browser;
    this.Browser = handle;
  }

  [FieldOffset(0)]
  public readonly SurfaceBackend Backend;

  [FieldOffset(sizeof(SurfaceBackend))]
  public readonly Win32.SurfaceNativeHandle Win32;

  [FieldOffset(sizeof(SurfaceBackend))]
  public readonly X11.SurfaceNativeHandle X11;

  [FieldOffset(sizeof(SurfaceBackend))]
  public readonly Wayland.SurfaceNativeHandle Wayland;

  [FieldOffset(sizeof(SurfaceBackend))]
  public readonly Cocoa.SurfaceNativeHandle Cocos;

  [FieldOffset(sizeof(SurfaceBackend))]
  public readonly Android.SurfaceNativeHandle Android;

  [FieldOffset(sizeof(SurfaceBackend))]
  public readonly Browser.SurfaceNativeHandle Browser;
}