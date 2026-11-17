using System.Runtime.InteropServices;

namespace UnivKit.OS.Windowing.X11;

[StructLayout(LayoutKind.Sequential)]
public struct SurfaceNativeHandle
{
  public required IntPtr Display { get; init; }
  public required ulong Window { get; init; }

  public SurfaceNativeHandle(IntPtr display, ulong window)
  {
    this.Display = display;
    this.Window = window;
  }
}
