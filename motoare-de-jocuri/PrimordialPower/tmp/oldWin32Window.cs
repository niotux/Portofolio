using System;
using System.Runtime.InteropServices;

namespace PrimordialPower.Core.Win32;

internal sealed class oldWin32Window : IViewportCore
{
  private readonly IntPtr _HInstance;
  private readonly IntPtr _HWnd;
  public oldWin32Window(
      API.WNDCLASSW windowClass,
      string title,
      uint width,
      uint height,
      int x,
      int y,
      float minDeapth,
      float maxDeapth,
      IntPtr owner = default
  )
  {
    _HInstance = windowClass.hInstance;
    _HWnd = API.CreateWindowExW(
        dwExStyle: 0,
        lpClassName: windowClass.lpszClassName,
        lpWindowName: title,
        dwStyle: API.WS_OVERLAPPEDWINDOW,
        x: x,
        y: y,
        nWidth: (int)width,
        nHeight: (int)height,
        hWndParent: owner,
        hMenu: IntPtr.Zero,
        hInstance: _HInstance,
        lpParam: IntPtr.Zero
    );
    if (_HWnd == IntPtr.Zero)
    {
      int error = Marshal.GetLastWin32Error();
      throw new InvalidOperationException($"Failed to create window. Error: {error}");
    }

    _Width = width;
    _Height = height;
    _X = x;
    _Y = y;
    _Title = title;

    this.IsVisible = true;
    this.IsFocused = true;
  }


  public ViewportNative Native
  {
    get => new ViewportNative(
      platformOS: ViewportPlatformOS.Win32,
      win32: new Win32Data(_HInstance, _HWnd)
    );
  }



  private readonly object _LockSize = new();
  private volatile uint _Width;
  private volatile uint _Height;
  public (uint Width, uint Height) Size
  {
    get => (_Width, _Height);
    set
    {
      lock (_LockSize)
      {
        API.SetWindowPos(
          hWnd: _HWnd,
          hWndInsertAfter: IntPtr.Zero,
          x: 0,
          y: 0,
          cx: (int)value.Width,
          cy: (int)value.Height,
          uFlags: API.SWP_NOMOVE | API.SWP_NOZORDER | API.SWP_NOACTIVATE
        );
        SizeChanged(value.Width, value.Height);
      }
    }
  }
  public void SizeChanged(uint width, uint height)
  {
    _Width = width;
    _Height = height;
    this.OnSizeChanged.InvokeThreadSafe(width, height);
  }
  public ConcurrentEvent.Action<uint, uint> OnSizeChanged { get; } = new();



  private readonly object _LockTitle = new();
  private volatile string _Title;
  public string Title
  {
    get => _Title;
    set
    {
      lock (_LockTitle)
      {
        _Title = value;
        API.SetWindowText(_HWnd, value);
        this.OnTitleChanged.InvokeThreadSafe(value);
      }
    }
  }
  public ConcurrentEvent.Action<string> OnTitleChanged { get; } = new();



  private readonly object _LockPosition = new();
  private volatile int _X;
  private volatile int _Y;
  public (int X, int Y) Position
  {
    get => (_X, _Y);
    set
    {
      lock (_LockPosition)
      {
        API.SetWindowPos(
          hWnd: _HWnd,
          hWndInsertAfter: IntPtr.Zero,
          x: value.X,
          y: value.Y,
          cx: 0,
          cy: 0,
          uFlags: API.SWP_NOSIZE | API.SWP_NOZORDER | API.SWP_NOACTIVATE
        );
        PositionChanged(value.X, value.Y);
      }
    }
  }
  public void PositionChanged(int x, int y)
  {
    _X = x;
    _Y = y;
    this.OnPositionChanged.InvokeThreadSafe(x, y);
  }
  public ConcurrentEvent.Action<int, int> OnPositionChanged { get; }




  public bool IsVisible
  {
    get => API.IsWindowVisible(_HWnd);
    private set
    {
      API.ShowWindow(_HWnd, value ? API.SW_SHOW : API.SW_HIDE);
      API.UpdateWindow(_HWnd);
    }
  }

  public bool IsClosed { get; private set; } = false;

  public bool IsFocused
  {
    get => API.GetForegroundWindow() == _HWnd;
    set => API.SetForegroundWindow(_HWnd);
  }

  public float MinDepth { get; set; }
  public float MaxDepth { get; set; }


  public event Func<bool>? OnCloseIfShouldClose;
  public event Action<IViewport>? OnClose;
  public event Action<IViewport>? OnInit;
  public event Action<int, int>? OnResize;
  public event Action<int, int>? OnReoffset;

  public void CloseRequest()
  {
    if (this.OnCloseIfShouldClose?.Invoke() == false)
      return;
    CloseForce();
  }

  public void CloseForce()
  {
    if (this.IsClosed)
      return;
    this.IsClosed = true;
    API.DestroyWindow(_HWnd);
    this.OnClose?.Invoke(this);
  }

  public void Resize(int width, int height)
  {
    this.OnResize?.Invoke(width, height);
  }

  public void Reoffset(int x, int y)
  {
    this.OnReoffset?.Invoke(x, y);
  }
}

