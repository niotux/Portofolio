using System;

namespace PrimordialPower.Core;

public class Window : IViewport
{
  private IViewport? _NativeWindow;

  public event Action<string> OnTitleChanged;
  public event Action<uint, uint> OnSizeChanged;
  public event Action<int, int> OnPositionChanged;
  public event Action<int, int> OnOffsetChanged;
  public event Action<float, float> OnDepthChanged;
  public event Func<bool> OnCloseRequested;
  public event Action<bool> OnVisibilityChanged;

  public Window(IViewport nativeWindow)
  {
    _NativeWindow = nativeWindow;
  }
  public Window()
  {
    _NativeWindow = null;
  }


  public ViewportNativeData NativeData
  {
    get
    {
      if (_NativeWindow == null)
        return new ViewportNativeData(
            platformOS: ViewportPlatformOS.Unavailable,
            width: 0,
            height: 0,
            x: 0,
            y: 0,
            minDepth: 0,
            maxDepth: 0
        );
      return _NativeWindow.NativeData;
    }
  }

  public string Title
  {
    get
    {
      if (_NativeWindow == null)
        return string.Empty;
      return _NativeWindow.Title;
    }
    set
    {
      if (_NativeWindow == null)
        return;
      _NativeWindow.Title = value;
    }
  }

  public bool IsVisible
  {
    get
    {
      if (_NativeWindow == null)
        return false;
      return _NativeWindow.IsVisible;
    }
  }

  public bool IsClosed
  {
    get
    {
      if (_NativeWindow == null)
        return true;
      return _NativeWindow.IsClosed;
    }
  }

  public bool IsFocused
  {
    get
    {
      if (_NativeWindow == null)
        return false;
      return _NativeWindow.IsFocused;
    }
    set
    {
      if (_NativeWindow == null)
        return;
      _NativeWindow.IsFocused = value;
    }
  }

  public (int Width, int Height) Size
  {
    get
    {
      if (_NativeWindow == null)
        return (0, 0);
      return _NativeWindow.Size;
    }
    set
    {
      if (_NativeWindow == null)
        return;
      _NativeWindow.Size = value;
    }
  }

  public (int X, int Y) Position
  {
    get
    {
      if (_NativeWindow == null)
        return (0, 0);
      return _NativeWindow.Position;
    }
    set
    {
      if (_NativeWindow == null)
        return;
      _NativeWindow.Position = value;
    }
  }

  public float MinDepth
  {
    get
    {
      if (_NativeWindow == null)
        return 0;
      return _NativeWindow.MinDepth;
    }
    set
    {
      if (_NativeWindow == null)
        return;
      _NativeWindow.MinDepth = value;
    }
  }

  public float MaxDepth
  {
    get
    {
      if (_NativeWindow == null)
        return 0;
      return _NativeWindow.MaxDepth;
    }
    set
    {
      if (_NativeWindow == null)
        return;
      _NativeWindow.MaxDepth = value;
    }
  }

  (uint Width, uint Height) IViewport.Size { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
  public (int X, int Y) Offset { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
  public (float Min, float Max) Depth { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

  public bool IsInitialized => throw new NotImplementedException();

  public bool ShouldClose => throw new NotImplementedException();

  public event Func<bool>? OnCloseIfShouldClose
  {
    add
    {
      if (_NativeWindow == null)
        return;
      _NativeWindow.OnCloseIfShouldClose += value;
    }
    remove
    {
      if (_NativeWindow == null)
        return;
      _NativeWindow.OnCloseIfShouldClose -= value;
    }
  }

  public event Action<IViewport>? OnClose
  {
    add
    {
      if (_NativeWindow == null)
        return;
      _NativeWindow.OnClose += value;
    }
    remove
    {
      if (_NativeWindow == null)
        return;
      _NativeWindow.OnClose -= value;
    }
  }

  public event Action<IViewport>? OnInit
  {
    add
    {
      if (_NativeWindow == null)
        return;
      _NativeWindow.OnInit += value;
    }
    remove
    {
      if (_NativeWindow == null)
        return;
      _NativeWindow.OnInit -= value;
    }
  }

  public event Action<int, int>? OnResize
  {
    add
    {
      if (_NativeWindow == null)
        return;
      _NativeWindow.OnResize += value;
    }
    remove
    {
      if (_NativeWindow == null)
        return;
      _NativeWindow.OnResize -= value;
    }
  }

  public event Action<int, int>? OnReoffset
  {
    add
    {
      if (_NativeWindow == null)
        return;
      _NativeWindow.OnReoffset += value;
    }
    remove
    {
      if (_NativeWindow == null)
        return;
      _NativeWindow.OnReoffset -= value;
    }
  }

  event Action? IViewport.OnInit
  {
    add
    {
      throw new NotImplementedException();
    }

    remove
    {
      throw new NotImplementedException();
    }
  }

  event Action IViewport.OnClose
  {
    add
    {
      throw new NotImplementedException();
    }

    remove
    {
      throw new NotImplementedException();
    }
  }

  public void CloseForce()
  {
    _NativeWindow?.CloseForce();
    _NativeWindow = null;
  }

  public void CloseRequest()
  {
    if (_NativeWindow == null)
      return;
    _NativeWindow.CloseRequest();
    if (_NativeWindow.IsClosed)
      _NativeWindow = null;
  }

  public void Reoffset(int x, int y) => _NativeWindow?.Reoffset(x, y);

  public void Resize(int width, int height) => _NativeWindow?.Resize(width, height);

  public void Initialize(IViewportCore core)
  {
    throw new NotImplementedException();
  }

  public void Show()
  {
    throw new NotImplementedException();
  }

  public void Hide()
  {
    throw new NotImplementedException();
  }
}
