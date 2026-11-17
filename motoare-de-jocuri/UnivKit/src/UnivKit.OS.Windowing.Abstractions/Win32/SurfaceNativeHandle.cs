using System.Runtime.InteropServices;

namespace UnivKit.OS.Windowing.Win32;

[StructLayout(LayoutKind.Sequential)]
public struct SurfaceNativeHandle
{
  public required IntPtr HInstance { get; init; }
  public required IntPtr HWnd { get; init; }

  public SurfaceNativeHandle(IntPtr hInstance, IntPtr hWnd)
  {
    this.HInstance = hInstance;
    this.HWnd = hWnd;
  }
}
