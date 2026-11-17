using System.Runtime.Versioning;
using UnivKit.Application;

namespace UnivKit.OS.Windowing;

public sealed class WindowingService : IService, IWindowManager
{
  public SurfaceBackend Backend => SurfaceSystemBackend.Type;


  private readonly WindowingNativeServiceBase server;
  public WindowingService()
  {
    if (SurfaceSystemBackend.IsWin32())
    {
      this.server = new Win32.WindowingNativeService();
      return;
    }
    if (SurfaceSystemBackend.IsX11())
    {
      this.server = new X11.WindowingNativeService();
      return;
    }
    throw new PlatformNotSupportedException("Windows are not supported on this operating system.");
  }


  public event WindowEventHandler? OnEvent
  {
    add => this.server.OnEvent += value;
    remove => this.server.OnEvent -= value;
  }


  public int CreatedCount => this.server.CreatedCount;
  public int OpenCount => this.server.OpenCount;

  public void CloseAll() => this.server.CloseAll();
  public IWindow CreateWindow(string title, uint width, uint height, int? x = null, int? y = null)
  {
    return this.server.CreateWindow(title, width, height, x, y);
  }

  public Task Execute(CancellationToken cancellationToken)
  {
    return this.server.Execute(cancellationToken);
  }
}
