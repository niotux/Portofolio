using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace PrimordialPower.Platform.Linux;

internal static partial class X11
{
  static X11()
  {
    XInitThreads();
  }



  public enum Bool32 : int
  {
    False = 0,
    True = 1,
  }



  [Flags]
  public enum XEventMask : long
  {
    NoEventMask = 0L,
    KeyPressMask = 1L << 0,
    KeyReleaseMask = 1L << 1,
    ButtonPressMask = 1L << 2,
    ButtonReleaseMask = 1L << 3,
    EnterWindowMask = 1L << 4,
    LeaveWindowMask = 1L << 5,
    PointerMotionMask = 1L << 6,
    PointerMotionHintMask = 1L << 7,
    Button1MotionMask = 1L << 8,
    Button2MotionMask = 1L << 9,
    Button3MotionMask = 1L << 10,
    Button4MotionMask = 1L << 11,
    Button5MotionMask = 1L << 12,
    ButtonMotionMask = 1L << 13,
    KeymapStateMask = 1L << 14,
    ExposureMask = 1L << 15,
    VisibilityChangeMask = 1L << 16,
    StructureNotifyMask = 1L << 17,
    ResizeRedirectMask = 1L << 18,
    SubstructureNotifyMask = 1L << 19,
    SubstructureRedirectMask = 1L << 20,
    FocusChangeMask = 1L << 21,
    PropertyChangeMask = 1L << 22,
    ColormapChangeMask = 1L << 23,
    OwnerGrabButtonMask = 1L << 24,
  }



  public enum XEventType : int
  {
    FocusIn = 9,
    FocusOut = 10,
    Expose = 12,
    Configure = 22,
    ClientMessage = 33,
    DestroyWindow = 17,
    Map = 19,
    Unmap = 18,
  }



  [StructLayout(LayoutKind.Explicit, Size = 192)]
  public struct XEvent
  {
    [FieldOffset(0)]
    public XEventType Type;

    [FieldOffset(0)]
    public XExposeEvent XExpose;

    [FieldOffset(0)]
    public XConfigureEvent XConfigure;

    [FieldOffset(0)]
    public XClientMessageEvent XClient;

    [FieldOffset(0)]
    public XDestroyWindowEvent XDestroyWindow;

    [FieldOffset(0)]
    public XMapEvent XMap;

    [FieldOffset(0)]
    public XUnmapEvent XUnmap;

    [FieldOffset(0)]
    public XFocusChangeEvent XFocusChange;
  }



  [StructLayout(LayoutKind.Sequential)]
  public struct XExposeEvent
  {
    public XEventType Type;
    public UIntPtr Serial;
    public int SendEvent;
    public IntPtr Display;
    public ulong Window;
    public int X, Y;
    public int Width, Height;
    public int Count;
  }



  [StructLayout(LayoutKind.Sequential)]
  public struct XConfigureEvent
  {
    public XEventType Type;
    public UIntPtr Serial;
    public int SendEvent;
    public IntPtr Display;
    public ulong Event;
    public ulong Window;
    public int X, Y;
    public int Width, Height;
    public int BorderWidth;
    public ulong Above;
    public int OverrideRedirect;
  }



  [InlineArray(20)]
  public struct ByteBuffer20
  {
    private byte field;
  }

  [InlineArray(10)]
  public struct ShortBuffer10
  {
    private short field;
  }

  [InlineArray(5)]
  public struct LongBuffer5
  {
    private long field;
  }

  [StructLayout(LayoutKind.Explicit)]
  public struct DataUnion
  {
    [FieldOffset(0)]
    public ByteBuffer20 B;
    [FieldOffset(0)]
    public ShortBuffer10 S;
    [FieldOffset(0)]
    public LongBuffer5 L;
  }

  [StructLayout(LayoutKind.Sequential)]
  public struct XClientMessageEvent
  {
    public XEventType Type;
    public UIntPtr Serial;
    public int SendEvent;
    public IntPtr Display;
    public ulong Window;
    public UIntPtr MessageType;
    public int Format;
    public DataUnion Data;
  }



  [StructLayout(LayoutKind.Sequential)]
  public struct XDestroyWindowEvent
  {
    public XEventType Type;
    public UIntPtr Serial;
    public int SendEvent;
    public IntPtr Display;
    public ulong Event;
    public ulong Window;
  }



