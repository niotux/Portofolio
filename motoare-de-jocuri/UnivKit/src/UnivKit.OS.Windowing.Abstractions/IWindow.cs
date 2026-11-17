namespace UnivKit.OS.Windowing;

public interface IWindow : ISurface
{
  new (uint Width, uint Height) Size { get; set; }
  (int X, int Y) Position { get; set; }
  string Title { get; set; }

  bool IsOpen { get; }
  bool IsFocused { get; }

  event Action<bool>? OnFocusChanged;
  event Action<int, int>? OnPositionChanged;
  event Action? OnCloseConfirmed;
  event Action? OnClose;
  event WindowEventHandler? OnEvent;

  WindowState WindowState { get; }

  void Minimize();
  void Maximize();
  void FullScreen();
  void Restore();

  void Focus();
  void Open();
  Task OpenAsync();
  bool TryClose();
  void Close();
  bool Update();
}