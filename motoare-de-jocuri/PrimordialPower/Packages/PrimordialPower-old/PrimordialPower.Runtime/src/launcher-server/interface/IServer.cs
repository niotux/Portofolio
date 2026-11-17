namespace PrimordialPower.Runtime;

public interface IServer
{
  ServerType Type { get; }



  Task InitAsync();
  Task StartAsync();
  Task<bool> LoopAsync();
  Task ShutdownAsync();



  void Init();
  void Start();
  bool Loop();
  void Shutdown();



  void SignalShutdown();
}



public interface IServer<TService> : IServer
{
  TService Services { get; }
}