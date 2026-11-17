using System.Runtime;
using QuantiX.Engine;

namespace QuantiX.Engine.Tests.Portal;

internal static class Program
{
  public static async Task Main()
  {
    GCSettings.LatencyMode = GCLatencyMode.Interactive;

    PortalServer server = new();

    App app = new(server);
    app.AddService(server.Windowing);
    app.OnServerException += HandleException;

    Console.ForegroundColor = ConsoleColor.White;
    await app.Run();
  }

  public static AppServerException.HandlerResult HandleException(Exception? ex)
  {
    for (var e = ex; e is not null; e = e.InnerException)
    {
      Console.WriteLine(e.Message);
      Console.WriteLine(e.StackTrace);
      Console.WriteLine("---");
    }
    return AppServerException.HandlerResult.KillProcess;
  }
}
