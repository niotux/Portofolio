namespace QuantiX.Engine.Windowing;

public interface IWindowManager<TWindow>
where TWindow : IWindow
{
  void OpenAll();
  void CloseAll();

  TWindow CreateWindow(string title, uint width, uint height, int? x = null, int? y = null);
  void Bind(TWindow window);
  void Unbind(TWindow window);
  void Open(TWindow window);
  Task OpenAsync(TWindow window);
  void Close(TWindow window);
  void Destroy(TWindow window);
  bool Update(TWindow window);
  void SetTitle(TWindow window, string text);
  void SetSize(TWindow window, uint width, uint height);
  void SetPosition(TWindow window, int x, int y);
  void SetSizePosition(TWindow window, uint width, uint height, int x, int y);
  void SetFocus(TWindow window, bool isFocused);

  int BoundCount { get; }
  int OpenCount { get; }

  event Action? OnAllClosed;
  event Action<WindowEvent>? OnPollEvents;
}