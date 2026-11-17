using System.Runtime.Versioning;
using UnivKit.OS.Platform.Linux;

namespace UnivKit.OS.Windowing.X11;

[SupportedOSPlatform("linux")]
internal sealed class WindowingNativeService : WindowingNativeServiceBase
{
  private IntPtr display;
  private int screen;
  private ulong rootWindow;
  private X11Atoms atoms;


  public WindowingNativeService()
    : base()
  {
    this.display = X11Api.XOpenDisplay(null);
    if (this.display == IntPtr.Zero)
    {
      throw new Exception("Could not establish link to X11 server.");
    }

    this.screen = X11Api.XDefaultScreen(this.display);
    this.rootWindow = X11Api.XRootWindow(this.display, this.screen);
    this.atoms = new(this.display);

    X11Api.XSelectInput(this.display, this.rootWindow, X11Api.XEventMask.SubstructureNotifyMask);
    X11Api.XFlush(this.display);
  }



  protected override void InitCore()
  {
    X11Api.XFlush(this.display);
  }


  private void HandleEvent(in X11Api.XEvent xEvent)
  {
    switch (xEvent.Type)
    {
      case X11Api.XEventType.ClientMessage:
        HandleClientMessage(in xEvent);
        break;

      case X11Api.XEventType.Configure:
        HandleConfigure(in xEvent);
        break;

      case X11Api.XEventType.Map:
        HandleMap(in xEvent);
        break;

      case X11Api.XEventType.Unmap:
        HandleUnmap(in xEvent);
        break;

      case X11Api.XEventType.Expose:
        HandleExpose(in xEvent);
        break;

      case X11Api.XEventType.FocusIn:
        HandleFocusIn(in xEvent);
        break;

      case X11Api.XEventType.FocusOut:
        HandleFocusOut(in xEvent);
        break;

      case X11Api.XEventType.DestroyWindow:
        break;
    }
  }

  protected override void PollEventsCore()
  {
    X11Api.XNextEvent(this.display, out var xEvent);
    HandleEvent(in xEvent);

    while (X11Api.XPending(this.display) > 0)
    {
      X11Api.XNextEvent(this.display, out xEvent);
      HandleEvent(in xEvent);
    }
  }



  private void HandleClientMessage(in X11Api.XEvent xEvent)
  {
    if (xEvent.XClient.MessageType == this.atoms.WMNullAtom)
    {
      base.OpenWindowsFromQueue();
    }
    else if (xEvent.XClient.MessageType == this.atoms.WMProtocolAtom)
    {
      if ((UIntPtr)xEvent.XClient.Data.L[0] == this.atoms.WMDeleteAtom)
      {
        if (!this.openWindows.TryGetValue(xEvent.XClient.Window, out var window))
        {
          return;
        }

        base.eventArgs.EventType = WindowEventType.ShouldClose;
        SendEvent(window, true);
      }
    }
    else if (xEvent.XClient.MessageType == this.atoms.WMDestroyWindowAtom)
    {
      ulong windowId = xEvent.XClient.Window;
      X11Api.XDestroyWindow(this.display, windowId);
    }
  }



  private void HandleConfigure(in X11Api.XEvent xEvent)
  {
    if (!this.openWindows.TryGetValue(xEvent.XConfigure.Window, out var window))
    {
      return;
    }

    int x = xEvent.XConfigure.X;
    int y = xEvent.XConfigure.Y;
    uint width = (uint)xEvent.XConfigure.Width;
    uint height = (uint)xEvent.XConfigure.Height;

    var oldPos = window.Position;
    var oldSize = window.Size;

    if (oldPos.X != x || oldPos.Y != y)
    {
      window.SetPositionInternal(x, y);

      base.eventArgs.PositionChangedArgs = new()
      {
        X = x,
        Y = y,
      };
      SendEvent(window, true);
    }

    if (oldSize.Width != width || oldSize.Height != height)
    {
      window.SetSizeInternal(width, height);

      base.eventArgs.SizeChangedArgs = new()
      {
        Width = width,
        Height = height,
      };
      SendEvent(window, true);
    }
  }



  private void HandleMap(in X11Api.XEvent xEvent)
  {
    if (!this.openWindows.TryGetValue(xEvent.XMap.Window, out var window))
    {
      return;
    }
    if (window.IsVisible && window.WindowState == WindowState.Normal)
    {
      return;
    }

    window.SetVisibilityInternal(true);
    window.SetStateInternal(WindowState.Normal);

    base.eventArgs.VisibilityChangedArgs = new()
    {
      State = WindowState.Normal,
    };
    base.eventArgs.EventType = WindowEventType.Visibile;
    SendEvent(window, true);
  }



