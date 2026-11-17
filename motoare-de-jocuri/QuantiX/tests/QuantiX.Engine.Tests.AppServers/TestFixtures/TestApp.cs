using QuantiX.Engine;

namespace QuantiX.Engine.Tests.AppServers.TestFixtures;

/// <summary>
/// A test helper for the App class.
/// App is a utility class that should not be inherited.
/// This class provides helper methods for testing App functionality.
/// </summary>
public static class TestApp
{
  public static App CreateWithServers(AppServer root, params AppServer[] servers)
  {
    var app = new App(root);
    foreach (var server in servers)
    {
      app.AddServer(server);
    }
    return app;
  }

  public static App CreateWithSingleton<TServer>(AppServer root)
      where TServer : AppServer, new()
  {
    var app = new App(root);
    app.AddSingleton<TServer>();
    return app;
  }
}