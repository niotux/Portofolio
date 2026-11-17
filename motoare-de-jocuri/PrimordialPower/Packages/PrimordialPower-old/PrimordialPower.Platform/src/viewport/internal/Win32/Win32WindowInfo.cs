using System.Runtime.InteropServices;

namespace PrimordialPower.Platform.Win32;

[StructLayout(LayoutKind.Sequential)]
public struct Win32WindowInfo
{
  public readonly IntPtr HInstance;
  public readonly IntPtr HWnd;

  public Win32WindowInfo(IntPtr hInstance, IntPtr hWnd)
  {
    this.HInstance = hInstance;
    this.HWnd = hWnd;
  }
}