  private void HandleUnmap(in X11Api.XEvent xEvent)
  {
    if (!this.openWindows.TryGetValue(xEvent.XUnmap.Window, out var window))
    {
      return;
    }
    if (!window.IsVisible && window.WindowState == WindowState.Minimized)
    {
      return;
    }

    window.SetVisibilityInternal(false);
    window.SetStateInternal(WindowState.Minimized);

    base.eventArgs.VisibilityChangedArgs = new()
    {
      State = WindowState.Minimized,
    };
    base.eventArgs.EventType = WindowEventType.Hidden;
    SendEvent(window, true);
  }



  private void HandleExpose(in X11Api.XEvent xEvent) { }



  private void HandleFocusIn(in X11Api.XEvent xEvent)
  {
    if (!this.openWindows.TryGetValue(xEvent.XFocusChange.Window, out var window)) return;
    if (window.IsFocused) return;

    window.SetFocusInternal(true);

    base.eventArgs.EventType = WindowEventType.FocusIn;
    SendEvent(window, true);
  }



  private void HandleFocusOut(in X11Api.XEvent xEvent)
  {
    if (!this.openWindows.TryGetValue(xEvent.XFocusChange.Window, out var window)) return;
    if (!window.IsFocused) return;

    window.SetFocusInternal(false);

    base.eventArgs.EventType = WindowEventType.FocusOut;
    SendEvent(window, true);
  }



  protected override void OpenCore(Window window)
  {
    ulong blackPixel = X11Api.XBlackPixel(this.display, this.screen);

    uint safeWidth = window.Size.Width;
    uint safeHeight = window.Size.Height;
    if (safeWidth == 0) safeWidth = 1;
    if (safeHeight == 0) safeHeight = 1;

    ulong xWindow = X11Api.XCreateSimpleWindow(
      display: this.display,
      parent: this.rootWindow,
      x: window.Position.X,
      y: window.Position.Y,
      width: safeWidth,
      height: safeHeight,
      borderWidth: 1,
      border: blackPixel,
      background: blackPixel
    );

    if (xWindow == 0)
    {
      throw new Exception("Failed to create X11 window.");
    }

    X11Api.XStoreName(this.display, xWindow, window.Title);

    X11Api.XEventMask mask = 0
      | X11Api.XEventMask.ExposureMask
      | X11Api.XEventMask.FocusChangeMask
      | X11Api.XEventMask.StructureNotifyMask;

    X11Api.XSelectInput(this.display, xWindow, mask);
    X11Api.XSetWMProtocols(this.display, xWindow, ref this.atoms.WMDeleteAtom, 1);
    X11Api.XMapWindow(this.display, xWindow);
    X11Api.XFlush(this.display);

    Windowing.SurfaceHandle handle = new(
      new SurfaceNativeHandle()
      {
        Display = this.display,
        Window = xWindow
      }
    );

    this.openWindows[xWindow] = window;
    window.ValidateInternal(handle);
  }



  protected override void CloseCore(Window window)
  {
    SurfaceHandle handle = window.Handle ?? default;
    // Post a destroy atom to the window — equivalent to Win32's
    // PostMessageW(WM_USER_WINDOW_DESTROY). This ensures the destroy
    // happens on the event loop thread.
    PostAtomToWindow(handle.X11.Window, this.atoms.WMDestroyWindowAtom);
  }


  protected override void WakeUpEventLoopCore()
  {
    PostAtomToRoot(this.atoms.WMNullAtom);
  }

  protected override ulong GetKeyFromHandleCore(in SurfaceHandle handle)
  {
    return handle.X11.Window;
  }



  internal override void SetFocusCore(Window window)
  {
    if (window.Handle is null) return;
    X11Api.XSetInputFocus(this.display, window.Handle.Value.X11.Window, X11Api.RevertTo.None, 0);

    // X11 focus is async — update state + fire event immediately
    // to match Win32's synchronous SetFocus → WM_SETFOCUS guarantee.
    // HandleFocusIn will dedup when the async event arrives.
    window.SetFocusInternal(true);

    base.eventArgs.EventType = WindowEventType.FocusIn;
    SendEvent(window, false);
  }