  [StructLayout(LayoutKind.Sequential)]
  public struct XMapEvent
  {
    public XEventType Type;
    public UIntPtr Serial;
    public int SendEvent;
    public IntPtr Display;
    public ulong Event;
    public ulong Window;
    public int OverrideRedirect;
  }



  [StructLayout(LayoutKind.Sequential)]
  public struct XUnmapEvent
  {
    public XEventType Type;
    public UIntPtr Serial;
    public int SendEvent;
    public IntPtr Display;
    public ulong Event;
    public ulong Window;
    public int FromConfigure;
  }



  [StructLayout(LayoutKind.Sequential)]
  public struct XFocusChangeEvent
  {
    public XEventType Type;
    public nuint Serial;
    public int SendEvent;
    public nint Display;
    public ulong Window;
    public int Mode;
    public int Detail;
  }



  [LibraryImport(LibNames.X11, StringMarshalling = StringMarshalling.Utf8)]
  public static partial IntPtr XOpenDisplay(string? displayName);


  [DllImport(LibNames.X11)]
  public static extern int XInitThreads();


  [LibraryImport(LibNames.X11)]
  public static partial int XDefaultScreen(IntPtr display);


  [LibraryImport(LibNames.X11)]
  public static partial ulong XRootWindow(IntPtr display, int screenNumber);


  [LibraryImport(LibNames.X11)]
  public static partial ulong XBlackPixel(IntPtr display, int screenNumber);


  [LibraryImport(LibNames.X11)]
  public static partial ulong XWhitePixel(IntPtr display, int screenNumber);


  [LibraryImport(LibNames.X11)]
  public static partial ulong XCreateSimpleWindow(
    IntPtr display,
    ulong parent,
    int x,
    int y,
    uint width,
    uint height,
    uint borderWidth,
    ulong border,
    ulong background
  );


  [LibraryImport(LibNames.X11)]
  public static partial int XSelectInput(IntPtr display, ulong window, XEventMask eventMask);


  [LibraryImport(LibNames.X11)]
  public static partial int XMapWindow(IntPtr display, ulong window);


  [LibraryImport(LibNames.X11)]
  public static partial int XMoveWindow(IntPtr display, ulong window, int x, int y);


  [LibraryImport(LibNames.X11)]
  public static partial int XResizeWindow(IntPtr display, ulong window, uint width, uint height);


  [LibraryImport(LibNames.X11)]
  public static partial int XMoveResizeWindow(IntPtr display, ulong window, int x, int y, uint width, uint height);


  [LibraryImport(LibNames.X11, StringMarshalling = StringMarshalling.Utf8)]
  public static partial int XStoreName(IntPtr display, ulong window, string windowName);


  [LibraryImport(LibNames.X11)]
  public static partial int XNextEvent(IntPtr display, out XEvent eventReturn);


  [LibraryImport(LibNames.X11)]
  public static partial int XPending(IntPtr display);


  [LibraryImport(LibNames.X11)]
  public static partial int XFlush(IntPtr display);


  [LibraryImport(LibNames.X11)]
  public static partial int XDestroyWindow(IntPtr display, ulong window);


  [LibraryImport(LibNames.X11)]
  public static partial int XCloseDisplay(IntPtr display);


  [LibraryImport(LibNames.X11, StringMarshalling = StringMarshalling.Utf8)]
  public static partial UIntPtr XInternAtom(IntPtr display, string atomName, Bool32 onlyIfExists);


  [LibraryImport(LibNames.X11)]
  public static partial int XSetWMProtocols(IntPtr display, ulong window, ref UIntPtr protocols, int count);


  [LibraryImport(LibNames.X11)]
  public static partial int XSendEvent(IntPtr display, ulong window, Bool32 propagate, XEventMask eventMask, ref XEvent eventSend);


  [LibraryImport(LibNames.X11)]
  public static partial void XPutBackEvent(IntPtr display, ref XEvent xevent);


  [LibraryImport(LibNames.X11)]
  public static partial int XSetInputFocus(IntPtr display, ulong window, int revert_to, UIntPtr time);

  // X11 Focus Constants
  public const int RevertToNone = 0;
  public const int RevertToPointerRoot = 1;
  public const int RevertToParent = 2;


  // ICCCM (Inter-Client Communication Conventions Manual) - Core Protocol
  public const string WM_PROTOCOLS = "WM_PROTOCOLS";
  public const string WM_DELETE_WINDOW = "WM_DELETE_WINDOW";
  public const string WM_TAKE_FOCUS = "WM_TAKE_FOCUS";
  public const string WM_STATE = "WM_STATE";
  public const string WM_CHANGE_STATE = "WM_CHANGE_STATE";
  public const string WM_CLASS = "WM_CLASS";
  public const string WM_NAME = "WM_NAME";
  public const string WM_ICON_NAME = "WM_ICON_NAME";
  public const string WM_CLIENT_MACHINE = "WM_CLIENT_MACHINE";

