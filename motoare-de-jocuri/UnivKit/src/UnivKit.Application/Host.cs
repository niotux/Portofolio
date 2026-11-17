using System;
using System.Linq.Expressions;
using System.Net.Http.Headers;

namespace UnivKit.Application;

public class Host : IHost
{
  private readonly Dictionary<Type, IService> singletons = new();
  public TService AddSingleton<TService>()
    where TService : class, IService, new()
  {
    TService service = new();
    this.singletons[typeof(TService)] = service;
    return service;
  }

  private readonly List<IService> instances = new();
  public void Add(IService service)
  {
    this.instances.Add(service);
  }

  public TService GetSingleton<TService>()
    where TService : class, IService, new()
  {
    return this.singletons[typeof(TService)] as TService
      ?? throw new ArgumentException(nameof(TService));
  }

  public int HostedCount => instances.Count + singletons.Count;


  private readonly CancellationTokenSource tcs = new();
  public async void StartAll()
  {
    Task[] tasks = new Task[this.HostedCount];

    int i = 0;
    foreach (var server in this.instances)
    {
      tasks[i] = Task.Run(() => server.Execute(this.tcs.Token));
      i++;
    }
    foreach (var serverKVP in this.singletons)
    {
      tasks[i] = Task.Run(() => serverKVP.Value.Execute(this.tcs.Token));
      i++;
    }

    this.completion = Task.WhenAll(tasks);
  }

  public Task StopAll()
  {
    if (this.completion is null)
    {
      return Task.CompletedTask;
    }

    this.tcs.Cancel();

    return this.completion;
  }

  private Task completion = Task.CompletedTask;
  public async Task WaitForStop()
  {
    await this.completion;
  }
}
