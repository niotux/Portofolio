using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace QuantiX.Engine.Renderers.Quartz.Linux;

[SupportedOSPlatform("linux")]
internal static partial class GLXApi
{
  [StructLayout(LayoutKind.Sequential)]
  public struct XWindowAttributes
  {
    public int x, y, width, height, border_width, depth;
    public nint visual;          // Visual* — offset 24, this is what we need
    public nuint root;           // Window
    public int @class;
    public int bit_gravity;
    public int win_gravity;
    public int backing_store;
    public nuint backing_planes;
    public nuint backing_pixel;
    public int save_under;
    public nint colormap;
    public int map_installed;
    public int map_state;
    public long all_event_masks;
    public long your_event_mask;
    public long do_not_propagate_mask;
    public int override_redirect;
    public nint screen;          // Screen*
  }

  [StructLayout(LayoutKind.Sequential)]
  public struct XVisualInfo
  {
    public nint visual;          // Visual*
    public nuint visualid;       // VisualID (unsigned long)
    public int screen;
    public int depth;
    public int @class;
    public nuint red_mask;
    public nuint green_mask;
    public nuint blue_mask;
    public int colormap_size;
    public int bits_per_rgb;
  }

  [StructLayout(LayoutKind.Sequential)]
  public struct XVisual
  {
    public nint ext_data;   // XExtData*
    public nuint visualid;  // the ID we need
  }



  [LibraryImport("libGL.so.1", EntryPoint = "glXGetProcAddressARB", StringMarshalling = StringMarshalling.Utf8)]
  public static partial nint GetProcAddress(string name);

  [LibraryImport("libGL.so.1", EntryPoint = "glXChooseVisual")]
  public static partial nint ChooseVisual(nint display, int screen, [In] int[] attribList);

  [LibraryImport("libGL.so.1", EntryPoint = "glXCreateContext")]
  public static partial nint CreateContext(nint display, nint visualInfo, nint shareList, [MarshalAs(UnmanagedType.Bool)] bool direct);

  [LibraryImport("libGL.so.1", EntryPoint = "glXMakeCurrent")]
  [return: MarshalAs(UnmanagedType.Bool)]
  public static partial bool MakeCurrent(nint display, ulong drawable, nint context);

  [LibraryImport("libGL.so.1", EntryPoint = "glXDestroyContext")]
  public static partial void DestroyContext(nint display, nint context);

  [LibraryImport("libGL.so.1", EntryPoint = "glXSwapBuffers")]
  public static partial void SwapBuffers(nint display, ulong drawable);

  [LibraryImport("libX11.so.6", EntryPoint = "XGetWindowAttributes")]
  public static partial int XGetWindowAttributes(nint display, ulong window, out XWindowAttributes attrs);

  [LibraryImport("libX11.so.6", EntryPoint = "XGetVisualInfo")]
  public static partial nint XGetVisualInfo(nint display, long mask, ref XVisualInfo tmpl, out int count);

  public const long VisualIDMask = 0x1;
  public const int GLX_RGBA = 4;
  public const int GLX_DEPTH_SIZE = 12;
  public const int GLX_DOUBLEBUFFER = 5;
  public const int GLX_RED_SIZE = 8;
  public const int GLX_GREEN_SIZE = 9;
  public const int GLX_BLUE_SIZE = 10;
  public const int GLX_ALPHA_SIZE = 11;
  public const int GLX_STENCIL_SIZE = 13;
}
