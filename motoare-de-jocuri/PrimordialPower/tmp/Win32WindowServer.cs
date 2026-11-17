using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace PrimordialPower.Core.Win32;

internal sealed partial class Win32WindowServer : IViewportServer<Window>
{
  private struct WindowInitData
  {
    public required string Title;
    public required uint Width;
    public required uint Height;
    public required int X;
    public required int Y;
    public required float MinDepth;
    public required float MaxDepth;
  }


  private readonly ConcurrentQueue<(Window Window, WindowInitData Data, SemaphoreSlim Semaphore)> toInit = new();
  private readonly SemaphoreSlim unlockMsgLoop = new(0);
  public async Task<Window> CreateViewport(string title, uint width, uint height, int x, int y, float minDepth, float maxDepth)
  {
    var window = new Window();
    await InitializeViewport(window, title, width, height, x, y, minDepth, maxDepth);
    return window;
  }



  public async Task InitializeViewport(Window viewport, string title, uint width, uint height, int x, int y, float minDepth, float maxDepth)
  {
    var data = new WindowInitData
    {
      Title = title,
      Width = width,
      Height = height,
      X = x,
      Y = y,
      MinDepth = minDepth,
      MaxDepth = maxDepth,
    };
    using var semaphore = new SemaphoreSlim(0, 1);

    this.toInit.Enqueue((viewport, data, semaphore));
    _PostThreadMessage(API.WM_USER_WINDOW_INIT);
    await semaphore.WaitAsync();
  }



  private void _RemoveWindow(IntPtr hWnd)
  {
    this.openWindows.Remove(hWnd);
    this.viewportCount--;

    if (this.viewportCount == 0)
    {
      var ev = this.OnZeroViewports;
      ev?.Invoke();
    }
  }



  private static readonly ConcurrentDictionary<IntPtr, Window> _openWindowsGlobal = new();
  private readonly Dictionary<IntPtr, Window> openWindows = new();
  private void _InitializeWindows()
  {
    while (this.toInit.TryDequeue(out var WDS))
    {
      try
      {
        if (WDS.Window.Initialized)
        {
          return;
        }

        WDS.Window.Initialize(new Win32Window(
          windowClass: Def.WindowClass,
          title: WDS.Data.Title,
          width: WDS.Data.Width,
          height: WDS.Data.Height,
          x: WDS.Data.X,
          y: WDS.Data.Y,
          minDepth: WDS.Data.MinDepth,
          maxDepth: WDS.Data.MaxDepth
        ));

        IntPtr key = WDS.Window.Native.Win32.HWnd;
        _openWindowsGlobal[key] = WDS.Window;
        this.openWindows[key] = WDS.Window;

        this.viewportCount++;
      }
      finally
      {
        WDS.Semaphore.Release();
      }
    }
  }




  private uint messageLoopThreadID;
  private void _PostThreadMessage(uint windowMessage, IntPtr wParam = 0, IntPtr lParam = 0)
  {
    API.PostThreadMessageW(this.messageLoopThreadID, windowMessage, wParam, lParam);
  }


  public void Init(CancellationToken cancellationToken)
  {
    this.messageLoopThreadID = API.GetCurrentThreadId();
    API.PeekMessageW(out _, IntPtr.Zero, 0, 0, 0);

    // cancellationToken.Register(
    //   () => _PostThreadMessage(API.WM_QUIT)
    // );
  }



  public void Start(CancellationToken cancellationToken)
  {
    if (API.GetCurrentThreadId() != this.messageLoopThreadID)
    {
      throw new ThreadInterruptedException("Win32WindowManager requires the same thread to be valid both in Init() and Run()");
    }

    while (!cancellationToken.IsCancellationRequested)
    {
      _PollWindowMessages();
    }
    _CloseAll();
  }

  public void Loop()
  {
    _PollWindowMessages();
  }


  public void SignalShutdown()
  {
    _PostThreadMessage(API.WM_USER_QUIT);
    _CloseAll();
  }




  private volatile uint viewportCount;
  public uint ViewportCount
  {
    get => this.viewportCount;
  }


  public event Func<Task>? OnZeroViewports;


  private void _CloseAll()
  {
    foreach (var window in this.openWindows)
    {
      window.Value.Close();
      _openWindowsGlobal.Remove(window.Key, out _);
    }
    this.openWindows.Clear();
  }



  private void _PollWindowMessages()
  {
    int result = API.GetMessageW(out API.MSG msg, IntPtr.Zero, 0, 0);

    API.TranslateMessage(ref msg);

    if (result < 0)
    {
      return;
    }
    if (result == 0 || msg.message == API.WM_QUIT)
    {
      SignalShutdown();
      return;
    }

    if (msg.message == API.WM_USER_WINDOW_INIT)
      _InitializeWindows();

    if (msg.message == API.WM_USER_WINDOW_DESTROY)
    {
      _RemoveWindow(msg.hWnd);
    }

    API.DispatchMessageW(ref msg);
  }
}
