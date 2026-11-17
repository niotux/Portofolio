using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using UnivKit.Application;
using UnivKit.OS.Platform.Windows;

namespace UnivKit.OS.Windowing.Win32;

[SupportedOSPlatform("windows")]
internal sealed class WindowingNativeService : WindowingNativeServiceBase
{
  private User32.WNDCLASSW windowClass;

  private IntPtr WindowProc(IntPtr hWnd, User32.WM msg, IntPtr wParam, IntPtr lParam)
  {
    Window? window;
    ulong handle = (ulong)hWnd;

    switch (msg)
    {
      case User32.WM.CLOSE:
        if (!this.openWindows.TryGetValue(handle, out window)) return 0;
        base.eventArgs.EventType = WindowEventType.ShouldClose;
        SendEvent(window, true);
        return 0;


      case User32.WM.DESTROY:
        return 0;


      case User32.WM.SIZE:
        if (this.openWindows.TryGetValue(handle, out window))
        {
          switch ((User32.SizeType)(int)wParam)
          {
            case User32.SizeType.RESTORED:
              window.SetStateInternal(WindowState.Normal);
              window.SetVisibilityInternal(true);

              base.eventArgs.VisibilityChangedArgs = new()
              {
                State = WindowState.Normal,
              };
              base.eventArgs.EventType = WindowEventType.Visibile;
              SendEvent(window, true);
              break;

            case User32.SizeType.MAXIMIZED:
              window.SetStateInternal(WindowState.Maximized);
              window.SetVisibilityInternal(true);

              base.eventArgs.VisibilityChangedArgs = new()
              {
                State = WindowState.Maximized,
              };
              base.eventArgs.EventType = WindowEventType.Visibile;
              SendEvent(window, true);
              break;

            case User32.SizeType.MINIMIZED:
              window.SetStateInternal(WindowState.Minimized);
              window.SetVisibilityInternal(false);

              base.eventArgs.VisibilityChangedArgs = new()
              {
                State = WindowState.Minimized,
              };
              base.eventArgs.EventType = WindowEventType.Hidden;
              SendEvent(window, true);
              break;
          }
          uint width = (uint)Macros.LoWord(lParam);
          uint height = (uint)Macros.HiWord(lParam);
          window.SetSizeInternal(width, height);

          base.eventArgs.SizeChangedArgs = new()
          {
            Width = width,
            Height = height,
          };
          SendEvent(window, true);
        }
        return 0;


      case User32.WM.MOVE:
        if (this.openWindows.TryGetValue(handle, out window))
        {
          int x = Macros.SignedLoWord(lParam);
          int y = Macros.SignedHiWord(lParam);
          window.SetPositionInternal(x, y);

          base.eventArgs.PositionChangedArgs = new()
          {
            X = x,
            Y = y,
          };
          SendEvent(window, true);
        }
        return 0;


      case User32.WM.SETFOCUS:
        if (this.openWindows.TryGetValue(handle, out window))
        {
          window.SetFocusInternal(true);

          base.eventArgs.EventType = WindowEventType.FocusIn;
          SendEvent(window, true);
        }
        return 0;


      case User32.WM.KILLFOCUS:
        if (this.openWindows.TryGetValue(handle, out window))
        {
          window.SetFocusInternal(false);

          base.eventArgs.EventType = WindowEventType.FocusOut;
          SendEvent(window, true);
        }
        return 0;


      case User32.WM.PAINT:
        User32.ValidateRect(hWnd, 0);
        return 0;


      case User32.WM.SETTEXT:
        if (this.openWindows.TryGetValue(handle, out window))
        {
          string title = Marshal.PtrToStringAuto(lParam) ?? string.Empty;
          window.SetTitleInternal(title);

          base.eventArgs.TitleChangedArgs = new()
          {
            Title = title,
          };
          SendEvent(window, true);
        }
        User32.DefWindowProcW(hWnd, msg, wParam, lParam);
        return 0;


      case User32.WM.SETCURSOR:
        switch ((User32.HT)Macros.LoWord(lParam))
        {
          case User32.HT.CLIENT:
            User32.SetCursor(User32.LoadCursorW(0, User32.IDC.ARROW));
            return 1;
          default:
            break;
        }
        break;

    }
    return User32.DefWindowProcW(hWnd, msg, wParam, lParam);
  }



  public WindowingNativeService()
    : base()
  {
    this.windowClass = new()
    {
      Style = User32.CS.HREDRAW | User32.CS.VREDRAW,
      LPFNWndProc = WindowProc,
      LPSZClassName = this.GetInstanceId(),
    };

    ushort atom = User32.RegisterClassW(ref this.windowClass);
    if (atom == 0)
    {
      throw new Win32Exception($"Failed to register Win32 window class: {Marshal.GetLastWin32Error()}");
    }
  }



