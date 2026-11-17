using System.ComponentModel;
using System.Runtime.Versioning;
using PrimordialPower.Runtime;

namespace PrimordialPower.Platform.Win32;

[SupportedOSPlatform("windows")]
internal sealed class Win32Window : IViewportInternal
{
  private readonly IntPtr hInstance;
  private readonly IntPtr hWnd;
  private readonly API.RECT decOffset;
  public Win32Window(
    API.WNDCLASSW windowClass,
    in ViewportParams parameters
  )
  {
    if ((parameters.Flags & ViewportFlags.WithShutdownEvents) == ViewportFlags.WithShutdownEvents)
    {
      this.OnDestroy += Launcher.Main.Shutdown;
    }

    this.decOffset = new()
    {
      Left = 0,
      Top = 0,
      Right = (int)parameters.Width,
      Bottom = (int)parameters.Height,
    };
    API.AdjustWindowRectEx(out this.decOffset, API.WS_OVERLAPPEDWINDOW, false, 0);

    this.hInstance = windowClass.HInstance;
    this.hWnd = API.CreateWindowExW(
        dwExStyle: 0,
        lpClassName: windowClass.LPSZClassName,
        lpWindowName: parameters.Title,
        dwStyle: API.WS_OVERLAPPEDWINDOW,
        x: parameters.X + this.decOffset.Left,
        y: parameters.Y + this.decOffset.Top,
        nWidth: this.decOffset.Right - this.decOffset.Left,
        nHeight: this.decOffset.Bottom - this.decOffset.Top,
        hWndParent: IntPtr.Zero,
        hMenu: IntPtr.Zero,
        hInstance: this.hInstance,
        lpParam: IntPtr.Zero
    );
    if (this.hWnd == IntPtr.Zero)
    {
      throw new Win32Exception("Failed to create Win32 window.");
    }

    int pref = API.DWMWCP_DONOTROUND;
    API.DwmSetWindowAttribute(this.hWnd, API.DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));

    this.decOffset.Right -= (int)parameters.Width;
    this.decOffset.Bottom -= (int)parameters.Height;

    this.width = parameters.Width;
    this.height = parameters.Height;
    this.x = parameters.X;
    this.y = parameters.Y;
    this.title = parameters.Title;

    API.ShowWindow(this.hWnd, API.SW_SHOW);
  }



  public ViewportNative Native => new(new Win32WindowInfo(this.hInstance, this.hWnd));
  public ViewportData Data => new()
  {
    Width = this.width,
    Height = this.height,
    XOffset = 0,
    YOffset = 0,
    MinDepth = this.minDepth,
    MaxDepth = this.maxDepth,
    Native = this.Native
  };



  private volatile uint isClosed = 0;
  public bool IsClosed => this.isClosed != 0;
  public void Close()
  {
    uint closed = Interlocked.Exchange(ref this.isClosed, 1);
    if (closed != 0) return;

    API.PostMessageW(this.hWnd, API.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
  }

  private volatile uint isDestroyed = 0;
  public void Destroy()
  {
    uint destroyed = Interlocked.Exchange(ref this.isDestroyed, 1);
    if (destroyed != 0) return;

    OnDestroy?.Invoke();

    API.PostMessageW(this.hWnd, API.WM_USER_WINDOW_DESTROY, IntPtr.Zero, IntPtr.Zero);
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



  private volatile bool isFocused = true;
  public bool IsActive => this.isFocused;
  public void UpdateInternalActivation(bool focus)
  {
    this.isFocused = focus;
  }



  private volatile uint updated = 0;
  public bool Updated => Interlocked.Exchange(ref this.updated, 0) != 0;
  public void Update()
  {
    this.updated = 1;
  }



  private readonly object lockSizeAndPosition = new();



  private volatile uint width;
  private volatile uint height;
  public (uint Width, uint Height) Size
  {
    get => (this.width, this.height);
    set
    {
      lock (this.lockSizeAndPosition)
      {
        API.SetWindowPos(
          hWnd: this.hWnd,
          hWndInsertAfter: IntPtr.Zero,
          x: 0,
          y: 0,
          cx: (int)value.Width + this.decOffset.Right - this.decOffset.Left,
          cy: (int)value.Height + this.decOffset.Bottom - this.decOffset.Top,
          uFlags: API.SWP_NOMOVE | API.SWP_NOZORDER | API.SWP_NOACTIVATE
        );
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
  public (int X, int Y) Position
  {
    get => (this.x, this.y);
    set
    {
      lock (this.lockSizeAndPosition)
      {
        API.SetWindowPos(
          hWnd: this.hWnd,
          hWndInsertAfter: IntPtr.Zero,
          x: value.X + this.decOffset.Left,
          y: value.Y + this.decOffset.Top,
          cx: 0,
          cy: 0,
          uFlags: API.SWP_NOSIZE | API.SWP_NOZORDER | API.SWP_NOACTIVATE
        );
      }
    }
  }
  public void UpdateInternalPosition(int x, int y)
  {
    this.x = x;
    this.y = y;
  }



  private volatile string title;
  private readonly object lockTitle = new();
  public string Title
  {
    get => this.title;
    set
    {
      lock (this.lockTitle)
      {
        API.SetWindowTextW(this.hWnd, value);
        this.title = value;
      }
    }
  }



  private volatile float minDepth;
  private volatile float maxDepth;
  private readonly object lockDepth = new();
  public (float Min, float Max) Depth
  {
    get => (minDepth, maxDepth);
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