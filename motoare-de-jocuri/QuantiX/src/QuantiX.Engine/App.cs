using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace QuantiX.Engine;

public sealed class App
{
  public App(IAppService rootService) : this(rootService.Server) { }
  public App(AppServer root)
  {
    this.root = root;
    this.root.App = this;
    this.root.OnInitDone += AutoStartCallback;
  }

  public static App Create(AppServer root) => new(root);
  public static App Create(IAppService rootService) => new(rootService);



  public event AppServerException.Handler? OnServerException;



  private AppServer root;
  public AppServer Root => this.root;



  private List<AppServer> servers = new(4);
  public void AddServer(AppServer server)
  {
    this.servers.Add(server);
    server.App = this;
  }

  public void AddService(IAppService service)
  {
    this.AddServer(service.Server);
  }



  private Dictionary<Type, IAppService> singletonServices = new();
  public TService AddSingleton<TService>()
    where TService : IAppService, new()
  {
    TService service = new();
    this.singletonServices[typeof(TService)] = service;
    this.AddServer(service.Server);
    return service;
  }

  public TService GetSingleton<TService>()
    where TService : IAppService
  {
    return (TService)this.singletonServices[typeof(TService)];
  }

  public bool TryGetSingleton<TService>([NotNullWhen(true)] out TService? instance)
    where TService : IAppService
  {
    bool result = this.singletonServices.TryGetValue(typeof(TService), out IAppService? service);
    instance = (TService?)service;
    return result;
  }



  public async Task Run()
  {
    AppServer? main = null;

    Task[] serverLifetimes = new Task[this.servers.Count];
    CountdownTask allInitDone = new(this.servers.Count);

    Task rootLifetime = Task.CompletedTask;

    this.root.OnException += this.OnServerException;

    for (int i = 0; i < this.servers.Count; i++)
    {
      AppServer server = this.servers[i];

      server.OnInitDone += (_) => allInitDone.Signal();
      server.OnException += this.OnServerException;

      switch (server.Lifetime)
      {
        case AppServerLifetime.Async:
          serverLifetimes[i] = Task.Run(server.RunAsyncLifetime);
          break;

        case AppServerLifetime.MonoThread:
          serverLifetimes[i] = Task.Run(server.RunMonoLifetime);
          break;

        case AppServerLifetime.MainThread:
          if (main is null) main = server;
          serverLifetimes[i] = Task.CompletedTask;
          break;

        default:
          serverLifetimes[i] = Task.CompletedTask;
          break;
      }
    }

    switch (this.root.Lifetime)
    {
      case AppServerLifetime.Async:
        rootLifetime = Task.Run(
          async () =>
          {
            await allInitDone.WaitAsync();
            await this.root.RunAsyncLifetime();
          }
        );
        break;

      case AppServerLifetime.MonoThread:
        rootLifetime = Task.Run(
          async () =>
          {
            await allInitDone.WaitAsync();
            this.root.RunMonoLifetime();
          }
        );
        break;

      case AppServerLifetime.MainThread:
        main = this.root;
        break;
    }


    main?.RunMonoLifetime();

    await rootLifetime;
    this.Stop();
    await Task.WhenAll(serverLifetimes);

    this.Shutdown();
  }

  private void Shutdown()
  {
    this.root.OnException -= this.OnServerException;
    this.root.OnInitDone -= AutoStartCallback;

    foreach (var server in this.servers)
    {
      server.OnException -= this.OnServerException;
      server.OnInitDone -= AutoStartCallback;
    }
  }



  public void Stop()
  {
    if (root.State != AppServerState.Stopped)
    {
      this.root.Stop();
    }
    foreach (var server in this.servers)
    {
      if (server.State != AppServerState.Stopped)
      {
        server.Stop();
      }
    }
  }



  private static void AutoStartCallback(AppServer server)
  {
    server.Start();
  }



  private class CountdownTask
  {
    private volatile int target;
    private volatile int count;
    private TaskCompletionSource tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public CountdownTask(int target)
    {
      this.target = target;
      this.count = target;

      if (target == 0)
      {
        this.tcs.SetResult();
      }
    }



    public void Signal()
    {
      var cnt = Interlocked.Decrement(ref count);
      if (cnt == 0) tcs.SetResult();
    }

    public void Reset()
    {
      this.count = this.target;
    }

    public void Reset(int newTarget)
    {
      this.target = newTarget;
      Reset();
    }

    public Task WaitAsync()
    {
      return this.tcs.Task;
    }

    public void Wait()
    {
      this.tcs.Task.GetAwaiter().GetResult();
    }
  }
}
