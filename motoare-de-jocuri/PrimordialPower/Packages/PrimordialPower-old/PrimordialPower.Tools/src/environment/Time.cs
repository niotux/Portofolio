using System.Diagnostics;

namespace PrimordialPower.Tools;

public sealed class Time : ITime<Time>
{
  public Time()
  {
    this.usedDTClock = this.originalDTClock;
    this.usedClock = this.originalClock;
  }



  public static Time Shared { get; } = new();



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



  public void ResetDeltaTime()
  {
    this.usedDTClock.Reset();
    this.lastDTCall = 0D;
    this.isFirstDTCall = true;
  }

  public void SyncWith(Time other)
  {
    this.usedClock = other.usedClock;

    this.lastDTCall = other.lastDTCall;
    this.isFirstDTCall = other.isFirstDTCall;
    this.usedDTClock = other.usedDTClock;
  }

  public void BreakSync()
  {
    this.usedClock = this.originalClock;
    this.usedDTClock = this.originalDTClock;
  }

  public void BreakSyncReset()
  {
    BreakSync();
    this.usedClock.Reset();
    ResetDeltaTime();
  }

  public void BreakSyncRestart()
  {
    BreakSyncReset();
    this.usedClock.Restart();
  }
}