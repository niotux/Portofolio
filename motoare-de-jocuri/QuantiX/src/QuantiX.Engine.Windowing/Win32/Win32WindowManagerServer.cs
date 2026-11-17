using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using QuantiX.Engine.Platform.Windows;

namespace QuantiX.Engine.Windowing.Win32;

[SupportedOSPlatform("windows")]
public sealed class Win32WindowManagerServer : WindowManagerServerBase
{
  public override AppServerLifetime Lifetime => AppServerLifetime.MonoThread;

  // Window Class Definitions

  private Win32Api.WNDCLASSW windowClass;
  private IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
  {
    Window? surface;

    switch (msg)
    {
      case Win32Api.WM_CLOSE:
        if (!this.openWindows.TryGetValue(hWnd, out surface)) return 0;
        surface.Close();
        return 0;


      case Win32Api.WM_DESTROY:
        return 0;


      case Win32Api.WM_SIZE:
        if (this.openWindows.TryGetValue(hWnd, out surface))
        {
          int width = Win32Api.LoWord(lParam);
          int height = Win32Api.HiWord(lParam);
          surface.AsInternal.ChangeInternalSize((uint)width, (uint)height);
          switch (wParam)
          {
            case Win32Api.SIZE_RESTORED:
            case Win32Api.SIZE_MAXIMIZED:
              surface.AsInternal.ChangeInternalVisibility(true);
              break;

            case Win32Api.SIZE_MINIMIZED:
              surface.AsInternal.ChangeInternalVisibility(false);
              break;
          }
        }
        return 0;


      case Win32Api.WM_MOVE:
        if (this.openWindows.TryGetValue(hWnd, out surface))
        {
          int x = Win32Api.LoWord(lParam);
          int y = Win32Api.HiWord(lParam);
          surface.AsInternal.ChangeInternalPosition(x, y);
        }
        return 0;


      case Win32Api.WM_SHOWWINDOW:
        if (this.openWindows.TryGetValue(hWnd, out surface))
        {
          surface.AsInternal.ChangeInternalFocus(true);
          surface.AsInternal.ChangeInternalVisibility(true);
        }
        return 0;


      case Win32Api.WM_SETFOCUS:
        if (this.openWindows.TryGetValue(hWnd, out surface))
        {
          surface.AsInternal.ChangeInternalFocus(true);
        }
        return 0;


      case Win32Api.WM_KILLFOCUS:
        if (this.openWindows.TryGetValue(hWnd, out surface))
        {
          surface.AsInternal.ChangeInternalFocus(false);
        }
        return 0;


      case Win32Api.WM_PAINT:
        Win32Api.ValidateRect(hWnd, 0);
        return 0;


      case Win32Api.WM_SETCURSOR:
        switch (Win32Api.LoWord(lParam))
        {
          case Win32Api.HTCLIENT:
            Win32Api.SetCursor(Win32Api.LoadCursorW(0, Win32Api.IDC_ARROW));
            return 1;
          default:
            break;
        }
        break;

    }
    return Win32Api.DefWindowProcW(hWnd, msg, wParam, lParam);
  }



  public Win32WindowManagerServer()
  : base()
  {
    this.windowClass = new()
    {
      Style = Win32Api.CS_HREDRAW | Win32Api.CS_VREDRAW,
      LPFNWndProc = WindowProc,
      LPSZClassName = this.InstanceId,
    };

    ushort atom = Win32Api.RegisterClassW(ref this.windowClass);
    if (atom == 0)
    {
      throw new Win32Exception($"Failed to register Win32 window class: {Marshal.GetLastWin32Error()}");
    }
  }

  // Window Manager Main Runtime

  private readonly Dictionary<IntPtr, Window> openWindows = new();
  private uint threadId;



  protected override void OnInit()
  {
    this.threadId = Win32Api.GetCurrentThreadId();
    Win32Api.PeekMessageW(out _, 0, 0, 0, 0);
  }



  protected override void PollEvents()
  {
    int result = Win32Api.GetMessageW(out var msg, IntPtr.Zero, 0, 0);
    Win32Api.TranslateMessage(ref msg);
    if (result <= 0) return;

    switch (msg.Message)
    {
      case Win32Api.WM_USER_WINDOW_INIT:
        base.OpenWindowsFromQueue();
        break;

      case Win32Api.WM_USER_WINDOW_DESTROY:
        Win32Api.DestroyWindow(msg.HWnd);
        break;
    }

    Win32Api.DispatchMessageW(ref msg);

    var e = this.OnPollEvents;
    e?.Invoke(new());
  }


