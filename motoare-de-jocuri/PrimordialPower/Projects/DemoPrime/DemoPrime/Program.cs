using System.Text;
using PrimordialPower.Runtime;

namespace DemoPrime;

internal static class Program
{
  private static async Task Main()
  {
    try
    {
      Application app = new(AppGlobal.Servers.MainServer);
      app.AddServer(AppGlobal.Servers.AudioServer);
      app.AddServer(AppGlobal.Servers.ViewportServer);

      app.OnLifetimeError += (ex) => HandleError(ex, app);

      await app.Run();
    }
    catch (Exception ex)
    {
      HandleError(ex);
    }
  }

  private static void HandleError(Exception ex, Application? app = null, int exceptionLevel = 0)
  {
    const int tabSize = 4;
    Console.ForegroundColor = ConsoleColor.DarkRed;
    StringBuilder indent = new(exceptionLevel * tabSize);
    for (int i = 0; i < exceptionLevel * tabSize; i++)
    {
      if (i % 4 == 0)
      {
        indent.Append('|');
      }
      else
      {
        indent.Append('-');
      }
    }
    Console.Error.Write(indent.ToString());
    Console.Error.WriteLineAsync(ex.Message);
    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.Error.WriteLineAsync(ex.StackTrace);
    Console.ResetColor();

    if (ex.InnerException is not null)
    {
      HandleError(ex.InnerException, app, ++exceptionLevel);
    }

    app?.Shutdown();
  }
}
