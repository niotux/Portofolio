using PrimordialPower.Runtime;

namespace PrimordialPower.Platform;

public class ViewportServer : Server<IViewportManager<Viewport>>
{
  public override ServerType Type { get; }
  public override IViewportManager<Viewport> Services { get; }

  private Server server;
  public ViewportServer()
  {
    if (OperatingSystem.IsWindows())
    {
      var managerServer = new Win32.Win32WindowManagerServer();
      this.server = managerServer;
      this.Services = managerServer;
      this.Type = managerServer.Type;
    }
    else if (OperatingSystem.IsLinux() && GetSessionLinux(out var session))
    {
      if (IsX11(session))
      {
        var managerServer = new X11.X12WindowManagerServer();
        this.server = managerServer;
        this.Services = managerServer;
        this.Type = managerServer.Type;
      }
      else if (IsWayland(session))
      {
        throw new Exception("We don't have WAYLAND for you right now.");
      }
      else
      {
        throw new Exception("Geez, this is some real nice Linux distro. What is this, the terminal?... SO FRICKIN OBSCURE, THAT I COULDN'T FIND IT!!!");
      }
    }
    else
    {
      throw new Exception("My guess is that you are either on apple or on TempleOS. The second one provides you more hope in your life.");
    }
  }



  private static bool IsWin32()
  {
    return OperatingSystem.IsWindows();
  }

  private static bool GetSessionLinux(out string? session)
  {
    session = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
    return true;
  }

  private static bool IsWayland(string? session)
  {
    return session == "wayland";
  }

  private static bool IsX11(string? session)
  {
    return session == "x11";
  }




  public override void Init() => this.server.Init();
  public override void Start() => this.server.Start();
  public override bool Loop() => this.server.Loop();
  public override void SignalShutdown() => this.server.SignalShutdown();
  public override void Shutdown() => this.server.Shutdown();
}