  internal override void SetStateCore(Window window, WindowState state)
  {
    if (window.Handle is null) return;

    ulong xWindow = window.Handle.Value.X11.Window;
    bool visible = true;
    switch (state)
    {
      case WindowState.Normal:
        X11Api.XMapWindow(this.display, xWindow);
        X11Api.XFlush(this.display);
        break;

      case WindowState.Minimized:
        PostAtomToWindow(xWindow, this.atoms.WMChangeStateAtom);
        visible = false;
        break;

      case WindowState.FullScreen:
      case WindowState.Maximized:
        X11Api.XMapWindow(this.display, xWindow);
        X11Api.XFlush(this.display);
        break;
    }

    // Update state + fire event immediately to match Win32's
    // synchronous ShowWindow → WM_SIZE guarantee.
    // HandleMap/HandleUnmap will dedup when the async events arrive.
    window.SetStateInternal(state);
    window.SetVisibilityInternal(visible);

    base.eventArgs.VisibilityChangedArgs = new()
    {
      State = state,
    };
    base.eventArgs.EventType = visible ? WindowEventType.Visibile : WindowEventType.Hidden;
    SendEvent(window, false);
  }


  internal override void SetPositionCore(Window window, int x, int y)
  {
    if (window.Handle is null) return;
    X11Api.XMoveWindow(this.display, window.Handle.Value.X11.Window, x, y);

    // Update state + fire event immediately to match Win32's
    // synchronous SetWindowPos → WM_MOVE guarantee.
    // HandleConfigure will dedup when the async ConfigureNotify arrives.
    window.SetPositionInternal(x, y);

    base.eventArgs.PositionChangedArgs = new()
    {
      X = x,
      Y = y,
    };
    SendEvent(window, false);
  }


  internal override void SetSizeCore(Window window, uint width, uint height)
  {
    if (window.Handle is null) return;
    X11Api.XResizeWindow(this.display, window.Handle.Value.X11.Window, width, height);

    // Update state + fire event immediately to match Win32's
    // synchronous SetWindowPos → WM_SIZE guarantee.
    // HandleConfigure will dedup when the async ConfigureNotify arrives.
    window.SetSizeInternal(width, height);

    base.eventArgs.SizeChangedArgs = new()
    {
      Width = width,
      Height = height,
    };
    SendEvent(window, false);
  }


  internal override void SetTitleCore(Window window, string text)
  {
    if (window.Handle is null) return;
    X11Api.XStoreName(this.display, window.Handle.Value.X11.Window, text);

    // X11 has no WM_SETTEXT equivalent — update immediately.
    window.SetTitleInternal(text);

    base.eventArgs.TitleChangedArgs = new()
    {
      Title = text,
    };
    SendEvent(window, false);
  }



  private void PostAtomToRoot(UIntPtr atom)
  {
    X11Api.XEvent evt = new() { Type = X11Api.XEventType.ClientMessage };
    evt.XClient.Display = this.display;
    evt.XClient.Window = this.rootWindow;
    evt.XClient.MessageType = atom;
    evt.XClient.Format = 32;

    X11Api.XSendEvent(this.display, this.rootWindow, X11Api.Bool32.False, X11Api.XEventMask.SubstructureNotifyMask, ref evt);
    X11Api.XFlush(this.display);
  }



  private void PostAtomToWindow(ulong window, UIntPtr atom)
  {
    X11Api.XEvent evt = new() { Type = X11Api.XEventType.ClientMessage };
    evt.XClient.Display = this.display;
    evt.XClient.Window = window;
    evt.XClient.MessageType = atom;
    evt.XClient.Format = 32;

    X11Api.XSendEvent(this.display, window, X11Api.Bool32.False, X11Api.XEventMask.NoEventMask, ref evt);
    X11Api.XFlush(this.display);
  }



  private struct X11Atoms
  {
    public X11Atoms(IntPtr display)
    {
      this.WMNullAtom = X11Api.XInternAtom(display, X11Api.WM_USER_NULL, X11Api.Bool32.False);
      this.WMDeleteAtom = X11Api.XInternAtom(display, X11Api.WM_DELETE_WINDOW, X11Api.Bool32.False);
      this.WMProtocolAtom = X11Api.XInternAtom(display, X11Api.WM_PROTOCOLS, X11Api.Bool32.False);
      this.WMDestroyWindowAtom = X11Api.XInternAtom(display, X11Api.WM_USER_DESTROY_WINDOW, X11Api.Bool32.False);
      this.WMFocusWindowAtom = X11Api.XInternAtom(display, X11Api.WM_USER_FOCUS_WINDOW, X11Api.Bool32.False);
      this.WMChangeStateAtom = X11Api.XInternAtom(display, X11Api.WM_CHANGE_STATE, X11Api.Bool32.False);
    }

    public UIntPtr WMNullAtom;
    public UIntPtr WMDeleteAtom;
    public UIntPtr WMProtocolAtom;
    public UIntPtr WMDestroyWindowAtom;
    public UIntPtr WMFocusWindowAtom;
    public UIntPtr WMChangeStateAtom;
  }
}
