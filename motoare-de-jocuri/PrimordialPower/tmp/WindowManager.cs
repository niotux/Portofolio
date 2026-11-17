namespace PrimordialPower.Core;

public class WindowManager : IViewportManager
{
  public uint ViewportCount => throw new NotImplementedException();

  public event Action? OnViewportCreation;
  public event Action? OnShutdown;

  public void CloseAllRequest()
  {
    throw new NotImplementedException();
  }

  public Task<IViewport> CreateViewportRequest(string title, uint width, uint height, int x, int y, float minDeapth = 0.1F, float maxDeapth = 1000)
  {
    throw new NotImplementedException();
  }

  public void Run(CancellationToken cancellationToken)
  {
    while (!cancellationToken.IsCancellationRequested)
    {
    }
  }

  public Task Shutdown()
  {
    throw new NotImplementedException();
  }

}
