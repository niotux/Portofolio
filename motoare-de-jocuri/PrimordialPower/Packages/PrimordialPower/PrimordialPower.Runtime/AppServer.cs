using System;
using System.Collections;
using System.Runtime.CompilerServices;

namespace PrimordialPower.Runtime;

public abstract class AppServer : IAppServer
{
  public const bool StopLoop = false;
  public const bool ContinueLoop = true;



  public virtual AppServerLifetime Lifetime => AppServerLifetime.Async;
  public Application? Application { get; set; } = null;



  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public virtual Task InitAsync()
  {
    this.Init();
    return Task.CompletedTask;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public virtual Task RunAsync()
  {
    this.Run();
    return Task.CompletedTask;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public virtual Task<bool> LoopAsync()
  {
    return Task.FromResult(this.Loop());
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public virtual Task ShutdownAsync()
  {
    this.Shutdown();
    return Task.CompletedTask;
  }



  public virtual void Init() { }
  public virtual void Run() { }
  public virtual bool Loop() => StopLoop;
  public virtual void Shutdown() { }
  public virtual void OnStop() { }
  public bool StopRequested { get; protected set; } = false;

  public void Stop()
  {
    OnStop();
    this.StopRequested = true;
  }



  private static ulong number;
  protected string GenerateInstanceID()
  {
    string result = $"{this.GetType().FullName}:{number}:{this.GetHashCode()}";
    number++;
    return result;
  }
}
