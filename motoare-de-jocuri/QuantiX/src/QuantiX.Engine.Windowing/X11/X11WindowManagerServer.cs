using System.Runtime.Versioning;
using QuantiX.Engine.Platform.Linux;

namespace QuantiX.Engine.Windowing.X11;

[SupportedOSPlatform("linux")]
public sealed class X11WindowManagerServer : WindowManagerServerBase
{
  private readonly Dictionary<ulong, Window> openWindow = new();



  protected override void OpenImpl(Window surface)
  {
    ulong blackPixel = X11Api.XBlackPixel(this.display, screen);
    ulong whitePixel = X11Api.XWhitePixel(this.display, screen);

    uint safeWidth = surface.Size.Width;
    uint safeHeight = surface.Size.Height;
    if (safeWidth <= 0)
    {
      safeWidth = 1;
    }
    if (safeHeight <= 0)
    {
      safeHeight = 1;
    }

    ulong window = X11Api.XCreateSimpleWindow(
      display: this.display,
      parent: this.rootWindow,
      x: surface.Position.X,
      y: surface.Position.Y,
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

    X11Api.XStoreName(this.display, window, surface.Title);

    X11Api.XEventMask mask =
      X11Api.XEventMask.ExposureMask |
      X11Api.XEventMask.FocusChangeMask |
      X11Api.XEventMask.StructureNotifyMask;

    X11Api.XSelectInput(
      this.display,
      window,
      mask
    );
    X11Api.XSetWMProtocols(this.display, window, ref this.atoms.WMDeleteAtom, 1);
    X11Api.XMapWindow(this.display, window);

    X11Api.XFlush(this.display);

    WindowHandle handle = new(
      new X11WindowHandle()
      {
        Display = this.display,
        Window = window
      }
    );

    this.openWindow.Add(window, surface);

    surface.AsInternal.Validate(handle);
  }

  protected override void PollEvents()
  {
    while (X11Api.XPending(this.display) > 0)
    {
      X11Api.XNextEvent(this.display, out var xEvent);
      Window? surface;

      switch (xEvent.Type)
      {
        case X11Api.XEventType.ClientMessage:
          if (xEvent.XClient.MessageType == this.atoms.WMNullAtom)
          {
            OpenWindowsFromQueue();
          }
          else if (xEvent.XClient.MessageType == this.atoms.WMProtocolAtom)
          {
            if ((UIntPtr)xEvent.XClient.Data.L[0] == this.atoms.WMDeleteAtom)
            {
              if (!this.openWindow.TryGetValue(xEvent.XClient.Window, out surface)) return;
              surface.Close();
            }
          }
          break;

        case X11Api.XEventType.Expose:
          if (this.openWindow.TryGetValue(xEvent.XExpose.Window, out surface))
          {
            surface.AsInternal.ChangeInternalVisibility(true);
          }
          break;

        case X11Api.XEventType.Configure:
          if (this.openWindow.TryGetValue(xEvent.XConfigure.Window, out surface))
          {
            surface.AsInternal.ChangeInternalPosition(xEvent.XConfigure.X, xEvent.XConfigure.Y);
            surface.AsInternal.ChangeInternalSize((uint)xEvent.XConfigure.Width, (uint)xEvent.XConfigure.Height);
          }
          break;

        case X11Api.XEventType.Map:
          if (this.openWindow.TryGetValue(xEvent.XMap.Window, out surface))
          {
            surface.AsInternal.ChangeInternalVisibility(true);
          }
          break;

        case X11Api.XEventType.Unmap:
          if (this.openWindow.TryGetValue(xEvent.XUnmap.Window, out surface))
          {
            surface.AsInternal.ChangeInternalVisibility(false);
          }
          break;

        case X11Api.XEventType.FocusIn:
          if (this.openWindow.TryGetValue(xEvent.XFocusChange.Window, out surface))
          {
            surface.AsInternal.ChangeInternalFocus(true);
          }
          break;

        case X11Api.XEventType.FocusOut:
          if (this.openWindow.TryGetValue(xEvent.XFocusChange.Window, out surface))
          {
            surface.AsInternal.ChangeInternalFocus(false);
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



  public override int OpenCount => this.openWindow.Count;

  public override event Action? OnAllClosed;
  public override event Action<WindowEvent>? OnPollEvents;

  public override void CloseAll()
  {
    lock (base.lockOpening)
    {
      foreach (var kvpSurface in this.openWindow)
      {
        Close(kvpSurface.Value);
      }
    }
  }

  public override void Destroy(Window surface)
  {
    surface.AsInternal.CheckBoundTo(this);
    surface.AsInternal.CheckAlive();
    lock (base.lockOpening)
    {
      if (this.openWindow.Count == 1)
      {
        var e = this.OnAllClosed;
        e?.Invoke();
      }
      WindowHandle handle = surface.Handle;
      surface.AsInternal.Invalidate();
      this.openWindow.Remove(handle.X11.Window);
      X11Api.XDestroyWindow(this.display, handle.X11.Window);
      surface.AsInternal.Invalidate();
    }
  }

  public override void SetFocus(Window surface, bool isFocused)
  {
    surface.AsInternal.CheckBoundTo(this);
    surface.AsInternal.CheckAlive();
    if (isFocused) X11Api.XSetInputFocus(this.display, surface.Handle.X11.Window, X11Api.RevertToNone, 0);
  }

  public override void SetPosition(Window surface, int x, int y)
  {
    surface.AsInternal.CheckBoundTo(this);
    surface.AsInternal.CheckAlive();
    X11Api.XMoveWindow(this.display, surface.Handle.X11.Window, x, y);
  }

  public override void SetSize(Window surface, uint width, uint height)
  {
    surface.AsInternal.CheckBoundTo(this);
    surface.AsInternal.CheckAlive();
    X11Api.XResizeWindow(this.display, surface.Handle.X11.Window, width, height);
  }

  public override void SetSizePosition(Window surface, uint width, uint height, int x, int y)
  {
    surface.AsInternal.CheckBoundTo(this);
    surface.AsInternal.CheckAlive();
    X11Api.XMoveResizeWindow(this.display, surface.Handle.X11.Window, x, y, width, height);
  }

  public override void SetTitle(Window surface, string text)
  {
    surface.AsInternal.CheckBoundTo(this);
    surface.AsInternal.CheckAlive();
    X11Api.XStoreName(this.display, surface.Handle.X11.Window, text);
  }




  private IntPtr display;
  private int screen;
  private ulong rootWindow;
  private X11Atoms atoms;



  public X11WindowManagerServer() :
    base()
  {
    this.display = X11Api.XOpenDisplay(null);
    if (this.display == IntPtr.Zero)
    {
      throw new Exception("Could not astablish link to X11 server.");
    }

    this.screen = X11Api.XDefaultScreen(this.display);
    this.rootWindow = X11Api.XRootWindow(this.display, this.screen);
    this.atoms = new(this.display);


    X11Api.XSelectInput(this.display, this.rootWindow, X11Api.XEventMask.SubstructureNotifyMask);
    X11Api.XFlush(this.display);
  }



  private void PostAtomToRoot(UIntPtr atom)
  {
    X11Api.XEvent evt = new X11Api.XEvent { Type = X11Api.XEventType.ClientMessage };
    evt.XClient.Display = this.display;
    evt.XClient.Window = this.rootWindow;
    evt.XClient.MessageType = atom;
    evt.XClient.Format = 32;

    X11Api.XSendEvent(this.display, this.rootWindow, 0, X11Api.XEventMask.SubstructureNotifyMask, ref evt);
    X11Api.XFlush(this.display);
  }



  private struct X11Atoms
  {
    public X11Atoms(IntPtr display)
    {
      this.WMNullAtom = X11Api.XInternAtom(display, X11Api.WM_USER_NULL, 0);
      this.WMDeleteAtom = X11Api.XInternAtom(display, X11Api.WM_DELETE_WINDOW, 0);
      this.WMInitWindowsAtom = X11Api.XInternAtom(display, X11Api.WM_USER_WINDOW_INIT, 0);
      this.WMQuitAtom = X11Api.XInternAtom(display, X11Api.WM_USER_QUIT, X11Api.Bool32.False);
      this.WMProtocolAtom = X11Api.XInternAtom(display, X11Api.WM_PROTOCOLS, X11Api.Bool32.False);
      this.WMDestroyWindowAtom = X11Api.XInternAtom(display, X11Api.WM_USER_DESTROY_WINDOW, 0);
      this.WMFocusWindowAtom = X11Api.XInternAtom(display, X11Api.WM_USER_FOCUS_WINDOW, 0);
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