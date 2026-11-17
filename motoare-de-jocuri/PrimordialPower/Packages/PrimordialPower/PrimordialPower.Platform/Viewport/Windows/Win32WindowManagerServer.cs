using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using PrimordialPower.Runtime;

namespace PrimordialPower.Platform.Windows;

[SupportedOSPlatform("windows")]
public sealed class Win32WindowManagerServer : ViewportManagerServerBase
{
  public override AppServerLifetime Lifetime => AppServerLifetime.MonoThread;

  // Window Class Definitions

  private Win32.WNDCLASSW windowClass;
  private IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
  {
    Viewport? viewport;

    switch (msg)
    {
      case Win32.WM_CLOSE:
        if (!this.openViewports.TryGetValue(hWnd, out viewport)) return 0;
        viewport.Close();
        return 0;


      case Win32.WM_DESTROY:
        return 0;


      case Win32.WM_SIZE:
        if (this.openViewports.TryGetValue(hWnd, out viewport))
        {
          int width = Win32.LoWord(lParam);
          int height = Win32.HiWord(lParam);
          viewport.AsInternal.ChangeInternalSize((uint)width, (uint)height);
          switch (wParam)
          {
            case Win32.SIZE_RESTORED:
            case Win32.SIZE_MAXIMIZED:
              viewport.AsInternal.ChangeInternalVisibility(true);
              break;

            case Win32.SIZE_MINIMIZED:
              viewport.AsInternal.ChangeInternalVisibility(false);
              break;
          }
        }
        return 0;


      case Win32.WM_MOVE:
        if (this.openViewports.TryGetValue(hWnd, out viewport))
        {
          int x = Win32.LoWord(lParam);
          int y = Win32.HiWord(lParam);
          viewport.AsInternal.ChangeInternalPosition(x, y);
        }
        return 0;


      case Win32.WM_SHOWWINDOW:
        if (this.openViewports.TryGetValue(hWnd, out viewport))
        {
          viewport.AsInternal.ChangeInternalFocus(true);
          viewport.AsInternal.ChangeInternalVisibility(true);
        }
        return 0;


      case Win32.WM_SETFOCUS:
        if (this.openViewports.TryGetValue(hWnd, out viewport))
        {
          viewport.AsInternal.ChangeInternalFocus(true);
        }
        return 0;


      case Win32.WM_KILLFOCUS:
        if (this.openViewports.TryGetValue(hWnd, out viewport))
        {
          viewport.AsInternal.ChangeInternalFocus(false);
        }
        return 0;


      case Win32.WM_PAINT:
        Win32.ValidateRect(hWnd, 0);
        return 0;


      case Win32.WM_SETCURSOR:
        switch (Win32.LoWord(lParam))
        {
          case Win32.HTCLIENT:
            Win32.SetCursor(Win32.LoadCursorW(0, Win32.IDC_ARROW));
            return 1;
          default:
            break;
        }
        break;

    }
    return Win32.DefWindowProcW(hWnd, msg, wParam, lParam);
  }



  public Win32WindowManagerServer()
  : base()
  {
    this.windowClass = new()
    {
      Style = Win32.CS_HREDRAW | Win32.CS_VREDRAW,
      LPFNWndProc = WindowProc,
      LPSZClassName = GenerateInstanceID()
    };

    ushort atom = Win32.RegisterClassW(ref this.windowClass);
    if (atom == 0)
    {
      throw new Win32Exception($"Failed to register Win32 window class: {Marshal.GetLastWin32Error()}");
    }
  }

  // Viewport Manager Main Runtime

  private readonly Dictionary<IntPtr, Viewport> openViewports = new();
  private uint threadId;



  public override void Init()
  {
    this.threadId = Win32.GetCurrentThreadId();
    Win32.PeekMessageW(out _, 0, 0, 0, 0);
  }



  protected override void PollEvents()
  {
    int result = Win32.GetMessageW(out var msg, IntPtr.Zero, 0, 0);
    Win32.TranslateMessage(ref msg);
    if (result <= 0) return;

    switch (msg.Message)
    {
      case Win32.WM_USER_WINDOW_INIT:
        base.OpenWindowsFromQueue();
        break;

      case Win32.WM_USER_WINDOW_DESTROY:
        Win32.DestroyWindow(msg.HWnd);
        break;
    }

    Win32.DispatchMessageW(ref msg);

    var e = this.OnPollEvents;
    e?.Invoke(new());
  }


