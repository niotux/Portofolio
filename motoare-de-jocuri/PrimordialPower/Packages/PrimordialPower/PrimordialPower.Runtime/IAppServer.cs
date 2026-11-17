namespace PrimordialPower.Runtime;

public interface IAppServer
{
  AppServerLifetime Lifetime { get; }
  Application? Application { get; set; }
  bool StopRequested { get; }



  Task InitAsync();
  Task RunAsync();
  Task<bool> LoopAsync();
  Task ShutdownAsync();



  void Init();
  void Run();
  bool Loop();
  void Shutdown();



  void Stop();
}