namespace PrimordialPower.Platform;

public interface IViewportManager<TViewport>
where TViewport : IViewport
{
  void OpenAll();
  void CloseAll();

  void Bind(TViewport viewport);
  void Unbind(TViewport viewport);
  void Open(TViewport viewport);
  Task OpenAsync(TViewport viewport);
  void Close(TViewport viewport);
  void Destroy(TViewport viewport);
  bool Update(TViewport viewport);
  void SetTitle(TViewport viewport, string text);
  void SetSize(TViewport viewport, uint width, uint height);
  void SetPosition(TViewport viewport, int x, int y);
  void SetSizePosition(TViewport viewport, uint width, uint height, int x, int y);
  void SetFocus(TViewport viewport, bool isFocused);

  int BoundViewportCount { get; }
  int OpenViewportCount { get; }

  event Action? OnAllViewportsClosed;
  event Action<ViewportEvent>? OnPollEvents;
}