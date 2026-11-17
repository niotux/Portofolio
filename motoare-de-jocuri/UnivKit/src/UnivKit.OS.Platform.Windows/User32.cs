using System.Runtime.InteropServices;

namespace UnivKit.OS.Platform.Windows;

public static partial class User32
{
  public const string LibName = "user32.dll";


  public delegate IntPtr WNDPROC(IntPtr hWnd, WM msg, IntPtr wParam, IntPtr lParam);


  [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
  public struct WNDCLASSW
  {
    public WNDCLASSW() { }

    public required CS Style;
    public required WNDPROC LPFNWndProc;
    public int CBClsExtra = 0;
    public int CBWndExtra = 0;
    public IntPtr HInstance = Kernel32.GetModuleHandleW(null);
    public IntPtr HIcon = IntPtr.Zero;
    public IntPtr HCursor = LoadCursorW(Kernel32.GetModuleHandleW(null), IDC.ARROW);
    public IntPtr HBRBackground = GDI32.GetStockObject(StockObject.BLACK_BRUSH);
    public IntPtr LPSZMenuName = IntPtr.Zero;
    public required string LPSZClassName;
  }


  [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
  public struct MSG
  {
    public IntPtr HWnd;
    public WM Message;
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

  [DllImport(LibName, CharSet = CharSet.Unicode)]
  public static extern ushort RegisterClassW(ref WNDCLASSW lpWndClass);


  [DllImport(LibName, CharSet = CharSet.Unicode, SetLastError = true)]
  public static extern IntPtr CreateWindowExW(
      WS dwExStyle,
      string lpClassName,
      string lpWindowName,
      WS dwStyle,
      int x, int y, int nWidth, int nHeight,
      IntPtr hWndParent,
      IntPtr hMenu,
      IntPtr hInstance,
      IntPtr lpParam
  );


  [DllImport(LibName)]
  public static extern bool ShowWindow(IntPtr hWnd, SW nCmdShow);


  [DllImport(LibName)]
  public static extern bool IsWindowVisible(IntPtr hWnd);


  [DllImport(LibName)]
  public static extern IntPtr GetForegroundWindow();


  [DllImport(LibName)]
  public static extern bool SetForegroundWindow(IntPtr hWnd);


  [DllImport(LibName)]
  public static extern bool UpdateWindow(IntPtr hWnd);


  [DllImport(LibName)]
  public static extern void SetFocus(IntPtr hWnd);


  [DllImport(LibName)]
  public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);


  [DllImport(LibName)]
  public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);


  [DllImport(LibName)]
  public static extern bool AdjustWindowRectEx(out RECT lpRect, WS dwStyle, bool bMenu, WS dwExStyle);


  [DllImport(LibName, SetLastError = true)]
  public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, SWP uFlags);



  [LibraryImport(LibName, StringMarshalling = StringMarshalling.Utf16)]
  [return: MarshalAs(UnmanagedType.Bool)]
  public static partial bool SetWindowTextW(IntPtr hWnd, string lpString);


  [LibraryImport(LibName, StringMarshalling = StringMarshalling.Utf16)]
  public static partial int GetMessageW(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);


  [LibraryImport(LibName, StringMarshalling = StringMarshalling.Utf16)]
  [return: MarshalAs(UnmanagedType.Bool)]
  public static partial bool PeekMessageW(
      out MSG lpMsg,
      IntPtr hWnd,
      uint wMsgFilterMin,
      uint wMsgFilterMax,
      PM wRemoveMsg
  );


  [LibraryImport(LibName)]
  public static partial IntPtr SendMessageW(IntPtr hWnd, WM Msg, IntPtr wParam, IntPtr lParam);


  [LibraryImport(LibName)]
  [return: MarshalAs(UnmanagedType.Bool)]
  public static partial bool PostMessageW(IntPtr hWnd, WM Msg, IntPtr wParam, IntPtr lParam);


