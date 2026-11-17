using System.Diagnostics;

namespace PrimordialPower.Tools;

public interface ITime<TSelf>
{
  double DeltaTime { get; }
  Stopwatch Clock { get; }
  static abstract TSelf Shared { get; }

  void ResetDeltaTime();
  void SyncWith(TSelf other);
  void BreakSync();
  void BreakSyncReset();
  void BreakSyncRestart();
}