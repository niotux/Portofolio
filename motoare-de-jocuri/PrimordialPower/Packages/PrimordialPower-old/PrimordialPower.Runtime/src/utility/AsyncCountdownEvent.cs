namespace PrimordialPower.Runtime;

internal class AsyncCountdownEvent
{
  private int count;
  private int target;
  private TaskCompletionSource tcs;

  public AsyncCountdownEvent(int targetCount)
  {
    if (targetCount < 0) throw new ArgumentOutOfRangeException(nameof(targetCount));
    this.target = targetCount;
    this.count = 0;
    this.tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    if (this.target == 0)
      this.tcs.TrySetResult();
  }

  private readonly object lockReset = new();
  public void Reset(int newTarget)
  {
    if (newTarget < 0) throw new ArgumentOutOfRangeException(nameof(newTarget));
    lock (lockReset)
    {
      this.target = newTarget;
      this.count = 0;

      if (this.tcs.Task.IsCompleted)
        this.tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

      if (this.target == 0)
        this.tcs.TrySetResult();
    }
  }

  public void Signal()
  {
    int newCount = Interlocked.Increment(ref count);
    if (newCount == this.target)
    {
      this.tcs.TrySetResult();
    }
  }

  public Task WaitAsync()
  {
    return this.tcs.Task;
  }
}

