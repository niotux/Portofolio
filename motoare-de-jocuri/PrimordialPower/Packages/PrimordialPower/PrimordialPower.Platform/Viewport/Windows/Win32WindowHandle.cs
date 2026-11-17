using System.Runtime.InteropServices;

namespace PrimordialPower.Platform.Windows;

[StructLayout(LayoutKind.Sequential)]
public struct Win32WindowHandle
{
  public required IntPtr HInstance { get; init; }
  public required IntPtr HWnd { get; init; }

  public Win32WindowHandle(IntPtr hInstance, IntPtr hWnd)
  {
    this.HInstance = hInstance;
    this.HWnd = hWnd;
  }
}
