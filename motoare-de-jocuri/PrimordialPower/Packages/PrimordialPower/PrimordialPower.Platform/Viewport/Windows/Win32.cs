using System.Runtime.InteropServices;

namespace PrimordialPower.Platform.Windows;

internal static partial class Win32
{
  public delegate IntPtr WNDPROC(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);



  [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
  public struct WNDCLASSW
  {
    public WNDCLASSW() { }

    public required uint Style;
    public required WNDPROC LPFNWndProc;
    public int CBClsExtra = 0;
    public int CBWndExtra = 0;
    public IntPtr HInstance = GetModuleHandleW(null);
    public IntPtr HIcon = IntPtr.Zero;
    public IntPtr HCursor = LoadCursorW(GetModuleHandleW(null), IDC_ARROW);
    public IntPtr HBRBackground = GetStockObject(BLACK_BRUSH);
    public IntPtr LPSZMenuName = IntPtr.Zero;
    public required string LPSZClassName;
  }



  [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
  public struct MSG
  {
    public IntPtr HWnd;
    public uint Message;
    public IntPtr WParam;
    public IntPtr LParam;
    public uint Time;
    public POINT Point;
  }



  [StructLayout(LayoutKind.Sequential)]
  public struct POINT
  {
    public int X;
    public int Y;
  }



  [StructLayout(LayoutKind.Sequential)]
  public struct RECT
  {
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;

    public int Width => Right - Left;
    public int Height => Bottom - Top;
  }



  [DllImport(LibNames.User, CharSet = CharSet.Unicode)]
  public static extern ushort RegisterClassW(ref WNDCLASSW lpWndClass);


  [DllImport(LibNames.User, CharSet = CharSet.Unicode, SetLastError = true)]
  public static extern IntPtr CreateWindowExW(
      uint dwExStyle,
      string lpClassName,
      string lpWindowName,
      uint dwStyle,
      int x, int y, int nWidth, int nHeight,
      IntPtr hWndParent,
      IntPtr hMenu,
      IntPtr hInstance,
      IntPtr lpParam
  );


  [DllImport(LibNames.User)]
  public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);


  [DllImport(LibNames.User)]
  public static extern bool IsWindowVisible(IntPtr hWnd);


  [DllImport(LibNames.User)]
  public static extern IntPtr GetForegroundWindow();


  [DllImport(LibNames.User)]
  public static extern bool SetForegroundWindow(IntPtr hWnd);


  [DllImport(LibNames.User)]
  public static extern bool UpdateWindow(IntPtr hWnd);


  [DllImport(LibNames.User)]
  public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);


