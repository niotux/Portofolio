using System;
using System.Collections.Concurrent;
using PrimordialPower.Runtime;

namespace PrimordialPower.Platform.X11;

internal class X12WindowManagerServer : Server, IViewportManager<Viewport>
{
  public uint ViewportCount { get; private set; }
  public event Func<Task>? OnZeroViewports;



  public void CloseAll()
  {
    foreach (var window in openWindows.Values)
    {
      window.Close();
    }
  }

  public void Close(Viewport window)
  {
    window.Close();
  }



  private readonly ConcurrentDictionary<ulong, Viewport> openWindows = new();



  public Task<Viewport> CreateViewport(
    string title,
    uint width,
    uint height,
    float minDepth,
    float maxDepth,
    int x = ViewportParams.DefaultX,
    int y = ViewportParams.DefaultY
  )
  {
    var info = new ViewportParams()
    {
      Title = title,
      Width = width,
      Height = height,
      X = x,
      Y = y,
      MinDepth = minDepth,
      MaxDepth = maxDepth,
    };

    return CreateViewport(info);
  }

  public Task<Viewport> CreateViewport(
    ViewportFlags template,
    string title,
    uint width,
    uint height,
    float minDepth,
    float maxDepth,
    int x = ViewportParams.DefaultX,
    int y = ViewportParams.DefaultY
  )
  {
    var info = new ViewportParams()
    {
      Flags = template,
      Title = title,
      Width = width,
      Height = height,
      X = x,
      Y = y,
      MinDepth = minDepth,
      MaxDepth = maxDepth,
    };

    return CreateViewport(info);
  }

  public async Task<Viewport> CreateViewport(ViewportParams info)
  {
    var window = new Viewport();
    await InitializeViewport(window, info);
    return window;
  }



  public Task InitializeViewport(
    Viewport viewport,
    string title,
    uint width,
    uint height,
    float minDepth,
    float maxDepth,
    int x = ViewportParams.DefaultX,
    int y = ViewportParams.DefaultY
  )
  {
    var info = new ViewportParams()
    {
      Title = title,
      Width = width,
      Height = height,
      X = x,
      Y = y,
      MinDepth = minDepth,
      MaxDepth = maxDepth,
    };

    return InitializeViewport(viewport, info);
  }

  public Task InitializeViewport(
    Viewport viewport,
    ViewportFlags template,
    string title,
    uint width,
    uint height,
    float minDepth,
    float maxDepth,
    int x = ViewportParams.DefaultX,
    int y = ViewportParams.DefaultY
  )
  {
    var info = new ViewportParams()
    {
      Flags = template,
      Title = title,
      Width = width,
      Height = height,
      X = x,
      Y = y,
      MinDepth = minDepth,
      MaxDepth = maxDepth,
    };

    return InitializeViewport(viewport, info);
  }

  public async Task InitializeViewport(Viewport viewport, ViewportParams info)
  {
    var config = new WindowInitConfig()
    {
      Window = viewport,
      Params = info
    };
    AddConfigToInitQueue(config);
    await config.Semaphore.WaitAsync();

    if (!viewport.Initialized) throw new Exception($"Viewport \"{info.Title}\" failed to initialize");
  }



  private void InitializeWindow(in WindowInitConfig config)
  {
    if (config.Window.Initialized) return;
    try
    {
      var core = new X11.X12Window(
        this.display,
        this.screen,
        this.rootWindow,
        this.atoms,
        config.Params
      );
      config.Window.Initialize(core);

      AddWindow(config.Window);
    }
    finally
    {
      config.Semaphore.Release();
    }
  }



  private readonly ConcurrentQueue<WindowInitConfig> windowsToInitQueue = new();
  private volatile uint isPendingWindowInits = 0;
  private void AddConfigToInitQueue(in WindowInitConfig config)
  {
    this.windowsToInitQueue.Enqueue(config);
    uint prev = Interlocked.CompareExchange(ref this.isPendingWindowInits, 1, 0);
    if (prev == 0)
    {
      PostAtomToRoot(this.atoms.WMInitWindowsAtom);
    }
  }

  private void InitializeWindowsFromQueue()
  {
    while (this.windowsToInitQueue.TryDequeue(out var config))
    {
      InitializeWindow(config);
    }
    this.isPendingWindowInits = 0;

    if (this.windowsToInitQueue.IsEmpty) return;

    if (Interlocked.CompareExchange(ref this.isPendingWindowInits, 1, 0) == 0)
    {
      PostAtomToRoot(this.atoms.WMInitWindowsAtom);
    }
  }



  private void RemoveWindow(Viewport window)
  {
    RemoveWindow(window.Native.X11.Window, out _);
  }

