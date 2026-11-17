using System;
using System.Collections;
using System.Runtime.CompilerServices;

namespace PrimordialPower.Runtime;

public abstract class Server : IServer
{
  public const bool StopLoop = false;
  public const bool ContinueLoop = true;



  public virtual ServerType Type => ServerType.Async;



  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public virtual Task InitAsync()
  {
    this.Init();
    return Task.CompletedTask;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public virtual Task StartAsync()
  {
    this.Start();
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
  public virtual void Start() { }
  public virtual bool Loop() => StopLoop;
  public virtual void Shutdown() { }
  protected bool Running = ContinueLoop;
  public virtual void SignalShutdown()
  {
    this.Running = StopLoop;
  }
}



public abstract class Server<TServices> : Server, IServer<TServices>
{
  public abstract TServices Services { get; }
}
