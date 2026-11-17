using System.Runtime.Versioning;

namespace PrimordialPower.Platform.Linux;

[SupportedOSPlatform("linux")]
public sealed class X11WindowManagerServer : ViewportManagerServerBase
{
  private readonly Dictionary<ulong, Viewport> openViewports = new();



  protected override void OpenImpl(Viewport viewport)
  {
    ulong blackPixel = X11.XBlackPixel(this.display, screen);
    ulong whitePixel = X11.XWhitePixel(this.display, screen);

    uint safeWidth = viewport.Size.Width;
    uint safeHeight = viewport.Size.Height;
    if (safeWidth <= 0)
    {
      safeWidth = 1;
    }
    if (safeHeight <= 0)
    {
      safeHeight = 1;
    }

    ulong window = X11.XCreateSimpleWindow(
      display: this.display,
      parent: this.rootWindow,
      x: viewport.Position.X,
      y: viewport.Position.Y,
      width: safeWidth,
      height: safeHeight,
      borderWidth: 1,
      border: blackPixel,
      background: blackPixel
    );

    if (window == 0)
    {
      throw new Exception("Failed to create X11 window.");
    }

    X11.XStoreName(this.display, window, viewport.Title);

    X11.XEventMask mask =
      X11.XEventMask.ExposureMask |
      X11.XEventMask.FocusChangeMask |
      X11.XEventMask.StructureNotifyMask;

    X11.XSelectInput(
      this.display,
      window,
      mask
    );
    X11.XSetWMProtocols(this.display, window, ref this.atoms.WMDeleteAtom, 1);
    X11.XMapWindow(this.display, window);

    X11.XFlush(this.display);

    ViewportHandle handle = new(
      new X11WindowHandle()
      {
        Display = this.display,
        Window = window
      }
    );

    this.openViewports.Add(window, viewport);

    viewport.AsInternal.Validate(handle);
  }

  protected override void PollEvents()
  {
    while (X11.XPending(this.display) > 0)
    {
      X11.XNextEvent(this.display, out var xEvent);
      Viewport? viewport;

      switch (xEvent.Type)
      {
        case X11.XEventType.ClientMessage:
          if (xEvent.XClient.MessageType == this.atoms.WMNullAtom)
          {
            OpenWindowsFromQueue();
          }
          else if (xEvent.XClient.MessageType == this.atoms.WMProtocolAtom)
          {
            if ((UIntPtr)xEvent.XClient.Data.L[0] == this.atoms.WMDeleteAtom)
            {
              if (!this.openViewports.TryGetValue(xEvent.XClient.Window, out viewport)) return;
              viewport.Close();
            }
          }
          break;

        case X11.XEventType.Expose:
          if (this.openViewports.TryGetValue(xEvent.XExpose.Window, out viewport))
          {
            viewport.AsInternal.ChangeInternalVisibility(true);
          }
          break;

        case X11.XEventType.Configure:
          if (this.openViewports.TryGetValue(xEvent.XConfigure.Window, out viewport))
          {
            viewport.AsInternal.ChangeInternalPosition(xEvent.XConfigure.X, xEvent.XConfigure.Y);
            viewport.AsInternal.ChangeInternalSize((uint)xEvent.XConfigure.Width, (uint)xEvent.XConfigure.Height);
          }
          break;

        case X11.XEventType.Map:
          if (this.openViewports.TryGetValue(xEvent.XMap.Window, out viewport))
          {
            viewport.AsInternal.ChangeInternalVisibility(true);
          }
          break;

        case X11.XEventType.Unmap:
          if (this.openViewports.TryGetValue(xEvent.XUnmap.Window, out viewport))
          {
            viewport.AsInternal.ChangeInternalVisibility(false);
          }
          break;

        case X11.XEventType.FocusIn:
          if (this.openViewports.TryGetValue(xEvent.XFocusChange.Window, out viewport))
          {
            viewport.AsInternal.ChangeInternalFocus(true);
          }
          break;

        case X11.XEventType.FocusOut:
          if (this.openViewports.TryGetValue(xEvent.XFocusChange.Window, out viewport))
          {
            viewport.AsInternal.ChangeInternalFocus(false);
          }
          break;
      }

      var e = this.OnPollEvents;
      e?.Invoke(new());
    }
  }

