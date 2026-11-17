using System;
using PrimordialPower.Runtime;

namespace PrimordialPower.Platform;

public class ViewportServer : AppServer
{
  private readonly IViewportManager<Viewport> manager;
  private readonly AppServer server;
  public ViewportServer()
  {
    if (OperatingSystem.IsWindows())
    {
      Windows.Win32WindowManagerServer viewportServer = new();
      this.manager = viewportServer;
      this.server = viewportServer;
    }
    else if (OperatingSystem.IsLinux())
    {
      Linux.X11WindowManagerServer viewportServer = new();
      this.manager = viewportServer;
      this.server = viewportServer;
    }
    else throw new Exception("Viewports on this operating system not supported");
  }



  public IViewportManager<Viewport> Manager => this.manager;
  public override AppServerLifetime Lifetime => this.server.Lifetime;
  public override void Init() => this.server.Init();
  public override void Run() => this.server.Run();
  public override bool Loop() => this.server.Loop();
  public override void Shutdown() => this.server.Shutdown();
  public override void OnStop() => this.server.OnStop();
}