  [LibraryImport(LibName)]
  [return: MarshalAs(UnmanagedType.Bool)]
  public static partial bool PostThreadMessageW(uint idThread, WM Msg, IntPtr wParam, IntPtr lParam);


  [LibraryImport(LibName)]
  [return: MarshalAs(UnmanagedType.Bool)]
  public static partial bool TranslateMessage(ref MSG lpMsg);


  [LibraryImport(LibName, StringMarshalling = StringMarshalling.Utf16)]
  public static partial IntPtr DispatchMessageW(ref MSG lpMsg);


  [LibraryImport(LibName, StringMarshalling = StringMarshalling.Utf16)]
  public static partial IntPtr DefWindowProcW(IntPtr hWnd, WM msg, IntPtr wParam, IntPtr lParam);



  [LibraryImport(LibName)]
  [return: MarshalAs(UnmanagedType.Bool)]
  public static partial bool DestroyWindow(IntPtr hWnd);


  [LibraryImport(LibName)]
  public static partial void PostQuitMessage(int nExitCode);


  [LibraryImport(LibName)]
  [return: MarshalAs(UnmanagedType.Bool)]
  public static partial bool ValidateRect(IntPtr hWnd, IntPtr lpRect);


  [LibraryImport(LibName)]
  public static partial IntPtr LoadCursorW(IntPtr hInstance, IDC lpCursorName);


  [LibraryImport(LibName)]
  public static partial IntPtr SetCursor(IntPtr hCursor);


  [Flags]
  public enum CS : uint
  {
    VREDRAW = 0x0001,
    HREDRAW = 0x0002,
    DBLCLKS = 0x0008,
    OWNDC = 0x0020,
    CLASSDC = 0x0040,
    PARENTDC = 0x0080,
    NOCLOSE = 0x0200,
    SAVEBITS = 0x0800,
    BYTEALIGNCLIENT = 0x1000,
    BYTEALIGNWINDOW = 0x2000,
    GLOBALCLASS = 0x4000,
  }


  // Window Styles (used in CreateWindowEx)
  [Flags]
  public enum WS : uint
  {
    OVERLAPPED = 0x00000000,
    POPUP = 0x80000000,
    CHILD = 0x40000000,
    MINIMIZE = 0x20000000,
    VISIBLE = 0x10000000,
    DISABLED = 0x08000000,
    CLIPSIBLINGS = 0x04000000,
    CLIPCHILDREN = 0x02000000,
    MAXIMIZE = 0x01000000,
    CAPTION = 0x00C00000,
    BORDER = 0x00800000,
    DLGFRAME = 0x00400000,
    VSCROLL = 0x00200000,
    HSCROLL = 0x00100000,
    SYSMENU = 0x00080000,
    THICKFRAME = 0x00040000,
    GROUP = 0x00020000,
    TABSTOP = 0x00010000,
    MINIMIZEBOX = 0x00020000,
    MAXIMIZEBOX = 0x00010000,
    OVERLAPPEDWINDOW = 0x0
      | OVERLAPPED
      | CAPTION
      | SYSMENU
      | THICKFRAME
      | MINIMIZEBOX
      | MAXIMIZEBOX,

    EX_DLGMODALFRAME = 0x00000001,
    EX_NOPARENTNOTIFY = 0x00000004,
    EX_TOPMOST = 0x00000008,
    EX_ACCEPTFILES = 0x00000010,
    EX_TRANSPARENT = 0x00000020,
    EX_LAYERED = 0x00080000,
  }


  // Window Messages
  public enum WM : uint
  {
    NULL = 0x0000,
    CREATE = 0x0001,
    DESTROY = 0x0002,
    MOVE = 0x0003,
    SIZE = 0x0005,
    ACTIVATE = 0x0006,
    SETFOCUS = 0x0007,
    SETTEXT = 0x000C,
    KILLFOCUS = 0x0008,
    PAINT = 0x000F,
    CLOSE = 0x0010,
    QUIT = 0x0012,
    ERASEBKGND = 0x0014,
    SHOWWINDOW = 0x0018,

