using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.VisualBasic;

namespace PrimordialPower.Core.Win32;

internal sealed class oldWin32WindowManager : IViewportManagerCore
{
  public oldWin32WindowManager()
  {
    _RegisterWindowClasses();
  }
  private void _RegisterWindowClasses()
  {
    UInt16 atom = API.RegisterClassW(ref _WindowClass);
    if (atom == 0)
    {
      throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }
  }


  private static volatile bool _ShutdownRequested = false;
  private static volatile uint _CreationsInProgress = 0;
  private static readonly TaskCompletionSource _CreationFinished = new();


  public ConcurrentEvent.Action OnViewportCreation { get; } = new();
  private ConcurrentQueue<(IViewport Viewport, WindowInitData Data, TaskCompletionSource TCS)> _ToInit = new();
  public async Task<IViewport> CreateViewportRequest(string title, uint width, uint height, int x, int y, float minDepth, float maxDepth)
  {
    Interlocked.Increment(ref _CreationsInProgress);
    if (_ShutdownRequested)
      return new Window();

    var initData = new WindowInitData()
    {
      Title = title,
      Width = width,
      Height = height,
      X = x,
      Y = y,
      MinDepth = minDepth,
      MaxDepth = maxDepth,
    };
    var viewport = new Window();
    var tcs = new TaskCompletionSource();
    _ToInit.Enqueue((viewport, initData, tcs));
    Interlocked.Decrement(ref _CreationsInProgress);
    if (_ShutdownRequested && _CreationsInProgress == 0)
      _CreationFinished.SetResult();
    await tcs.Task;

    this.OnViewportCreation.InvokeThreadSafe();
    return viewport;
  }

  private struct WindowInitData
  {
    public string Title;
    public uint Width;
    public uint Height;
    public int X;
    public int Y;
    public float MinDepth;
    public float MaxDepth;
  }


  private static readonly ConcurrentDictionary<IntPtr, IViewport> _OpenViewports = new();
  private void _InitializeViewports()
  {
    while (_ToInit.TryDequeue(out var initRequest))
    {
      var coreViewport = new Win32Window(
        _WindowClass,
        initRequest.Data.Title,
        initRequest.Data.Width,
        initRequest.Data.Height,
        initRequest.Data.X,
        initRequest.Data.Y,
        initRequest.Data.MinDepth,
        initRequest.Data.MaxDepth
      );
      initRequest.Viewport.Initialize(coreViewport);
      _OpenViewports[initRequest.Viewport.Native.Win32.HWnd] = initRequest.Viewport;
      initRequest.TCS.SetResult();
    }
  }


  public ConcurrentEvent.Action OnShutdown { get; } = new();
  public async Task Shutdown()
  {
    _ShutdownRequested = true;
    await _CreationFinished.Task;
    foreach (var viewport in _OpenViewports.Values)
    {
      viewport.CloseForce();
    }
    _OpenViewports.Clear();
    OnShutdown.InvokeThreadSafe();
  }


  private readonly List<IntPtr> _ToClose = new();
  public void CloseAllRequest()
  {
    foreach (var viewport in _OpenViewports)
    {
      viewport.Value.CloseRequest();
      if (viewport.Value.IsClosed)
        _ToClose.Add(viewport.Key);
    }
    foreach (var key in _ToClose)
    {
      _OpenViewports.TryRemove(key, out _);
    }
    _ToClose.Clear();
  }


  public void Run(CancellationToken cancellationToken)
  {
    while (_OpenViewports.Count > 0 && !cancellationToken.IsCancellationRequested)
    {
      _InitializeViewports();
      _DispatchMessages();
    }
  }

  private void _DispatchMessages()
  {
    while (API.PeekMessage(out API.MSG msg, IntPtr.Zero, 0, 0, API.PM_REMOVE))
    {
      if (msg.message == API.WM_QUIT)
        return;

      API.TranslateMessage(ref msg);
      API.DispatchMessageW(ref msg);
    }
  }


  private API.WNDCLASSW _WindowClass = new()
  {
    style = API.CS_HREDRAW | API.CS_VREDRAW,
    lpfnWndProc = _WindowProcedure,
    lpszClassName = "Vindo.NormalWindow"
  };

  private readonly static API.WNDPROC _WindowProcedure = (IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam) =>
  {
    IViewport? viewport;
    // var paramLO = API.LOWord(lParam);
    // var paramHI = API.HIWord(lParam);

    switch (msg)
    {
      case API.WM_CLOSE:
        if (!_OpenViewports.TryGetValue(hWnd, out viewport))
          break;
        viewport.CloseRequest();
        return IntPtr.Zero;

      case API.WM_DESTROY:
        _OpenViewports.TryRemove(hWnd, out _);
        Console.WriteLine(_OpenViewports.Count);
        if (_OpenViewports.Count == 0)
          API.PostQuitMessage(0);
        return IntPtr.Zero;

      case API.WM_SIZE:
        if (_OpenViewports.TryGetValue(hWnd, out viewport))
        {
          int width = API.LOWord(lParam);
          int height = API.HIWord(lParam);
          viewport.SizeChanged((uint)width, (uint)height);
        }
        return IntPtr.Zero;

      case API.WM_MOVE:
        if (_OpenViewports.TryGetValue(hWnd, out viewport))
        {
          int x = API.LOWord(lParam);
          int y = API.HIWord(lParam);
          viewport.PositionChanged(x, y);
        }
        return IntPtr.Zero;

      case API.WM_PAINT:
        API.ValidateRect(hWnd, IntPtr.Zero);
        return IntPtr.Zero;

      case API.WM_SETCURSOR:
        switch (API.LOWord(lParam))
        {
          case API.HTCLIENT:
            API.SetCursor(API.LoadCursor(IntPtr.Zero, API.IDC_ARROW));
            return (IntPtr)1;
          default:
            break;
        }
        break;
    }
    return API.DefWindowProcW(hWnd, msg, wParam, lParam);
  };
}
