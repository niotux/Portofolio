using System.Collections.Concurrent;
using PrimordialPower.Runtime;

namespace PrimordialPower.Platform.Win32;

internal sealed partial class Win32WindowManagerServer : Server, IViewportManager<Viewport>
{
  private uint loopThreadID;
  private void PostThreadMessage(uint windowMessage, IntPtr wParam = 0, IntPtr lParam = 0)
  {
    API.PostThreadMessageW(this.loopThreadID, windowMessage, wParam, lParam);
  }



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



  private readonly Dictionary<IntPtr, Viewport> openWindows = new();
  private static readonly ConcurrentDictionary<IntPtr, Viewport> _openWindowsGlobal = new();



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
    ViewportFlags flags,
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
      Flags = flags,
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
    var info = new ViewportParams
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
    ViewportFlags flags,
    string title,
    uint width,
    uint height,
    float minDepth,
    float maxDepth,
    int x = ViewportParams.DefaultX,
    int y = ViewportParams.DefaultY
  )
  {
    var info = new ViewportParams
    {
      Flags = flags,
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
      var core = new Win32.Win32Window(
        Def.WindowClass,
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
  private void InitializeWindowsFromQueue()
  {
    if (this.windowsToInitQueue.TryDequeue(out var config))
    {
      InitializeWindow(config);
    }
  }



  private void RemoveWindow(Viewport window)
  {
    RemoveWindow(window.Native.Win32.HWnd, out _);
  }

  private void RemoveWindow(IntPtr hWnd, out Viewport? window)
  {
    this.ViewportCount--;
    this.openWindows.Remove(hWnd);
    _openWindowsGlobal.Remove(hWnd, out window);

    if (this.ViewportCount <= 0)
    {
      OnZeroViewports?.Invoke();
    }
  }

  private void AddWindow(Viewport window)
  {
    this.openWindows[window.Native.Win32.HWnd] = window;
    _openWindowsGlobal[window.Native.Win32.HWnd] = window;
    this.ViewportCount++;
  }



  private void AddConfigToInitQueue(in WindowInitConfig config)
  {
    this.windowsToInitQueue.Enqueue(config);
    PostThreadMessage(API.WM_USER_WINDOW_INIT);
  }



  private void PollEvents()
  {
    int result = API.GetMessageW(out var msg, IntPtr.Zero, 0, 0);
    API.TranslateMessage(ref msg);

    if (result < 0) return;
    if (result == 0)
    {
      SignalShutdown();
      return;
    }

    switch (msg.Message)
    {
      case API.WM_USER_WINDOW_INIT:
        InitializeWindowsFromQueue();
        break;

      case API.WM_USER_WINDOW_DESTROY:
        RemoveWindow(msg.HWnd, out var window);
        break;
    }

    API.DispatchMessageW(ref msg);
  }



  public override ServerType Type => ServerType.MonoThread;

  public override void Init()
  {
    API.PeekMessageW(out _, IntPtr.Zero, 0, 0, 0);
    this.loopThreadID = API.GetCurrentThreadId();
  }

  public override void Start()
  {
    if (this.loopThreadID != API.GetCurrentThreadId())
    {
      throw new Exception("Win32WindowServer requires to initialize and continue to run on the same OS thread.");
    }
  }

  public override bool Loop()
  {
    PollEvents();

    return this.Running;
  }

  public override void Shutdown()
  {
    CloseAll();
    while (API.PeekMessageW(out var msg, IntPtr.Zero, 0, 0, API.PM_REMOVE))
    {
      API.TranslateMessage(ref msg);
      API.DispatchMessageW(ref msg);
      if (msg.Message == API.WM_USER_WINDOW_DESTROY)
      {
        RemoveWindow(msg.HWnd, out var window);
      }
    }
  }

  public override void SignalShutdown()
  {
    PostThreadMessage(API.WM_USER_QUIT);
    this.Running = StopLoop;
  }
}
