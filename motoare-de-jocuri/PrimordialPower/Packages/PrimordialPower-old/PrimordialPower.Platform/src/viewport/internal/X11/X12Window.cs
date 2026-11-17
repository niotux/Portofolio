using System;
using PrimordialPower.Runtime;

namespace PrimordialPower.Platform.X11;

internal class X12Window : IViewportInternal
{
  private readonly IntPtr display;
  private readonly ulong window;
  private readonly int screen;
  private readonly X11Atoms atoms;
  public X12Window(
    IntPtr display,
    int screen,
    ulong rootWindow,
    in X11Atoms atoms,
    in ViewportParams parameters
  )
  {
    this.display = display;
    this.screen = screen;
    this.atoms = atoms;

    if ((parameters.Flags & ViewportFlags.WithShutdownEvents) == ViewportFlags.WithShutdownEvents)
    {
      this.OnDestroy += Launcher.Main.Shutdown;
    }

    ulong blackPixel = API.XBlackPixel(display, screen);
    ulong whitePixel = API.XWhitePixel(display, screen);

    uint safeWidth = parameters.Width;
    uint safeHeight = parameters.Height;
    if (parameters.Width <= 0)
    {
      safeWidth = 1;
    }
    if (parameters.Height <= 0)
    {
      safeHeight = 1;
    }

    this.window = API.XCreateSimpleWindow(
      display: display,
      parent: rootWindow,
      x: parameters.X,
      y: parameters.Y,
      width: safeWidth,
      height: safeHeight,
      borderWidth: 1,
      border: blackPixel,
      background: blackPixel
    );

    if (this.window == 0)
    {
      throw new Exception("Failed to create X11 window.");
    }

    API.XStoreName(display, this.window, parameters.Title);

    API.XEventMask mask =
      API.XEventMask.ExposureMask |
      API.XEventMask.FocusChangeMask |
      API.XEventMask.StructureNotifyMask;

    API.XSelectInput(
      display,
      this.window,
      mask
    );

    API.XSetWMProtocols(display, this.window, ref this.atoms.WMDeleteAtom, 1);

    if ((parameters.Flags & ViewportFlags.Hidden) != ViewportFlags.Hidden)
    {
      API.XMapWindow(display, this.window);
      API.XFlush(display);
    }

    this.width = parameters.Width;
    this.height = parameters.Height;
    this.x = parameters.X;
    this.y = parameters.Y;
    this.title = parameters.Title;
    this.minDepth = parameters.MinDepth;
    this.maxDepth = parameters.MaxDepth;
  }



  public ViewportNative Native => new(new X11WindowInfo(this.display, this.window));
  public ViewportData Data => new()
  {
    Width = this.width,
    Height = this.height,
    XOffset = 0,
    YOffset = 0,
    MinDepth = this.minDepth,
    MaxDepth = this.maxDepth,
    Native = this.Native,
  };



  public volatile uint isClosed = 0;
  public bool IsClosed => this.isClosed != 0;
  public void Close()
  {
    try
    {
      uint closed = Interlocked.Exchange(ref this.isClosed, 1);
      if (closed != 0) return;

      var xEvent = new API.XEvent();
      xEvent.Type = API.XEventType.ClientMessage;
      ref var client = ref xEvent.XClient;
      client.Display = this.display;
      client.Window = this.window;
      client.MessageType = this.atoms.WMProtocolAtom;
      client.Format = 32;
      client.Data.L[0] = (long)this.atoms.WMDeleteAtom;

      API.XSendEvent(this.display, this.window, API.Bool32.False, API.XEventMask.StructureNotifyMask, ref xEvent);
    }
    catch (Exception ex)
    {
      Destroy();
      throw new Exception($"X11 failed to close window in a managed way and was instead forcely destroyed:\n{ex.Message}");
    }
  }



  private volatile uint isDestroyed = 0;
  public void Destroy()
  {
    uint destroyed = Interlocked.Exchange(ref this.isDestroyed, 1);
    if (destroyed != 0) return;

    OnDestroy?.Invoke();

    API.XDestroyWindow(this.display, this.window);
    API.XFlush(this.display);
  }

  public bool ConfirmDestroy()
  {
    var onClose = this.OnClose;
    var confirmClose = this.ConfirmClose;

    onClose?.Invoke();
    if (confirmClose?.Invoke() == false) return false;

    return true;
  }

  public event Action? OnClose;
  public event Func<bool>? ConfirmClose;
  public event Action? OnDestroy;



  private volatile uint updated = 0;
  public bool Updated => Interlocked.Exchange(ref this.updated, 0) != 0;
  public void Update()
  {
    this.updated = 1;
  }



  private volatile bool isFocused = true;
  public bool IsActive => this.isFocused;
  public void UpdateInternalActivation(bool focus)
  {
    this.isFocused = focus;
  }



  private readonly object lockTitle = new();
  private volatile string title;
  public string Title
  {
    get => title;
    set
    {
      lock (lockTitle)
      {
        // API.XStoreName(this.display, this.window, value);
        this.title = value;
      }
    }
  }



  private volatile uint width;
  private volatile uint height;
  private readonly object lockSize = new();
  public (uint Width, uint Height) Size
  {
    get => (this.width, this.height);
    set
    {
      lock (lockSize)
      {

      }
    }
  }
  public void UpdateInternalSize(uint width, uint height)
  {
    this.width = width;
    this.height = height;
  }



  private volatile int x;
  private volatile int y;
  private readonly object lockPosition = new();
  public (int X, int Y) Position
  {
    get => (this.x, this.y);
    set
    {
      lock (lockPosition)
      {

      }
    }
  }
  public void UpdateInternalPosition(int x, int y)
  {
    this.x = x;
    this.y = y;
  }



  private volatile float minDepth;
  private volatile float maxDepth;
  private readonly object lockDepth = new();
  public (float Min, float Max) Depth
  {
    get => (this.minDepth, this.maxDepth);
    set
    {
      lock (lockDepth)
      {
        Update();
        this.minDepth = value.Min;
        this.maxDepth = value.Max;
      }
    }
  }
}
