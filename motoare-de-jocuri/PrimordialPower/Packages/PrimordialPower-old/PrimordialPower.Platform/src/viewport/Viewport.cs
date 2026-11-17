namespace PrimordialPower.Platform;

public class Viewport : IViewport
{
  public Viewport() { }


  internal IViewportInternal? Core { get; private set; } = null;
  internal bool Initialized { get; private set; } = false;
  internal void Initialize(IViewportInternal core)
  {
    this.Initialized = true;
    this.Core = core;
  }



  public ViewportNative Native => this.Core?.Native ?? new ViewportNative();
  public ViewportData Data => this.Core?.Data ?? new ViewportData();



  public bool IsClosed => this.Core is null;

  public event Func<bool>? ConfirmClose
  {
    add
    {
      if (this.Core is null) return;
      this.Core.ConfirmClose += value;
    }
    remove
    {
      if (this.Core is null) return;
      this.Core.ConfirmClose -= value;
    }
  }

  public event Action? OnClose
  {
    add
    {
      if (this.Core is null) return;
      this.Core.OnClose += value;
    }
    remove
    {
      if (this.Core is null) return;
      this.Core.OnClose -= value;
    }
  }

  public event Action? OnDestroy
  {
    add
    {
      if (this.Core is null) return;
      this.Core.OnDestroy += value;
    }
    remove
    {
      if (this.Core is null) return;
      this.Core.OnDestroy -= value;
    }
  }

  public void Close() => this.Core?.Close();

  public void Destroy()
  {
    this.Core?.Destroy();
    this.Core = null;
  }



  public bool IsActive => this.Core?.IsActive ?? false;
  public bool Updated => this.Core?.Updated ?? false;



  public (uint Width, uint Height) Size
  {
    get => this.Core?.Size ?? (0, 0);
    set
    {
      if (this.Core is null) return;
      this.Core.Size = value;
    }
  }



  public (int X, int Y) Position
  {
    get => this.Core?.Position ?? (0, 0);
    set
    {
      if (this.Core is null) return;
      this.Core.Position = value;
    }
  }



  public string Title
  {
    get => this.Core?.Title ?? string.Empty;
    set
    {
      if (this.Core is null) return;
      this.Core.Title = value;
    }
  }



  public (float Min, float Max) Depth
  {
    get => this.Core?.Depth ?? (0, 0);
    set
    {
      if (this.Core is null) return;
      this.Core.Depth = value;
    }
  }
}
