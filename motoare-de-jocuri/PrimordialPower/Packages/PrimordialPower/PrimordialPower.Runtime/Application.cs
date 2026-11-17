using System;
using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

namespace PrimordialPower.Runtime;

public class Application
{
  public Application(IAppServer root)
  {
    this.root = root;
    this.root.Application = this;
  }

  public Application Create(IAppServer root)
  {
    return new(root);
  }



  public event Action<Exception>? OnLifetimeError;



  private IAppServer root;
  public IAppServer Root => this.root;



  private List<IAppServer> servers = new(4);
  public void AddServer(IAppServer server)
  {
    this.servers.Add(server);
    server.Application = this;
  }



  public async Task Run()
  {
    IAppServer? main = null;

    Task[] serverLifetimes = new Task[this.servers.Count];
    CowntdownTask allInitDone = new(this.servers.Count);

    Task rootLifetime = Task.CompletedTask;

    for (int i = 0; i < this.servers.Count; i++)
    {
      IAppServer server = this.servers[i];

      switch (server.Lifetime)
      {
        case AppServerLifetime.Async:
          serverLifetimes[i] = Task.Run(() => ServerLifetime.Async(server, allInitDone, this.OnLifetimeError));
          break;

        case AppServerLifetime.MonoThread:
          serverLifetimes[i] = Task.Run(() => ServerLifetime.MonoThread(server, allInitDone, this.OnLifetimeError));
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
            await ServerLifetime.Async(this.root, null, this.OnLifetimeError);
          }
        );
        break;

      case AppServerLifetime.MonoThread:
        rootLifetime = Task.Run(
          async () =>
          {
            await allInitDone.WaitAsync();
            ServerLifetime.MonoThread(this.root, null, this.OnLifetimeError);
          }
        );
        break;

      case AppServerLifetime.MainThread:
        main = this.root;
        break;
    }

    if (main is not null)
    {
      ServerLifetime.MonoThread(main, allInitDone, this.OnLifetimeError);
    }

    await rootLifetime;
    this.Shutdown();
    await Task.WhenAll(serverLifetimes);
  }



  private static class ServerLifetime
  {
    public static void MonoThread(IAppServer server, CowntdownTask? initDone, Action<Exception>? onError)
    {
      try
      {
        server.Init();
      }
      catch (Exception ex)
      {
        onError?.Invoke(ex);
        return;
      }
      finally
      {
        initDone?.Signal();
      }

      try
      {
        server.Run();
        while (server.Loop()) { }
      }
      catch (Exception ex)
      {
        onError?.Invoke(ex);
      }

      try
      {
        server.Shutdown();
      }
      catch (Exception ex)
      {
        onError?.Invoke(ex);
      }
    }

    public static async Task Async(IAppServer server, CowntdownTask? initDone, Action<Exception>? onError)
    {
      try
      {
        await server.InitAsync();
      }
      catch (Exception ex)
      {
        onError?.Invoke(ex);
        return;
      }
      finally
      {
        initDone?.Signal();
      }

      try
      {
        await server.RunAsync();
        while (await server.LoopAsync()) { }
      }
      catch (Exception ex)
      {
        onError?.Invoke(ex);
      }

      try
      {
        await server.ShutdownAsync();
      }
      catch (Exception ex)
      {
        onError?.Invoke(ex);
      }
    }
  }



  public void Shutdown()
  {
    this.root.Stop();
    foreach (var server in this.servers)
    {
      server.Stop();
    }
  }



  private class CowntdownTask
  {
    private volatile int target;
    private volatile int count;
    private TaskCompletionSource tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public CowntdownTask(int target)
    {
      this.target = target;
      this.count = target;
    }



    public void Signal()
    {
      Interlocked.Decrement(ref count);
      if (this.count == 0) tcs.SetResult();
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
