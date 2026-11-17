using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace QuantiX.Engine.Renderers.Quartz.Windows;

[SupportedOSPlatform("windows")]
internal static partial class WGLApi
{
  [LibraryImport("opengl32.dll", EntryPoint = "wglGetProcAddress", StringMarshalling = StringMarshalling.Utf8)]
  private static partial nint WglGetProcAddress(string name);

  [LibraryImport("kernel32.dll", EntryPoint = "GetProcAddress", StringMarshalling = StringMarshalling.Utf8)]
  private static partial nint KernelGetProcAddress(nint hModule, string name);

  [LibraryImport("kernel32.dll", EntryPoint = "LoadLibraryW", StringMarshalling = StringMarshalling.Utf16)]
  private static partial nint LoadLibrary(string lpFileName);

  private static readonly nint _openglModule = LoadLibrary("opengl32.dll");

  public static nint GetProcAddress(string name)
  {
    nint addr = WglGetProcAddress(name);
    if (addr == 0 || addr == 1 || addr == 2 || addr == -1)
    {
      addr = KernelGetProcAddress(_openglModule, name);
    }
    return addr;
  }

  [LibraryImport("opengl32.dll", EntryPoint = "wglCreateContext")]
  public static partial nint CreateContext(nint hdc);

  [LibraryImport("opengl32.dll", EntryPoint = "wglMakeCurrent")]
  [return: MarshalAs(UnmanagedType.Bool)]
  public static partial bool MakeCurrent(nint hdc, nint hglrc);

  [LibraryImport("opengl32.dll", EntryPoint = "wglDeleteContext")]
  [return: MarshalAs(UnmanagedType.Bool)]
  public static partial bool DeleteContext(nint hglrc);

  [LibraryImport("user32.dll", EntryPoint = "GetDC")]
  public static partial nint GetDC(nint hWnd);

  [LibraryImport("user32.dll", EntryPoint = "ReleaseDC")]
  public static partial int ReleaseDC(nint hWnd, nint hdc);

  [LibraryImport("gdi32.dll", EntryPoint = "SetPixelFormat")]
  [return: MarshalAs(UnmanagedType.Bool)]
  public static partial bool SetPixelFormat(nint hdc, int format, ref PixelFormatDescriptor pfd);

  [LibraryImport("gdi32.dll", EntryPoint = "ChoosePixelFormat")]
  public static partial int ChoosePixelFormat(nint hdc, ref PixelFormatDescriptor pfd);

  [LibraryImport("gdi32.dll", EntryPoint = "SwapBuffers")]
  [return: MarshalAs(UnmanagedType.Bool)]
  public static partial bool SwapBuffers(nint hdc);

  [StructLayout(LayoutKind.Sequential)]
  public struct PixelFormatDescriptor
  {
    public ushort nSize;
    public ushort nVersion;
    public uint dwFlags;
    public byte iPixelType;
    public byte cColorBits;
    public byte cRedBits, cRedShift;
    public byte cGreenBits, cGreenShift;
    public byte cBlueBits, cBlueShift;
    public byte cAlphaBits, cAlphaShift;
    public byte cAccumBits;
    public byte cAccumRedBits, cAccumGreenBits, cAccumBlueBits, cAccumAlphaBits;
    public byte cDepthBits;
    public byte cStencilBits;
    public byte cAuxBuffers;
    public byte iLayerType;
    public byte bReserved;
    public uint dwLayerMask, dwVisibleMask, dwDamageMask;

    public const uint PFD_DRAW_TO_WINDOW = 0x00000004;
    public const uint PFD_SUPPORT_OPENGL = 0x00000020;
    public const uint PFD_DOUBLEBUFFER = 0x00000001;
    public const byte PFD_TYPE_RGBA = 0;
    public const byte PFD_MAIN_PLANE = 0;
  }
}
