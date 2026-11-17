using System;
using System.Threading;
using QuantiX.Engine;

namespace QuantiX.Engine.Tests.AppServers.TestFixtures;

/// <summary>
/// A test server that can be configured for different behaviors.
/// Used for testing AppServer lifetimes and exception handling.
/// </summary>
public class TestServer : AppServer
{
  private Thread? _initThread;
  private Thread? _runThread;
  private Thread? _loopThread;
  private Thread? _shutdownThread;

  public string Name { get; set; } = "TestServer";
  public bool ThrowInInit { get; set; } = false;
  public bool ThrowInRun { get; set; } = false;
  public bool ThrowInLoop { get; set; } = false;
  public bool ThrowInShutdown { get; set; } = false;
  public int LoopIterations { get; set; } = 10;
  public bool AutoStop { get; set; } = true;

  public override AppServerLifetime Lifetime { get; } = AppServerLifetime.Async;

  public TestServer()
  {
  }

  public TestServer(AppServerLifetime lifetime)
  {
    Lifetime = lifetime;
  }

  protected override void OnInit()
  {
    _initThread = Thread.CurrentThread;
    Console.WriteLine($"[{Name}] OnInit on thread {Thread.CurrentThread.ManagedThreadId}");

    if (ThrowInInit)
    {
      throw new InvalidOperationException($"Exception in {Name}.OnInit()");
    }
  }

  protected override void OnRun()
  {
    _runThread = Thread.CurrentThread;
    Console.WriteLine($"[{Name}] OnRun on thread {Thread.CurrentThread.ManagedThreadId}");

    if (ThrowInRun)
    {
      throw new InvalidOperationException($"Exception in {Name}.OnRun()");
    }
  }

  protected override void OnLoop()
  {
    _loopThread = Thread.CurrentThread;
    Console.WriteLine($"[{Name}] OnLoop on thread {Thread.CurrentThread.ManagedThreadId}");

    if (ThrowInLoop)
    {
      throw new InvalidOperationException($"Exception in {Name}.OnLoop()");
    }

    if (AutoStop && CurrentIteration >= LoopIterations)
    {
      Stop();
    }

    CurrentIteration++;
  }

  protected override void OnShutdown()
  {
    _shutdownThread = Thread.CurrentThread;
    Console.WriteLine($"[{Name}] OnShutdown on thread {Thread.CurrentThread.ManagedThreadId}");

    if (ThrowInShutdown)
    {
      throw new InvalidOperationException($"Exception in {Name}.OnShutdown()");
    }
  }

  public int CurrentIteration { get; private set; } = 0;

  public bool VerifyThreadAffinity()
  {
    // For MonoThread/MainThread, all methods should run on the same thread
    // For Async, threads may differ

    if (Lifetime == AppServerLifetime.Async)
    {
      // Async can switch threads, so we just verify they were set
      return _initThread != null && _runThread != null && _loopThread != null && _shutdownThread != null;
    }

    // For MonoThread/MainThread, verify same thread throughout
    return _initThread == _runThread &&
           _runThread == _loopThread &&
           _loopThread == _shutdownThread;
  }

  public Thread? GetInitThread() => _initThread;
  public Thread? GetRunThread() => _runThread;
  public Thread? GetLoopThread() => _loopThread;
  public Thread? GetShutdownThread() => _shutdownThread;
}