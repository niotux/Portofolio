using System;
using System.Collections;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace QuantiX.Engine;

public abstract class AppServer
{
  public App? App { get; set; } = null;

  public AppServerState State { get; private set; } = AppServerState.Stopped;
  public string InstanceId => GenerateInstanceID(this);



  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  protected virtual Task OnInitAsync()
  {
    this.OnInit();
    return Task.CompletedTask;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  protected virtual Task OnRunAsync()
  {
    this.OnRun();
    return Task.CompletedTask;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  protected virtual Task OnLoopAsync()
  {
    this.OnLoop();
    return Task.CompletedTask;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  protected virtual Task OnShutdownAsync()
  {
    this.OnShutdown();
    return Task.CompletedTask;
  }



  protected virtual void OnInit() { }
  protected virtual void OnStart() { }
  protected virtual void OnRun() { }
  protected virtual void OnLoop() => this.Stop();
  protected virtual void OnStop() { }
  protected virtual void OnShutdown() { }



  public event Action<AppServer>? OnInitDone;
  public event AppServerException.Handler? OnException;



  public virtual AppServerLifetime Lifetime => AppServerLifetime.Async;
  public void RunMonoLifetime()
  {
    try
    {
      this.State = AppServerState.Initializing;
      this.OnInit();

      this.State = AppServerState.InitDone;
      var e = this.OnInitDone;
      e?.Invoke(this);
      this.waitOnStart.Wait();

      if (this.State == AppServerState.Running)
      {
        this.OnRun();
        while (this.State == AppServerState.Running)
        {
          this.OnLoop();
        }
      }
      this.OnShutdown();
      this.State = AppServerState.Stopped;
    }
    catch (Exception ex)
    {
      AppServerException appEx = new(this, ex);
      var e = this.OnException;
      switch (e?.Invoke(appEx))
      {
        case AppServerException.HandlerResult.StopServer:
          this.Stop();
          break;

        case AppServerException.HandlerResult.StopApplication:
          this.App?.Stop();
          break;

        case AppServerException.HandlerResult.RestartServer:
          this.RunMonoLifetime();
          break;

        default:
        case AppServerException.HandlerResult.KillProcess:
          Environment.Exit(1);
          break;
      }
    }
  }

  public async Task RunAsyncLifetime()
  {
    try
    {
      this.State = AppServerState.Initializing;
      await this.OnInitAsync();

      this.State = AppServerState.InitDone;
      var e = this.OnInitDone;
      e?.Invoke(this);
      await this.waitOnStart.WaitAsync();

      if (this.State == AppServerState.Running)
      {
        await this.OnRunAsync();
        while (this.State == AppServerState.Running)
        {
          await this.OnLoopAsync();
        }
      }

      await this.OnShutdownAsync();
      this.State = AppServerState.Stopped;
    }
    catch (Exception ex)
    {
      AppServerException appEx = new(this, ex);
      var e = this.OnException;
      switch (e?.Invoke(appEx))
      {
        case AppServerException.HandlerResult.StopServer:
          this.Stop();
          break;

        case AppServerException.HandlerResult.StopApplication:
          this.App?.Stop();
          break;

        case AppServerException.HandlerResult.RestartServer:
          await this.RunAsyncLifetime();
          break;

        default:
        case AppServerException.HandlerResult.KillProcess:
          Environment.Exit(1);
          break;
      }
    }
  }



  private SemaphoreSlim waitOnStart = new(0, 1);
  public void Start()
  {
    this.State = AppServerState.Running;
    if (this.waitOnStart.CurrentCount == 0)
    {
      this.waitOnStart.Release();
    }
  }

  public void Stop()
  {
    this.Start();
    this.State = AppServerState.StopRequested;
    this.OnStop();
  }



  private static string GenerateInstanceID(AppServer instance)
  {
    string result = $"{instance.GetType().FullName}::{instance.GetHashCode()}";
    return result;
  }
}
