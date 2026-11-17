using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace UnivKit.OS.Windowing;

internal sealed class Window : IWindow
{
  private readonly WindowingNativeServiceBase manager;
  public Window(WindowingNativeServiceBase manager, string title, uint width, uint height, int x, int y)
  {
    this.manager = manager;
    this.title = title;
    this.size = new() { Low = width, High = height };
    this.position = new() { Low = x, High = y };
    this.isFocused = false;
    this.isOpen = 0;
    this.isVisible = false;
    this.handle = null;
    this.shouldClose = 0;
  }



  private string title;
  public string Title
  {
    get => this.title;
    set
    {
      ThrowIfNotAlive();
      this.manager.SetTitleCore(this, value);
    }
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  internal void SetTitleInternal(string title)
  {
    Interlocked.Exchange(ref this.title, title);
  }



  private AtomicHelpers.UWord64 size;
  public (uint Width, uint Height) Size
  {
    get
    {
      var copy = this.size;
      return (copy.Low, copy.High);
    }
    set
    {
      ThrowIfNotAlive();
      this.manager.SetSizeCore(this, value.Width, value.Height);
    }
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  internal void SetSizeInternal(uint width, uint height)
  {
    AtomicHelpers.UWord64 newSize = new() { Low = width, High = height };
    Interlocked.Exchange(ref this.size.Word, newSize.Word);
    var e = this.OnSizeChanged;
    e?.Invoke(width, height);
  }



  private AtomicHelpers.Word64 position;
  public (int X, int Y) Position
  {
    get
    {
      var copy = this.position;
      return (copy.Low, copy.High);
    }
    set
    {
      ThrowIfNotAlive();
      this.manager.SetPositionCore(this, value.X, value.Y);
    }
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  internal void SetPositionInternal(int x, int y)
  {
    AtomicHelpers.Word64 newPos = new() { Low = x, High = y };
    Interlocked.Exchange(ref this.position.Word, newPos.Word);
    var e = this.OnPositionChanged;
    e?.Invoke(x, y);
  }



  private SurfaceHandle? handle;
  public SurfaceHandle? Handle => this.handle;

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  internal void ValidateInternal(in SurfaceHandle handle)
  {
    this.handle = handle;
    Interlocked.Exchange(ref this.isOpen, 1);
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  internal void InvalidateInternal()
  {
    var e = OnClose;
    e?.Invoke();
    Interlocked.Exchange(ref this.isOpen, 0);
    this.handle = null;
  }



  private volatile bool isVisible;
  public bool IsVisible => this.isVisible;

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  internal void SetVisibilityInternal(bool isVisible)
  {
    this.isVisible = isVisible;
    var e = this.OnVisibilityChanged;
    e?.Invoke(isVisible);
  }



  private volatile bool isFocused;
  public bool IsFocused => this.isFocused;
  public void Focus()
  {
    ThrowIfNotAlive();
    this.manager.SetFocusCore(this);
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  internal void SetFocusInternal(bool isFocused)
  {
    this.isFocused = isFocused;
    var e = this.OnFocusChanged;
    e?.Invoke(isFocused);
  }



  public event Action<uint, uint>? OnSizeChanged;
  public event Action<int, int>? OnPositionChanged;
  public event Action<bool>? OnVisibilityChanged;
  public event Action<bool>? OnFocusChanged;
  public event Action? OnCloseConfirmed;
  public event Action? OnClose;
  public event WindowEventHandler? OnEvent;



  private uint isOpen = 0;
  public bool IsOpen => this.isOpen != 0;

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public void Open()
  {
    if (this.isOpen != 0) return;
    this.manager.Open(this);
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public async Task OpenAsync()
  {
    if (this.isOpen != 0) return;
    await this.manager.OpenAsync(this);
  }



  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public void Close()
  {
    if (this.isOpen == 0) return;
    this.manager.Close(this);
  }

  private uint shouldClose = 0;
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public bool TryClose()
  {
    if (this.isOpen == 0) return true;
    this.manager.TryClose(this);
    return this.isOpen == 0;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public bool Update()
  {
    if (this.isOpen == 0)
    {
      return false;
    }
    if (this.shouldClose != 0)
    {
      Close();
      return false;
    }
    return true;
  }


  internal void HandleEvents(in WindowEventArgs args, ref WindowEventResponse response, bool isServerEvent)
  {
    var e = this.OnEvent;
    e?.Invoke(this, in args, ref response);

    if (args.EventType == WindowEventType.ShouldClose)
    {
      if (response.TryCancelEvent)
      {
        return;
      }

      if (isServerEvent)
      {
        Interlocked.Exchange(ref this.shouldClose, 1);
      }
      else
      {
        Close();
      }

      var e1 = this.OnCloseConfirmed;
      e1?.Invoke();
      this.manager.FireCloseConfirmed(this, isServerEvent);
    }
  }



  private int windowState;
  public WindowState WindowState => (WindowState)this.windowState;
  internal void SetStateInternal(WindowState state)
  {
    Interlocked.Exchange(ref this.windowState, (int)state);
  }

  public void Minimize()
  {
    ThrowIfNotAlive();
    this.manager.SetStateCore(this, WindowState.Minimized);
  }

  public void Maximize()
  {
    ThrowIfNotAlive();
    this.manager.SetStateCore(this, WindowState.Minimized);
  }

  public void FullScreen()
  {
    ThrowIfNotAlive();
    this.manager.SetStateCore(this, WindowState.FullScreen);
  }

  public void Restore()
  {
    ThrowIfNotAlive();
    this.manager.SetStateCore(this, WindowState.Normal);
  }



  internal void ThrowIfNotAlive()
  {
    if (!this.IsOpen) throw new InvalidOperationException("Window is closed! Writes to it will fail miserably.");
  }
}
