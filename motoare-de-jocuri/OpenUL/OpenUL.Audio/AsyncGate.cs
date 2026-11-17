using System.Collections.ObjectModel;
using System.Runtime.Serialization;
using System.Threading.Tasks.Sources;

namespace OpenUL.Utility.Async;

internal class AsyncGate : IValueTaskSource
{
  private ManualResetValueTaskSourceCore<bool> core = new();
  private bool open;
  private readonly object lockObj = new();
  public AsyncGate(bool open = false)
  {
    this.open = open;
    this.core.RunContinuationsAsynchronously = true;
    if (open)
    {
      this.core.SetResult(true);
    }
  }

  public void Open()
  {
    lock (this.lockObj)
    {
      if (this.open)
      {
        return;
      }
      this.open = true;
      this.core.SetResult(true);
    }
  }

  public void Close()
  {
    lock (this.lockObj)
    {
      if (!this.open)
      {
        return;
      }
      this.open = false;
      this.core.Reset();
    }
  }

  public ValueTask WaitToOpenAsync()
  {
    if (this.open)
    {
      return ValueTask.CompletedTask;
    }

    return new ValueTask(this, core.Version);
  }

  void IValueTaskSource.GetResult(short token)
    => this.core.GetResult(token);

  ValueTaskSourceStatus IValueTaskSource.GetStatus(short token)
    => this.core.GetStatus(token);

  void IValueTaskSource.OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
    => this.core.OnCompleted(continuation, state, token, flags);
}
