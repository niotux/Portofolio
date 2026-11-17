using System.Collections;

namespace QuantiX.Engine;

public sealed class AppServerException : Exception
{
  public delegate HandlerResult Handler(AppServerException exception);
  public enum HandlerResult
  {
    StopServer,
    StopApplication,
    RestartServer,
    KillProcess,
  }



  public App? App { get; }
  public AppServer AppServer { get; }
  public AppServerState AppServerCrashState { get; }
  public DateTime TimeStamp { get; }



  public AppServerException(AppServer server, Exception innerException) :
    base(BuildMessage(server), innerException)
  {
    this.AppServer = server;
    this.App = server.App;
    this.AppServerCrashState = server.State;
    this.TimeStamp = DateTime.UtcNow;
  }

  private static string BuildMessage(AppServer server)
  {
    return $"Application server [\"{server.InstanceId}\"] with lifetime [{server.Lifetime}] threw an exception in state [{server.State}].";
  }
}
