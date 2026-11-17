using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

namespace QuantiX.Engine.Benchmarks;

public class AppServerBenchmarks
{
  private AppServerImpl _server1 = null!;
  private AppServer2Impl _server2 = null!;

  [GlobalSetup]
  public void Setup()
  {
    _server1 = new AppServerImpl();
    _server2 = new AppServer2Impl();
  }

  [Benchmark]
  public void AppServer_Init()
  {
    _server1.Init();
  }

  [Benchmark]
  public void AppServer_Run()
  {
    _server1.Run();
  }

  [Benchmark]
  public void AppServer_Loop()
  {
    _server1.Loop();
  }

  [Benchmark]
  public void AppServer_Shutdown()
  {
    _server1.Shutdown();
  }

  [Benchmark]
  public void AppServer_Stop()
  {
    _server1.Stop();
  }

  [Benchmark]
  public Task AppServer_InitAsync()
  {
    return _server1.InitAsync();
  }

  [Benchmark]
  public Task AppServer_RunAsync()
  {
    return _server1.RunAsync();
  }

  [Benchmark]
  public Task AppServer_LoopAsync()
  {
    return _server1.LoopAsync();
  }

  [Benchmark]
  public Task AppServer_ShutdownAsync()
  {
    return _server1.ShutdownAsync();
  }

  [Benchmark]
  public void AppServer2_Init()
  {
    _server2.Init();
  }

  [Benchmark]
  public void AppServer2_Run()
  {
    _server2.Run();
  }

  [Benchmark]
  public void AppServer2_Loop()
  {
    _server2.Loop();
  }

  [Benchmark]
  public void AppServer2_Shutdown()
  {
    _server2.Shutdown();
  }

  [Benchmark]
  public void AppServer2_Stop()
  {
    _server2.Stop();
  }

  [Benchmark]
  public Task AppServer2_InitAsync()
  {
    return _server2.InitAsync();
  }

  [Benchmark]
  public Task AppServer2_RunAsync()
  {
    return _server2.RunAsync();
  }

  [Benchmark]
  public Task AppServer2_LoopAsync()
  {
    return _server2.LoopAsync();
  }

  [Benchmark]
  public Task AppServer2_ShutdownAsync()
  {
    return _server2.ShutdownAsync();
  }
}

public class AppServerImpl : AppServer
{
  private int _counter = 0;

  public override void Init()
  {
    base.Init();
    _counter = 0;
    for (int i = 0; i < 100; i++)
    {
      _counter += i;
    }
  }

  public override void Run()
  {
    base.Run();
    _counter *= 2;
  }

  public override void Loop()
  {
    base.Loop();
    _counter++;
  }

  public override void Shutdown()
  {
    base.Shutdown();
    _counter = 0;
  }

  public override void Stop()
  {
    base.Stop();
    _counter = -1;
  }
}

public class AppServer2Impl : AppServer2
{
  private int _counter = 0;

  protected override void OnInit()
  {
    base.OnInit();
    _counter = 0;
    for (int i = 0; i < 100; i++)
    {
      _counter += i;
    }
  }

  protected override void OnRun()
  {
    base.OnRun();
    _counter *= 2;
  }

  protected override void OnLoop()
  {
    base.OnLoop();
    _counter++;
  }

  protected override void OnShutdown()
  {
    base.OnShutdown();
    _counter = 0;
  }

  protected override void OnStop()
  {
    base.OnStop();
    _counter = -1;
  }
}

public class Program
{
  public static void Main(string[] args)
  {
    var summary = BenchmarkRunner.Run<AppServerBenchmarks>();
  }
}