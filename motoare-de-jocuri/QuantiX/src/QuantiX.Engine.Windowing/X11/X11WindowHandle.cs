using System.Runtime.InteropServices;

namespace QuantiX.Engine.Windowing.X11;

[StructLayout(LayoutKind.Sequential)]
public struct X11WindowHandle
{
  public required IntPtr Display { get; init; }
  public required ulong Window { get; init; }

  public X11WindowHandle(IntPtr display, ulong window)
  {
    this.Display = display;
    this.Window = window;
  }
}
