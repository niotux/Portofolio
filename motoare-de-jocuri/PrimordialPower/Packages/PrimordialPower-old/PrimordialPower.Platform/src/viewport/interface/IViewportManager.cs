namespace PrimordialPower.Platform;

public interface IViewportManager<TViewport>
{
  Task<TViewport> CreateViewport(
    string title,
    uint width,
    uint height,
    float minDepth,
    float maxDepth,
    int x = ViewportParams.DefaultX,
    int y = ViewportParams.DefaultY
  );
  Task<TViewport> CreateViewport(
    ViewportFlags flags,
    string title,
    uint width,
    uint height,
    float minDepth,
    float maxDepth,
    int x = ViewportParams.DefaultX,
    int y = ViewportParams.DefaultY
  );
  Task<TViewport> CreateViewport(ViewportParams info);



  Task InitializeViewport(
    TViewport viewport,
    string title,
    uint width,
    uint height,
    float minDepth,
    float maxDepth,
    int x = ViewportParams.DefaultX,
    int y = ViewportParams.DefaultY
  );
  Task InitializeViewport(
    TViewport viewport,
    ViewportFlags flags,
    string title,
    uint width,
    uint height,
    float minDepth,
    float maxDepth,
    int x = ViewportParams.DefaultX,
    int y = ViewportParams.DefaultY
  );
  Task InitializeViewport(TViewport viewport, ViewportParams info);



  void CloseAll();
  void Close(TViewport viewport);



  uint ViewportCount { get; }
  event Func<Task>? OnZeroViewports;
}