  protected override void OpenImpl(Viewport viewport)
  {
    Win32.RECT decOffset = new()
    {
      Left = 0,
      Top = 0,
      Right = (int)viewport.Size.Width,
      Bottom = (int)viewport.Size.Height,
    };
    Win32.AdjustWindowRectEx(out decOffset, Win32.WS_OVERLAPPEDWINDOW, false, 0);

    IntPtr hInstance = windowClass.HInstance;
    IntPtr hWnd = Win32.CreateWindowExW(
      dwExStyle: 0,
      lpClassName: windowClass.LPSZClassName,
      lpWindowName: viewport.Title,
      dwStyle: Win32.WS_OVERLAPPEDWINDOW,
      x: viewport.IsDefaultPosition.X ? Win32.CW_USEDEFAULT : viewport.Position.X,// + decOffset.Left,
      y: viewport.IsDefaultPosition.Y ? Win32.CW_USEDEFAULT : viewport.Position.Y,// + decOffset.Top,
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

    int pref = Win32.DWMWCP_DONOTROUND;
    Win32.DwmSetWindowAttribute(hWnd, Win32.DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));

    Win32.ShowWindow(hWnd, Win32.SW_SHOW);
    Win32.UpdateWindow(hWnd);

    ViewportHandle handle = new(
      new Win32WindowHandle()
      {
        HWnd = hWnd,
        HInstance = hInstance
      }
    );

    this.openViewports.Add(hWnd, viewport);

    viewport.AsInternal.Validate(handle);
  }

  protected override void WakeUpEventLoop()
  {
    Win32.PostThreadMessageW(this.threadId, Win32.WM_USER_WINDOW_INIT, 0, 0);
  }

  // Viewport Manager Services

  public override event Action? OnAllViewportsClosed;
  public override event Action<ViewportEvent>? OnPollEvents;



  public override int OpenViewportCount => this.openViewports.Count;



  public override void CloseAll()
  {
    lock (base.lockOpeningViewports)
    {
      foreach (var kvpViewport in this.openViewports)
      {
        Close(kvpViewport.Value);
      }
    }
  }

  public override void Destroy(Viewport viewport)
  {
    viewport.AsInternal.CheckBoundTo(this);
    viewport.AsInternal.CheckAlive();
    lock (base.lockOpeningViewports)
    {
      if (this.openViewports.Count == 1)
      {
        var e = this.OnAllViewportsClosed;
        e?.Invoke();
      }

      ViewportHandle handle = viewport.Handle;
      viewport.AsInternal.Invalidate();
      this.openViewports.Remove(handle.Win32.HWnd);
      Win32.PostMessageW(handle.Win32.HWnd, Win32.WM_USER_WINDOW_DESTROY, 0, 0);
    }
  }

  public override void SetFocus(Viewport viewport, bool isFocused)
  {
    viewport.AsInternal.CheckBoundTo(this);
    viewport.AsInternal.CheckAlive();
    if (isFocused) Win32.PostMessageW(viewport.Handle.Win32.HWnd, Win32.WM_SETFOCUS, 0, 0);
    else Win32.PostMessageW(viewport.Handle.Win32.HWnd, Win32.WM_KILLFOCUS, 0, 0);
  }

  public override void SetPosition(Viewport viewport, int x, int y)
  {
    viewport.AsInternal.CheckBoundTo(this);
    viewport.AsInternal.CheckAlive();
    Win32.SetWindowPos(
      hWnd: viewport.Handle.Win32.HWnd,
      hWndInsertAfter: 0,
      x: x,
      y: y,
      cx: 0,
      cy: 0,
      uFlags: Win32.SWP_NOSIZE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE
    );
  }

  public override void SetSize(Viewport viewport, uint width, uint height)
  {
    viewport.AsInternal.CheckBoundTo(this);
    viewport.AsInternal.CheckAlive();
    Win32.SetWindowPos(
      hWnd: viewport.Handle.Win32.HWnd,
      hWndInsertAfter: 0,
      x: 0,
      y: 0,
      cx: (int)width,
      cy: (int)height,
      uFlags: Win32.SWP_NOMOVE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE
    );
  }

  public override void SetSizePosition(Viewport viewport, uint width, uint height, int x, int y)
  {
    viewport.AsInternal.CheckBoundTo(this);
    viewport.AsInternal.CheckAlive();
    Win32.SetWindowPos(
      hWnd: viewport.Handle.Win32.HWnd,
      hWndInsertAfter: 0,
      x: x,
      y: y,
      cx: (int)width,
      cy: (int)height,
      uFlags: Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE
    );
  }

  public override void SetTitle(Viewport viewport, string text)
  {
    viewport.AsInternal.CheckBoundTo(this);
    viewport.AsInternal.CheckAlive();
    Win32.SetWindowTextW(viewport.Handle.Win32.HWnd, text);
  }
}