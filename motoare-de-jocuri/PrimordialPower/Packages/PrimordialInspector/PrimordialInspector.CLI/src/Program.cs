using System.Threading.Tasks;
using PrimordialInspector.Scripting.Servers;
using PrimordialPower.Core;


namespace PrimordialInspector.CLI;

public class Game : TaskServer
{
  public WindowServer WindowServer => this.windowServer;

  private readonly WindowServer windowServer;
  private readonly ConsoleInspectorServer inspectorServer = new();
  public Game()
  {
    this.windowServer =
      Launcher.AddThreadServer<WindowServer>(nameof(this.windowServer));
    this.inspectorServer =
      Launcher.AddTaskServer<ConsoleInspectorServer>(nameof(this.inspectorServer));
  }



  private Task<bool> loop = Launcher.ContinueLoopTask;
  public override Task<bool> Loop()
  {
    return this.loop;
  }

  public override Task Start()
  {
    this.inspectorServer.Services.SetFocus(this);
    return Task.CompletedTask;
  }

  public override void SignalShutdown()
  {
    this.loop = Launcher.StopLoopTask;
  }
}


internal static class Program
{
  public static async Task Main()
  {
    Launcher.AddTaskServer<Game>("Game");
    await Launcher.Run();
  }
}