  private uint threadId;
  protected override void InitCore()
  {
    this.threadId = Kernel32.GetCurrentThreadId();
    User32.PeekMessageW(out _, 0, 0, 0, User32.PM.NOREMOVE);
  }



  protected override void PollEventsCore()
  {
    int result = User32.GetMessageW(out var msg, IntPtr.Zero, 0, 0);
    if (result <= 0) return;

    User32.TranslateMessage(ref msg);

    switch (msg.Message)
    {
      case User32.WM.USER_NULL:
        base.OpenWindowsFromQueue();
        break;

      case User32.WM.USER_WINDOW_DESTROY:
        User32.DestroyWindow(msg.HWnd);
        break;
    }

    User32.DispatchMessageW(ref msg);
  }


  protected override void OpenCore(Window window)
  {
    User32.RECT decOffset = new()
    {
      Left = 0,
      Top = 0,
      Right = (int)window.Size.Width,
      Bottom = (int)window.Size.Height,
    };
    User32.AdjustWindowRectEx(out decOffset, User32.WS.OVERLAPPEDWINDOW, false, 0);

    IntPtr hInstance = this.windowClass.HInstance;
    IntPtr hWnd = User32.CreateWindowExW(
      dwExStyle: 0,
      lpClassName: this.windowClass.LPSZClassName,
      lpWindowName: window.Title,
      dwStyle: User32.WS.OVERLAPPEDWINDOW,
      x: window.Position.X,// + decOffset.Left,
      y: window.Position.Y,// + decOffset.Top,
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

    Windowing.SurfaceHandle handle = new(
      new SurfaceNativeHandle()
      {
        HWnd = hWnd,
        HInstance = hInstance
      }
    );

    this.openWindows[(ulong)handle.Win32.HWnd] = window;
    window.ValidateInternal(handle);

    int pref = (int)DWMApi.DWMWCP.DONOTROUND;
    DWMApi.DwmSetWindowAttribute(hWnd, DWMApi.DWMWA.WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));

    User32.ShowWindow(hWnd, User32.SW.SHOW);
    User32.UpdateWindow(hWnd);
  }

  protected override void WakeUpEventLoopCore()
  {
    User32.PostThreadMessageW(this.threadId, User32.WM.USER_NULL, 0, 0);
  }

  protected override void CloseCore(Window window)
  {
    SurfaceHandle handle = window.Handle ?? default;
    User32.PostMessageW(handle.Win32.HWnd, User32.WM.USER_WINDOW_DESTROY, 0, 0);
  }

  protected override ulong GetKeyFromHandleCore(in SurfaceHandle handle)
  {
    return (ulong)handle.Win32.HWnd;
  }


  internal override void SetFocusCore(Window window)
  {
    if (window.Handle is null)
    {
      return;
    }
    User32.SetFocus(window.Handle.Value.Win32.HWnd);
  }

  internal override void SetStateCore(Window window, WindowState state)
  {
    if (window.Handle is null)
    {
      return;
    }
    switch (state)
    {
      case WindowState.Normal:
        User32.ShowWindow(window.Handle.Value.Win32.HWnd, User32.SW.RESTORE);
        break;

      case WindowState.Minimized:
        User32.ShowWindow(window.Handle.Value.Win32.HWnd, User32.SW.MINIMIZE);
        break;

      case WindowState.FullScreen:
      case WindowState.Maximized:
        User32.ShowWindow(window.Handle.Value.Win32.HWnd, User32.SW.SHOWMAXIMIZED);
        break;
    }
  }

  internal override void SetPositionCore(Window window, int x, int y)
  {
    if (window.Handle is null) return;
    User32.SetWindowPos(
      hWnd: window.Handle.Value.Win32.HWnd,
      hWndInsertAfter: 0,
      x: x,
      y: y,
      cx: 0,
      cy: 0,
      uFlags: User32.SWP.NOSIZE | User32.SWP.NOZORDER | User32.SWP.NOACTIVATE
    );
  }

  internal override void SetSizeCore(Window window, uint width, uint height)
  {
    if (window.Handle is null) return;
    User32.SetWindowPos(
      hWnd: window.Handle.Value.Win32.HWnd,
      hWndInsertAfter: 0,
      x: 0,
      y: 0,
      cx: (int)width,
      cy: (int)height,
      uFlags: User32.SWP.NOMOVE | User32.SWP.NOZORDER | User32.SWP.NOACTIVATE
    );
  }

  internal override void SetTitleCore(Window window, string text)
  {
    if (window.Handle is null) return;
    User32.SetWindowTextW(window.Handle.Value.Win32.HWnd, text);
  }
}
