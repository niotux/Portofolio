using QuantiX.Engine;
using QuantiX.Engine.Windowing;

namespace QuantiX.Engine.Tests.FPS;

internal static class Program
{
  public static async Task Main()
  {
    GameServer game = new();

    App app = new(game);
    app.AddService(game.Windowing);
    app.AddService(game.Audio);
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