  protected override void OpenImpl(Window surface)
  {
    Win32Api.RECT decOffset = new()
    {
      Left = 0,
      Top = 0,
      Right = (int)surface.Size.Width,
      Bottom = (int)surface.Size.Height,
    };
    Win32Api.AdjustWindowRectEx(out decOffset, Win32Api.WS_OVERLAPPEDWINDOW, false, 0);

    IntPtr hInstance = windowClass.HInstance;
    IntPtr hWnd = Win32Api.CreateWindowExW(
      dwExStyle: 0,
      lpClassName: windowClass.LPSZClassName,
      lpWindowName: surface.Title,
      dwStyle: Win32Api.WS_OVERLAPPEDWINDOW,
      x: surface.IsDefaultPosition.X ? Win32Api.CW_USEDEFAULT : surface.Position.X,// + decOffset.Left,
      y: surface.IsDefaultPosition.Y ? Win32Api.CW_USEDEFAULT : surface.Position.Y,// + decOffset.Top,
      nWidth: decOffset.Right - decOffset.Left,
      nHeight: decOffset.Bottom - decOffset.Top,
      hWndParent: 0,
      hMenu: 0,
      hInstance: hInstance,
      lpParam: IntPtr.Zero
    );
    if (hWnd == 0)
    {
      throw new Win32Exception($"Failed to create Win32 window: {Marshal.GetLastWin32Error()}");
    }

    int pref = Win32Api.DWMWCP_DONOTROUND;
    Win32Api.DwmSetWindowAttribute(hWnd, Win32Api.DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));

    Win32Api.ShowWindow(hWnd, Win32Api.SW_SHOW);
    Win32Api.UpdateWindow(hWnd);

    WindowHandle handle = new(
      new Win32WindowHandle()
      {
        HWnd = hWnd,
        HInstance = hInstance
      }
    );

    this.openWindows.Add(hWnd, surface);

    surface.AsInternal.Validate(handle);
  }

  protected override void WakeUpEventLoop()
  {
    Win32Api.PostThreadMessageW(this.threadId, Win32Api.WM_USER_WINDOW_INIT, 0, 0);
  }

  // Window Manager Services
  public override event Action? OnAllClosed;
  public override event Action<WindowEvent>? OnPollEvents;



  public override int OpenCount => this.openWindows.Count;



  public override void CloseAll()
  {
    lock (base.lockOpening)
    {
      foreach (var kvpWindow in this.openWindows)
      {
        Close(kvpWindow.Value);
      }
    }
  }

  public override void Destroy(Window surface)
  {
    surface.AsInternal.CheckBoundTo(this);
    surface.AsInternal.CheckAlive();
    lock (base.lockOpening)
    {
      if (this.openWindows.Count == 1)
      {
        var e = this.OnAllClosed;
        e?.Invoke();
      }

      WindowHandle handle = surface.Handle;
      surface.AsInternal.Invalidate();
      this.openWindows.Remove(handle.Win32.HWnd);
      Win32Api.PostMessageW(handle.Win32.HWnd, Win32Api.WM_USER_WINDOW_DESTROY, 0, 0);
    }
  }

  public override void SetFocus(Window surface, bool isFocused)
  {
    surface.AsInternal.CheckBoundTo(this);
    surface.AsInternal.CheckAlive();
    if (isFocused) Win32Api.PostMessageW(surface.Handle.Win32.HWnd, Win32Api.WM_SETFOCUS, 0, 0);
    else Win32Api.PostMessageW(surface.Handle.Win32.HWnd, Win32Api.WM_KILLFOCUS, 0, 0);
  }

  public override void SetPosition(Window surface, int x, int y)
  {
    surface.AsInternal.CheckBoundTo(this);
    surface.AsInternal.CheckAlive();
    Win32Api.SetWindowPos(
      hWnd: surface.Handle.Win32.HWnd,
      hWndInsertAfter: 0,
      x: x,
      y: y,
      cx: 0,
      cy: 0,
      uFlags: Win32Api.SWP_NOSIZE | Win32Api.SWP_NOZORDER | Win32Api.SWP_NOACTIVATE
    );
  }

  public override void SetSize(Window surface, uint width, uint height)
  {
    surface.AsInternal.CheckBoundTo(this);
    surface.AsInternal.CheckAlive();
    Win32Api.SetWindowPos(
      hWnd: surface.Handle.Win32.HWnd,
      hWndInsertAfter: 0,
      x: 0,
      y: 0,
      cx: (int)width,
      cy: (int)height,
      uFlags: Win32Api.SWP_NOMOVE | Win32Api.SWP_NOZORDER | Win32Api.SWP_NOACTIVATE
    );
  }

  public override void SetSizePosition(Window surface, uint width, uint height, int x, int y)
  {
    surface.AsInternal.CheckBoundTo(this);
    surface.AsInternal.CheckAlive();
    Win32Api.SetWindowPos(
      hWnd: surface.Handle.Win32.HWnd,
      hWndInsertAfter: 0,
      x: x,
      y: y,
      cx: (int)width,
      cy: (int)height,
      uFlags: Win32Api.SWP_NOZORDER | Win32Api.SWP_NOACTIVATE
    );
  }

  public override void SetTitle(Window surface, string text)
  {
    surface.AsInternal.CheckBoundTo(this);
    surface.AsInternal.CheckAlive();
    Win32Api.SetWindowTextW(surface.Handle.Win32.HWnd, text);
  }
}