  // User defined
  public const string WM_USER_QUIT = "WM_USER_QUIT";
  public const string WM_USER_WINDOW_INIT = "WM_USER_WINDOW_INIT";
  public const string WM_USER_NULL = "WM_USER_NULL";
  public const string WM_USER_DESTROY_WINDOW = "WM_USER_DESTROY_WINDOW";
  public const string WM_USER_FOCUS_WINDOW = "WM_USER_FOCUS_WINDOW";

  // EWMH (Extended Window Manager Hints) - Modern WM Protocol
  public const string _NET_WM_NAME = "_NET_WM_NAME";
  public const string _NET_WM_ICON_NAME = "_NET_WM_ICON_NAME";
  public const string _NET_WM_ICON = "_NET_WM_ICON";
  public const string _NET_WM_PID = "_NET_WM_PID";
  public const string _NET_WM_PING = "_NET_WM_PING";

  // Window State
  public const string _NET_WM_STATE = "_NET_WM_STATE";
  public const string _NET_WM_STATE_MODAL = "_NET_WM_STATE_MODAL";
  public const string _NET_WM_STATE_STICKY = "_NET_WM_STATE_STICKY";
  public const string _NET_WM_STATE_MAXIMIZED_VERT = "_NET_WM_STATE_MAXIMIZED_VERT";
  public const string _NET_WM_STATE_MAXIMIZED_HORZ = "_NET_WM_STATE_MAXIMIZED_HORZ";
  public const string _NET_WM_STATE_SHADED = "_NET_WM_STATE_SHADED";
  public const string _NET_WM_STATE_SKIP_TASKBAR = "_NET_WM_STATE_SKIP_TASKBAR";
  public const string _NET_WM_STATE_SKIP_PAGER = "_NET_WM_STATE_SKIP_PAGER";
  public const string _NET_WM_STATE_HIDDEN = "_NET_WM_STATE_HIDDEN";
  public const string _NET_WM_STATE_FULLSCREEN = "_NET_WM_STATE_FULLSCREEN";
  public const string _NET_WM_STATE_ABOVE = "_NET_WM_STATE_ABOVE";
  public const string _NET_WM_STATE_BELOW = "_NET_WM_STATE_BELOW";
  public const string _NET_WM_STATE_DEMANDS_ATTENTION = "_NET_WM_STATE_DEMANDS_ATTENTION";
  public const string _NET_WM_STATE_FOCUSED = "_NET_WM_STATE_FOCUSED";

  // Window Type
  public const string _NET_WM_WINDOW_TYPE = "_NET_WM_WINDOW_TYPE";
  public const string _NET_WM_WINDOW_TYPE_DESKTOP = "_NET_WM_WINDOW_TYPE_DESKTOP";
  public const string _NET_WM_WINDOW_TYPE_DOCK = "_NET_WM_WINDOW_TYPE_DOCK";
  public const string _NET_WM_WINDOW_TYPE_TOOLBAR = "_NET_WM_WINDOW_TYPE_TOOLBAR";
  public const string _NET_WM_WINDOW_TYPE_MENU = "_NET_WM_WINDOW_TYPE_MENU";
  public const string _NET_WM_WINDOW_TYPE_UTILITY = "_NET_WM_WINDOW_TYPE_UTILITY";
  public const string _NET_WM_WINDOW_TYPE_SPLASH = "_NET_WM_WINDOW_TYPE_SPLASH";
  public const string _NET_WM_WINDOW_TYPE_DIALOG = "_NET_WM_WINDOW_TYPE_DIALOG";
  public const string _NET_WM_WINDOW_TYPE_NORMAL = "_NET_WM_WINDOW_TYPE_NORMAL";

  // Text encoding
  public const string UTF8_STRING = "UTF8_STRING";
  public const string COMPOUND_TEXT = "COMPOUND_TEXT";
  public const string STRING = "STRING";

  // Clipboard/Selection
  public const string CLIPBOARD = "CLIPBOARD";
  public const string PRIMARY = "PRIMARY";
  public const string SECONDARY = "SECONDARY";
  public const string TARGETS = "TARGETS";
}
