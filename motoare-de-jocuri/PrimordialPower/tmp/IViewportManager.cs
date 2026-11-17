namespace PrimordialPower.Core;

public interface IViewportManager : IAsyncComponent
{
  Task<IViewport> CreateViewportRequest(string title, uint width, uint height, int x, int y, float minDeapth = 0.1f, float maxDeapth = 1000.0f);
  event Action? OnViewportCreation;


  void CloseAllRequest();


  Task Shutdown();
  event Action? OnShutdown;
}
