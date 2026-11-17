using System.Runtime.InteropServices;

namespace PrimordialPower.Platform.X11;

[StructLayout(LayoutKind.Sequential)]
public struct X11WindowInfo
{
  public readonly IntPtr Display;
  public readonly ulong Window;

  public X11WindowInfo(IntPtr display, ulong window)
  {
    this.Display = display;
    this.Window = window;
  }
}