  [DllImport(LibNames.User)]
  public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);


  [DllImport(LibNames.User)]
  public static extern bool AdjustWindowRectEx(out RECT lpRect, uint dwStyle, bool bMenu, uint dwExStyle);


  [DllImport(LibNames.User, SetLastError = true)]
  public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);



  [LibraryImport(LibNames.User, StringMarshalling = StringMarshalling.Utf16)]
  [return: MarshalAs(UnmanagedType.Bool)]
  public static partial bool SetWindowTextW(IntPtr hWnd, string lpString);


  [LibraryImport(LibNames.User, StringMarshalling = StringMarshalling.Utf16)]
  public static partial int GetMessageW(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);


  [LibraryImport(LibNames.User, StringMarshalling = StringMarshalling.Utf16)]
  [return: MarshalAs(UnmanagedType.Bool)]
  public static partial bool PeekMessageW(
      out MSG lpMsg,
      IntPtr hWnd,
      uint wMsgFilterMin,
      uint wMsgFilterMax,
      uint wRemoveMsg
  );


  [LibraryImport(LibNames.User)]
  public static partial IntPtr SendMessageW(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);


  [LibraryImport(LibNames.User)]
  [return: MarshalAs(UnmanagedType.Bool)]
  public static partial bool PostMessageW(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);


  [LibraryImport(LibNames.User)]
  [return: MarshalAs(UnmanagedType.Bool)]
  public static partial bool PostThreadMessageW(uint idThread, uint Msg, IntPtr wParam, IntPtr lParam);


  [LibraryImport(LibNames.User)]
  [return: MarshalAs(UnmanagedType.Bool)]
  public static partial bool TranslateMessage(ref MSG lpMsg);


  [LibraryImport(LibNames.User, StringMarshalling = StringMarshalling.Utf16)]
  public static partial IntPtr DispatchMessageW(ref MSG lpMsg);


  [LibraryImport(LibNames.User, StringMarshalling = StringMarshalling.Utf16)]
  public static partial IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);



  [LibraryImport(LibNames.User)]
  [return: MarshalAs(UnmanagedType.Bool)]
  public static partial bool DestroyWindow(IntPtr hWnd);


  [LibraryImport(LibNames.User)]
  public static partial void PostQuitMessage(int nExitCode);


  [LibraryImport(LibNames.User)]
  [return: MarshalAs(UnmanagedType.Bool)]
  public static partial bool ValidateRect(IntPtr hWnd, IntPtr lpRect);

  //                                                                                                            This is not a 'pointer', it's a 'refference' to another IJoke you will never .Get() :P.
  [LibraryImport(LibNames.DWMAPI)]                                                                              //     V
  public static partial int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize); // (Can*) make box, boxular, squaricle, rough, rectangular, actual rectangular window


  [LibraryImport(LibNames.User)]
  public static partial IntPtr LoadCursorW(IntPtr hInstance, int lpCursorName);


  [LibraryImport(LibNames.User)]
  public static partial IntPtr SetCursor(IntPtr hCursor);


  [LibraryImport(LibNames.GDI32)]
  public static partial IntPtr GetStockObject(int fnObject);



  // Window Class Styles (used in WNDCLASS.style)
  public const uint CS_VREDRAW = 0x0001;
  public const uint CS_HREDRAW = 0x0002;
  public const uint CS_DBLCLKS = 0x0008;
  public const uint CS_OWNDC = 0x0020;
  public const uint CS_CLASSDC = 0x0040;
  public const uint CS_PARENTDC = 0x0080;
  public const uint CS_NOCLOSE = 0x0200;
  public const uint CS_SAVEBITS = 0x0800;
  public const uint CS_BYTEALIGNCLIENT = 0x1000;
  public const uint CS_BYTEALIGNWINDOW = 0x2000;
  public const uint CS_GLOBALCLASS = 0x4000;


  // Window Styles (used in CreateWindowEx)
  public const uint WS_OVERLAPPED = 0x00000000;
  public const uint WS_POPUP = 0x80000000;
  public const uint WS_CHILD = 0x40000000;
  public const uint WS_MINIMIZE = 0x20000000;
  public const uint WS_VISIBLE = 0x10000000;
  public const uint WS_DISABLED = 0x08000000;
  public const uint WS_CLIPSIBLINGS = 0x04000000;
  public const uint WS_CLIPCHILDREN = 0x02000000;
  public const uint WS_MAXIMIZE = 0x01000000;
  public const uint WS_CAPTION = 0x00C00000;
  public const uint WS_BORDER = 0x00800000;
  public const uint WS_DLGFRAME = 0x00400000;
  public const uint WS_VSCROLL = 0x00200000;
  public const uint WS_HSCROLL = 0x00100000;
  public const uint WS_SYSMENU = 0x00080000;
  public const uint WS_THICKFRAME = 0x00040000;
  public const uint WS_GROUP = 0x00020000;
  public const uint WS_TABSTOP = 0x00010000;
  public const uint WS_MINIMIZEBOX = 0x00020000;
  public const uint WS_MAXIMIZEBOX = 0x00010000;
  public const uint WS_OVERLAPPEDWINDOW =
    WS_OVERLAPPED |
    WS_CAPTION |
    WS_SYSMENU |
    WS_THICKFRAME |
    WS_MINIMIZEBOX |
    WS_MAXIMIZEBOX;


  // Extended Window Styles
  public const uint WS_EX_DLGMODALFRAME = 0x00000001;
  public const uint WS_EX_NOPARENTNOTIFY = 0x00000004;
  public const uint WS_EX_TOPMOST = 0x00000008;
  public const uint WS_EX_ACCEPTFILES = 0x00000010;
  public const uint WS_EX_TRANSPARENT = 0x00000020;
  public const uint WS_EX_LAYERED = 0x00080000;


  // Window Messages
  public const uint WM_NULL = 0x0000;
  public const uint WM_CREATE = 0x0001;
  public const uint WM_DESTROY = 0x0002;
  public const uint WM_MOVE = 0x0003;
  public const uint WM_SIZE = 0x0005;
  public const uint WM_ACTIVATE = 0x0006;
  public const uint WM_SETFOCUS = 0x0007;
  public const uint WM_KILLFOCUS = 0x0008;
  public const uint WM_PAINT = 0x000F;
  public const uint WM_CLOSE = 0x0010;
  public const uint WM_QUIT = 0x0012;
  public const uint WM_ERASEBKGND = 0x0014;
  public const uint WM_SHOWWINDOW = 0x0018;

  public const uint WM_USER_WINDOW_INIT = 0x0400 + 1;
  public const uint WM_USER_WINDOW_DESTROY = 0x0400 + 2;
  public const uint WM_USER_QUIT = 0x0400 + 3;


  public const uint WM_KEYDOWN = 0x0100;
  public const uint WM_KEYUP = 0x0101;
  public const uint WM_CHAR = 0x0102;
  public const uint WM_MOUSEMOVE = 0x0200;
  public const uint WM_LBUTTONDOWN = 0x0201;
  public const uint WM_LBUTTONUP = 0x0202;
  public const uint WM_RBUTTONDOWN = 0x0204;
  public const uint WM_RBUTTONUP = 0x0205;
  public const uint WM_MBUTTONDOWN = 0x0207;
  public const uint WM_MBUTTONUP = 0x0208;
  public const uint WM_MOUSEWHEEL = 0x020A;
  public const uint WM_SETCURSOR = 0x0020;


  public const uint PM_NOREMOVE = 0x0000;
  public const uint PM_REMOVE = 0x0001;
  public const uint PM_NOYIELD = 0x0002;


  // System Metrics and Defaults
  public const int CW_USEDEFAULT = unchecked((int)0x80000000);


  // ShowWindow Commands
  public const int SW_HIDE = 0;
  public const int SW_SHOWNORMAL = 1;
  public const int SW_SHOW = 5;
  public const int SW_MINIMIZE = 6;
  public const int SW_RESTORE = 9;


  // SetWindowPos Commands
  public const uint SWP_NOMOVE = 0x0002;
  public const uint SWP_NOZORDER = 0x0004;
  public const uint SWP_NOACTIVATE = 0x0010;
  public const uint SWP_NOSIZE = 0x0001;
  public const uint SWP_SHOWWINDOW = 0x0040;
  public const uint SWP_HIDEWINDOW = 0x0080;
  public const uint SWP_NOCOPYBITS = 0x0100;
  public const uint SWP_NOREDRAW = 0x0008;


  // Stock Object Constants (GDI Brushes)
  public const int WHITE_BRUSH = 0;
  public const int LTGRAY_BRUSH = 1;
  public const int GRAY_BRUSH = 2;
  public const int DKGRAY_BRUSH = 3;
  public const int BLACK_BRUSH = 4;
  public const int NULL_BRUSH = 5;
  public const int HOLLOW_BRUSH = 5;


  // Cursors
  public const int IDC_ARROW = 32512;
  public const int IDC_IBEAM = 32513;
  public const int IDC_WAIT = 32514;
  public const int IDC_CROSS = 32515;
  public const int IDC_UPARROW = 32516;
  public const int IDC_SIZE = 32640;
  public const int IDC_ICON = 32641;
  public const int IDC_SIZENWSE = 32642;
  public const int IDC_SIZENESW = 32643;
  public const int IDC_SIZEWE = 32644;
  public const int IDC_SIZENS = 32645;
  public const int IDC_SIZEALL = 32646;
  public const int IDC_NO = 32648;
  public const int IDC_HAND = 32649;
  public const int IDC_APPSTARTING = 32650;

  // HT
  public const int HTERROR = -2;
  public const int HTTRANSPARENT = -1;
  public const int HTNOWHERE = 0;
  public const int HTCLIENT = 1;
  public const int HTCAPTION = 2;
  public const int HTSYSMENU = 3;
  public const int HTGROWBOX = 4;
  public const int HTMENU = 5;
  public const int HTHSCROLL = 6;
  public const int HTVSCROLL = 7;
  public const int HTMINBUTTON = 8;
  public const int HTMAXBUTTON = 9;
  public const int HTLEFT = 10;
  public const int HTRIGHT = 11;
  public const int HTTOP = 12;
  public const int HTTOPLEFT = 13;
  public const int HTTOPRIGHT = 14;
  public const int HTBOTTOM = 15;
  public const int HTBOTTOMLEFT = 16;
  public const int HTBOTTOMRIGHT = 17;
  public const int HTBORDER = 18;
  public const int HTOBJECT = 19;
  public const int HTCLOSE = 20;
  public const int HTHELP = 21;


  // DWMA
  public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
  public const int DWMWCP_DEFAULT = 0;
  public const int DWMWCP_DONOTROUND = 1;
  public const int DWMWCP_ROUND = 2;
  public const int DWMWCP_ROUNDSMALL = 3;

  public const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
  public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
  public const int DWMWCP_DARK_MODE = 1;
  public const int DWMWCP_LIGHT_MODE = 0;


  // SIZE
  public const IntPtr SIZE_RESTORED = 0;
  public const IntPtr SIZE_MINIMIZED = 1;
  public const IntPtr SIZE_MAXIMIZED = 2;
  public const IntPtr SIZE_MAXSHOW = 3;
  public const IntPtr SIZE_MAXHIDE = 4;
}