  protected override void WakeUpEventLoop()
  {
    this.PostAtomToRoot(this.atoms.WMNullAtom);
  }



  public override int OpenViewportCount => this.openViewports.Count;

  public override event Action? OnAllViewportsClosed;
  public override event Action<ViewportEvent>? OnPollEvents;

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
      this.openViewports.Remove(handle.X11.Window);
      X11.XDestroyWindow(this.display, handle.X11.Window);
      viewport.AsInternal.Invalidate();
    }
  }

  public override void SetFocus(Viewport viewport, bool isFocused)
  {
    viewport.AsInternal.CheckBoundTo(this);
    viewport.AsInternal.CheckAlive();
    if (isFocused) X11.XSetInputFocus(this.display, viewport.Handle.X11.Window, X11.RevertToNone, 0);
  }

  public override void SetPosition(Viewport viewport, int x, int y)
  {
    viewport.AsInternal.CheckBoundTo(this);
    viewport.AsInternal.CheckAlive();
    X11.XMoveWindow(this.display, viewport.Handle.X11.Window, x, y);
  }

  public override void SetSize(Viewport viewport, uint width, uint height)
  {
    viewport.AsInternal.CheckBoundTo(this);
    viewport.AsInternal.CheckAlive();
    X11.XResizeWindow(this.display, viewport.Handle.X11.Window, width, height);
  }

  public override void SetSizePosition(Viewport viewport, uint width, uint height, int x, int y)
  {
    viewport.AsInternal.CheckBoundTo(this);
    viewport.AsInternal.CheckAlive();
    X11.XMoveResizeWindow(this.display, viewport.Handle.X11.Window, x, y, width, height);
  }

  public override void SetTitle(Viewport viewport, string text)
  {
    viewport.AsInternal.CheckBoundTo(this);
    viewport.AsInternal.CheckAlive();
    X11.XStoreName(this.display, viewport.Handle.X11.Window, text);
  }




  private IntPtr display;
  private int screen;
  private ulong rootWindow;
  private X11Atoms atoms;



  public X11WindowManagerServer()
  : base()
  {
    this.display = X11.XOpenDisplay(null);
    if (this.display == IntPtr.Zero)
    {
      throw new Exception("Could not astablish link to X11 server.");
    }

    this.screen = X11.XDefaultScreen(this.display);
    this.rootWindow = X11.XRootWindow(this.display, this.screen);
    this.atoms = new(this.display);


    X11.XSelectInput(this.display, this.rootWindow, X11.XEventMask.SubstructureNotifyMask);
    X11.XFlush(this.display);
  }



  private void PostAtomToRoot(UIntPtr atom)
  {
    X11.XEvent evt = new X11.XEvent { Type = X11.XEventType.ClientMessage };
    evt.XClient.Display = this.display;
    evt.XClient.Window = this.rootWindow;
    evt.XClient.MessageType = atom;
    evt.XClient.Format = 32;

    X11.XSendEvent(this.display, this.rootWindow, 0, X11.XEventMask.SubstructureNotifyMask, ref evt);
    X11.XFlush(this.display);
  }



  private struct X11Atoms
  {
    public X11Atoms(IntPtr display)
    {
      this.WMNullAtom = X11.XInternAtom(display, X11.WM_USER_NULL, 0);
      this.WMDeleteAtom = X11.XInternAtom(display, X11.WM_DELETE_WINDOW, 0);
      this.WMInitWindowsAtom = X11.XInternAtom(display, X11.WM_USER_WINDOW_INIT, 0);
      this.WMQuitAtom = X11.XInternAtom(display, X11.WM_USER_QUIT, X11.Bool32.False);
      this.WMProtocolAtom = X11.XInternAtom(display, X11.WM_PROTOCOLS, X11.Bool32.False);
      this.WMDestroyWindowAtom = X11.XInternAtom(display, X11.WM_USER_DESTROY_WINDOW, 0);
      this.WMFocusWindowAtom = X11.XInternAtom(display, X11.WM_USER_FOCUS_WINDOW, 0);
    }

    public UIntPtr WMNullAtom;
    public UIntPtr WMDeleteAtom;
    public UIntPtr WMInitWindowsAtom;
    public UIntPtr WMQuitAtom;
    public UIntPtr WMProtocolAtom;
    public UIntPtr WMDestroyWindowAtom;
    public UIntPtr WMFocusWindowAtom;
  }
}