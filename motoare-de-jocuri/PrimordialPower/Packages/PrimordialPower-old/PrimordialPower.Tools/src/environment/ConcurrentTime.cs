using System.Diagnostics;

namespace PrimordialPower.Tools;

public sealed class ConcurrentTime : ITime<ConcurrentTime>
{
  public ConcurrentTime()
  {
    this.usedDTClock = this.originalDTClock;
    this.usedClock = this.originalClock;
  }



  public static ConcurrentTime Shared { get; } = new();



  private readonly Stopwatch originalDTClock = new();
  private Stopwatch usedDTClock;
  private double lastDTCall = 0D;
  private bool isFirstDTCall = true;
  public double DeltaTime
  {
    get
    {
      double currentTime = this.usedDTClock.Elapsed.TotalSeconds;

      if (this.isFirstDTCall)
      {
        this.lastDTCall = currentTime;
        this.isFirstDTCall = false;
        this.usedDTClock.Start();
        return 0D;
      }

      double deltaTime = currentTime - this.lastDTCall;
      this.lastDTCall = currentTime;
      return deltaTime;
    }
  }



  private readonly Stopwatch originalClock = new();
  private Stopwatch usedClock;
  public Stopwatch Clock
  {
    get => this.usedClock;
    private set => this.usedClock = value;
  }


  private readonly object lockState = new();



  private void ResetDeltaTimeUnlocked()
  {
    this.usedDTClock.Reset();
    this.lastDTCall = 0D;
    this.isFirstDTCall = true;
  }

  public void ResetDeltaTime()
  {
    lock (lockState)
    {
      ResetDeltaTimeUnlocked();
    }
  }

  public void SyncWith(ConcurrentTime other)
  {
    lock (lockState)
    {
      this.usedClock = other.usedClock;

      this.lastDTCall = other.lastDTCall;
      this.isFirstDTCall = other.isFirstDTCall;
      this.usedDTClock = other.usedDTClock;
    }
  }

  private void BreakSyncUnlocked()
  {
    this.usedClock = this.originalClock;
    this.usedDTClock = this.originalDTClock;
  }

  public void BreakSync()
  {
    lock (lockState)
    {
      BreakSyncUnlocked();
    }
  }

  public void BreakSyncReset()
  {
    lock (lockState)
    {
      BreakSyncUnlocked();
      this.usedClock.Reset();
      ResetDeltaTimeUnlocked();
    }
  }

  public void BreakSyncRestart()
  {
    lock (lockState)
    {
      BreakSyncUnlocked();
      this.usedClock.Reset();
      ResetDeltaTimeUnlocked();
      this.usedClock.Start();
    }
  }
}