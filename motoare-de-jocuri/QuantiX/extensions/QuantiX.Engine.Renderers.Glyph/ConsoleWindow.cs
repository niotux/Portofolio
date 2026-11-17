using QuantiX.Engine.Windowing;

namespace QuantiX.Engine.Renderers.Glyph;

/// <summary>
/// IWindow implementation that wraps the system Console as a rendering surface.
/// Size is measured in character cells (columns x rows).
/// </summary>
public sealed class ConsoleWindow : IWindow
{
  private uint _width;
  private uint _height;
  private string _title;
  private bool _isOpen;
  private bool _isVisible;
  private bool _isFocused;

  public ConsoleWindow(string title, uint width, uint height)
  {
    _title = title;
    _width = width;
    _height = height;
  }

  public string Title
  {
    get => _title;
    set
    {
      _title = value;
      if (_isOpen)
        Console.Title = value;
    }
  }

  public WindowHandle Handle => default; // no native handle for console

  public bool IsFocused => _isFocused;
  public (bool X, bool Y) IsDefaultPosition => (true, true);

  public (uint Width, uint Height) Size
  {
    get => (_width, _height);
    set
    {
      _width = value.Width;
      _height = value.Height;
      OnSizeUpdate?.Invoke(value.Width, value.Height);
    }
  }

  public (int X, int Y) Position
  {
    get => (0, 0);
    set => OnPositionUpdate?.Invoke(value.X, value.Y);
  }

  public bool IsOpen => _isOpen;
  public bool IsVisible => _isVisible;

  public event Action<uint, uint>? OnSizeUpdate;
  public event Action<int, int>? OnPositionUpdate;
  public event Action<bool>? OnVisibilityUpdate;
  public event Action? OnClose;
  public event Action? OnDestroy;
  public event Action<bool>? OnFocusUpdate;
  public event Func<bool>? OnConfirmClose;

  public void SetSizePosition(uint width, uint height, int x, int y)
  {
    _width = width;
    _height = height;
    OnSizeUpdate?.Invoke(width, height);
  }

  public void Open()
  {
    _isOpen = true;
    _isVisible = true;
    _isFocused = true;
    Console.Title = _title;
    Console.CursorVisible = false;
    Console.Clear();
    OnVisibilityUpdate?.Invoke(true);
    OnFocusUpdate?.Invoke(true);
  }

  public Task OpenAsync()
  {
    Open();
    return Task.CompletedTask;
  }

  public void Close()
  {
    _isOpen = false;
    _isVisible = false;
    Console.CursorVisible = true;
    OnClose?.Invoke();
  }

  public void Focus() => _isFocused = true;

  public bool Update()
  {
    if (!_isOpen) return false;

    // Detect console resize
    uint newWidth = (uint)Console.WindowWidth;
    uint newHeight = (uint)Console.WindowHeight;
    if (newWidth != _width || newHeight != _height)
    {
      _width = newWidth;
      _height = newHeight;
      OnSizeUpdate?.Invoke(newWidth, newHeight);
    }

    return true;
  }
}