    USER_NULL = 0x0400 + 1,
    USER_WINDOW_DESTROY = 0x0400 + 2,
    USER_QUIT = 0x0400 + 3,

    KEYDOWN = 0x0100,
    KEYUP = 0x0101,
    CHAR = 0x0102,
    MOUSEMOVE = 0x0200,
    LBUTTONDOWN = 0x0201,
    LBUTTONUP = 0x0202,
    RBUTTONDOWN = 0x0204,
    RBUTTONUP = 0x0205,
    MBUTTONDOWN = 0x0207,
    MBUTTONUP = 0x0208,
    MOUSEWHEEL = 0x020A,
    SETCURSOR = 0x0020,
  }


  public enum PM : uint
  {
    NOREMOVE = 0x0000,
    REMOVE = 0x0001,
    NOYIELD = 0x0002,
  }


  // ShowWindow Commands
  public enum SW : int
  {
    HIDE = 0,
    SHOWNORMAL = 1,
    SHOWMINIMIZED = 2,
    SHOWMAXIMIZED = 3,
    SHOWNOACTIVATE = 4,
    SHOW = 5,
    MINIMIZE = 6,
    SHOWMINNOACTIVE = 7,
    SHOWNA = 8,
    RESTORE = 9,
    SHOWDEFAULT = 10,
    FORCEMINIMIZE = 11,
  }


  // SetWindowPos Commands
  [Flags]
  public enum SWP : uint
  {
    NOMOVE = 0x0002,
    NOZORDER = 0x0004,
    NOACTIVATE = 0x0010,
    NOSIZE = 0x0001,
    SHOWWINDOW = 0x0040,
    HIDEWINDOW = 0x0080,
    NOCOPYBITS = 0x0100,
    NOREDRAW = 0x0008,
  }


  // Cursor IDs
  public enum IDC : int
  {
    ARROW = 32512,
    IBEAM = 32513,
    WAIT = 32514,
    CROSS = 32515,
    UPARROW = 32516,
    SIZE = 32640,
    ICON = 32641,
    SIZENWSE = 32642,
    SIZENESW = 32643,
    SIZEWE = 32644,
    SIZENS = 32645,
    SIZEALL = 32646,
    NO = 32648,
    HAND = 32649,
    APPSTARTING = 32650,
  }


  // Stock Objects (for GetStockObject)
  public enum StockObject : int
  {
    WHITE_BRUSH = 0,
    LTGRAY_BRUSH = 1,
    GRAY_BRUSH = 2,
    DKGRAY_BRUSH = 3,
    BLACK_BRUSH = 4,
    NULL_BRUSH = 5,
    HOLLOW_BRUSH = 5,
  }


  // System Metrics and Defaults
  public const int CW_USEDEFAULT = unchecked((int)0x80000000);


  // Hit Test Results
  public enum HT : int
  {
    ERROR = -2,
    TRANSPARENT = -1,
    NOWHERE = 0,
    CLIENT = 1,
    CAPTION = 2,
    SYSMENU = 3,
    GROWBOX = 4,
    MENU = 5,
    HSCROLL = 6,
    VSCROLL = 7,
    MINBUTTON = 8,
    MAXBUTTON = 9,
    LEFT = 10,
    RIGHT = 11,
    TOP = 12,
    TOPLEFT = 13,
    TOPRIGHT = 14,
    BOTTOM = 15,
    BOTTOMLEFT = 16,
    BOTTOMRIGHT = 17,
    BORDER = 18,
    OBJECT = 19,
    CLOSE = 20,
    HELP = 21,
  }


  // WM_SIZE wParam values
  public enum SizeType : int
  {
    RESTORED = 0,
    MINIMIZED = 1,
    MAXIMIZED = 2,
    MAXSHOW = 3,
    MAXHIDE = 4,
  }
}
