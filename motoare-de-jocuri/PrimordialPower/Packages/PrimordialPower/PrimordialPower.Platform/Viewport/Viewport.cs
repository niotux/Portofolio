using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace PrimordialPower.Platform;

public class Viewport : IViewport<Viewport>, IViewportInternals<Viewport>
{
  public Viewport(string title, uint width, uint height, int? x = null, int? y = null, int xOffset = 0, int yOffset = 0)
  {
    this.title = title;
    this.size = (width, height);
    this.position = (x ?? 0, y ?? 0);
    this.offset = (xOffset, yOffset);
    this.isFocused = false;
    this.isOpen = false;
    this.isVisible = false;
    this.handle = default;
    this.shouldDestroy = 0;
    this.isDefaultPosition = (x == null, y == null);
  }



  private volatile string title;
  public string Title
  {
    get => this.title;
    set
    {
      CheckAlive();
      this.manager.SetTitle(this, value);
    }
  }

  private (uint Width, uint Height) size;
  public (uint Width, uint Height) Size
  {
    get => this.size;
    set
    {
      CheckAlive();
      this.manager.SetSize(this, value.Width, value.Height);
    }
  }

  private (int X, int Y) position;
  public (int X, int Y) Position
  {
    get => this.position;
    set
    {
      CheckAlive();
      this.manager.SetPosition(this, value.X, value.Y);
    }
  }

  private (int X, int Y) offset;
  public (int X, int Y) Offset
  {
    get => this.offset;
    set
    {
      this.offset = value;
    }
  }



  internal IViewportInternals<Viewport> AsInternal => this;



  private ViewportHandle handle;
  public ViewportHandle Handle
  {
    get
    {
      CheckAlive();
      return this.handle;
    }
  }



  private IViewportManager<Viewport>? manager = null;
  public IViewportManager<Viewport>? Manager
  {
    get => this.manager;
    set
    {
      if (value is null)
      {
        this.manager?.Unbind(this);
      }
      else
      {
        value.Bind(this);
      }
    }
  }



  private volatile bool isOpen;
  public bool IsOpen => this.isOpen;

  private volatile bool isVisible;
  public bool IsVisible => this.isVisible;

  private volatile bool isFocused;
  public bool IsFocused => this.isFocused;

  private (bool X, bool Y) isDefaultPosition;
  public (bool X, bool Y) IsDefaultPosition => isDefaultPosition;


  public event Action<uint, uint>? OnSizeUpdate;
  public event Action<int, int>? OnPositionUpdate;
  public event Action<int, int>? OnOffsetUpdate;
  public event Action<bool>? OnVisibilityUpdate;
  public event Action<bool>? OnFocusUpdate;
  public event Action? OnClose;
  public event Func<bool>? OnConfirmClose;
  public event Action? OnDestroy;



  public static Viewport Create(string title, uint width, uint height, int? x, int? y, int xOffset = 0, int yOffset = 0)
  {
    return new Viewport(title, width, height, x, y, xOffset, yOffset);
  }



  public void Open()
  {
    CheckBound();
    this.manager.Open(this);
  }

  public Task OpenAsync()
  {
    CheckBound();
    return this.manager.OpenAsync(this);
  }

  public void Close()
  {
    if (!this.isOpen) return;

    var onClose = this.OnClose;
    onClose?.Invoke();

    var onConfirmClose = this.OnConfirmClose;
    bool confirm = onConfirmClose?.Invoke() ?? true;
    if (confirm) Interlocked.Exchange(ref this.shouldDestroy, 1);
  }

  public void Focus()
  {
    CheckAlive();
    this.manager.SetFocus(this, true);
  }

  public void SetSizePosition(uint width, uint height, int x, int y)
  {
    CheckAlive();
    this.manager.SetSizePosition(this, width, height, x, y);
  }

  public bool Update()
  {
    CheckAlive();
    return this.manager.Update(this);
  }



  [MemberNotNull(nameof(manager))]
  private void CheckAlive()
  {
    CheckBound();
    if (!this.IsOpen) throw new InvalidOperationException("Window is closed! Writes to it and reads of any native handle or identifier will fail");
  }

  [MemberNotNull(nameof(manager))]
  private void CheckBound()
  {
    if (this.manager is null) throw new InvalidOperationException("Viewport is not bound to a manager!");
  }



  [MemberNotNull(nameof(manager))]
  void IViewportInternals<Viewport>.CheckAlive()
  {
    this.CheckAlive();
  }

  [MemberNotNull(nameof(manager))]
  void IViewportInternals<Viewport>.CheckBoundTo(IViewportManager<Viewport> otherManager)
  {
    this.CheckBound();
    if (this.manager != otherManager) throw new InvalidOperationException("Manager does not own that viewport");
  }

  void IViewportInternals<Viewport>.ChangeInternalSize(uint width, uint height)
  {
    this.size = (width, height);
    var e = this.OnSizeUpdate;
    e?.Invoke(width, height);
  }

  void IViewportInternals<Viewport>.ChangeInternalPosition(int x, int y)
  {
    this.position = (x, y);
    var e = this.OnPositionUpdate;
    e?.Invoke(x, y);
  }

  void IViewportInternals<Viewport>.ChangeInternalOffset(int x, int y)
  {
    this.offset = (x, y);
    var e = this.OnOffsetUpdate;
    e?.Invoke(x, y);
  }

  void IViewportInternals<Viewport>.ChangeInternalFocus(bool isFocused)
  {
    this.isFocused = isFocused;
    var e = this.OnFocusUpdate;
    e?.Invoke(isFocused);
  }

  void IViewportInternals<Viewport>.ChangeInternalVisibility(bool isVisible)
  {
    this.isVisible = isVisible;
    var e = this.OnVisibilityUpdate;
    e?.Invoke(isVisible);
  }

  void IViewportInternals<Viewport>.ChangeInternalManger(IViewportManager<Viewport>? manager)
  {
    this.manager = manager;
  }

  private int shouldDestroy = 0;
  bool IViewportInternals<Viewport>.ConfirmDestroy()
  {
    int confirm = Interlocked.Exchange(ref this.shouldDestroy, 0);
    return confirm != 0;
  }

  void IViewportInternals<Viewport>.Validate(in ViewportHandle handle)
  {
    this.handle = handle;
    this.isOpen = true;
  }

  void IViewportInternals<Viewport>.Invalidate()
  {
    var e = OnDestroy;
    e?.Invoke();
    this.isOpen = false;
    this.handle = default;
  }

  //  Lifetime Scheme:
  //
  //  Close:
  //    [user loop]
  //    -> send close message
  //
  //    [event loop]
  //    -> manager event loop processes message
  //    -> confirm close
  //    -> switch aboutToDestroy flag
  //
  //    [user loop]
  //    -> viewports gets updated by user loop
  //    -> update checks for aboutToDestroy
  //    -> send destroy message
}