  private void RemoveWindow(ulong id, out Viewport? window)
  {
    this.ViewportCount--;
    this.openWindows.Remove(id, out window);

    if (this.ViewportCount <= 0)
    {
      OnZeroViewports?.Invoke();
    }
  }

  private void AddWindow(Viewport window)
  {
    this.openWindows[window.Native.X11.Window] = window;
    this.ViewportCount++;
  }



  private void PostAtomToRoot(UIntPtr atom)
  {
    API.XEvent evt = new API.XEvent { Type = API.XEventType.ClientMessage };
    evt.XClient.Display = this.display;
    evt.XClient.Window = this.rootWindow;
    evt.XClient.MessageType = atom;
    evt.XClient.Format = 32;

    API.XSendEvent(this.display, this.rootWindow, API.Bool32.False, API.XEventMask.SubstructureNotifyMask, ref evt);
    API.XFlush(this.display);
  }



  private void DrainEvents()
  {
    while (API.XPending(this.display) > 0)
    {
      API.XNextEvent(this.display, out var nextEvt);
      HandleAllEvents(nextEvt);
    }
  }

  private void HandleClientMessage(in API.XEvent xEvent)
  {
    ref readonly var client = ref xEvent.XClient;

    if (client.MessageType == this.atoms.WMInitWindowsAtom)
    {
      InitializeWindowsFromQueue();
    }
    else if (client.MessageType == this.atoms.WMProtocolAtom)
    {
      if ((UIntPtr)client.Data.L[0] == this.atoms.WMDeleteAtom)
      {
        if (!this.openWindows.TryGetValue(xEvent.XClient.Window, out var window)) return;
        if (window.Core?.ConfirmDestroy() == true)
        {
          window.Destroy();
        }
      }
    }
  }

  private void HandleAllEvents(in API.XEvent xEvent)
  {
    Viewport? window;

    switch (xEvent.Type)
    {
      case API.XEventType.ClientMessage:
        HandleClientMessage(xEvent);
        break;

      case API.XEventType.DestroyWindow:
        RemoveWindow(xEvent.XDestroyWindow.Window, out _);
        break;

      case API.XEventType.Expose:
        if (!this.openWindows.TryGetValue(xEvent.XExpose.Window, out window)) return;
        window.Core?.Update();
        break;

      case API.XEventType.Configure:
        if (!this.openWindows.TryGetValue(xEvent.XConfigure.Window, out window)) return;
        window.Core?.UpdateInternalSize(
          (uint)xEvent.XConfigure.Width,
          (uint)xEvent.XConfigure.Height
        );
        window.Core?.UpdateInternalPosition(
          xEvent.XConfigure.X,
          xEvent.XConfigure.Y
        );
        break;

      case API.XEventType.Map:
        break;

      case API.XEventType.Unmap:
        break;

      case API.XEventType.FocusIn:
        if (!this.openWindows.TryGetValue(xEvent.XFocusChange.Window, out window)) return;
        window.Core?.UpdateInternalActivation(true);
        break;

      case API.XEventType.FocusOut:
        if (!this.openWindows.TryGetValue(xEvent.XFocusChange.Window, out window)) return;
        window.Core?.UpdateInternalActivation(false);
        break;
    }
  }



  public override ServerType Type => ServerType.MonoThread;

  private IntPtr display;
  private int screen;
  private ulong rootWindow;
  private uint loopThreadId;
  private X11Atoms atoms;
  public override void Init()
  {
    this.display = API.XOpenDisplay(null);
    if (this.display == IntPtr.Zero)
    {
      throw new Exception("Could not astablish link to X11 server.");
    }

    this.screen = API.XDefaultScreen(this.display);
    this.rootWindow = API.XRootWindow(this.display, this.screen);
    this.loopThreadId = (uint)Environment.CurrentManagedThreadId;

    this.atoms = new X11Atoms(this.display);

    API.XSelectInput(this.display, this.rootWindow, API.XEventMask.SubstructureNotifyMask);
  }

  public override void Start()
  {
    if (this.loopThreadId != (uint)Environment.CurrentManagedThreadId)
    {
      throw new Exception("X11WindowServer requires to initialize and continue to run on the same thread.");
    }
  }

  public override bool Loop()
  {
    DrainEvents();

    return this.Running;
  }

  public override void Shutdown()
  {
    CloseAll();
    DrainEvents();

    if (this.display != 0)
    {
      API.XCloseDisplay(this.display);
      this.display = IntPtr.Zero;
    }
  }

  public override void SignalShutdown()
  {
    this.Running = StopLoop;
    PostAtomToRoot(this.atoms.WMQuitAtom);
  }
}
