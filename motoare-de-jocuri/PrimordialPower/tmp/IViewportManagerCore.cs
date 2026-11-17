namespace PrimordialPower.Core;

public interface IViewportManagerCore : IAsyncComponent
{
  Task<IViewport> CreateViewportRequest(string title, uint width, uint height, int x, int y, float minDeapth = 0.1f, float maxDeapth = 1000.0f);
  ConcurrentEvent.Action OnViewportCreation { get; }


  void CloseAllRequest();


  Task Shutdown();
  ConcurrentEvent.Action OnShutdown { get; }
}
