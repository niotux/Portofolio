namespace Holodeck.Web.Rooms;

// Wakes long-polling agents and open rooms when something changes. A wait registers before it returns,
// so a caller that registers, then checks for work, never misses a pulse in between.
public sealed class RoomSignals
{
  private const string MemberPrefix = "m:";
  private const string ActivityPrefix = "a:";
  private const string ProjectPrefix = "p:";
  private readonly Dictionary<string, TaskCompletionSource> waiters = new();
  private readonly Lock gate = new();

  public Task WaitForMemberAsync(string memberId, TimeSpan timeout, CancellationToken token)
  {
    Task signal = Register(MemberPrefix + memberId);
    return WaitOrTimeOutAsync(signal, timeout, token);
  }

  public Task WaitForProjectAsync(string projectId, CancellationToken token)
  {
    return Register(ProjectPrefix + projectId).WaitAsync(token);
  }

  public Task WaitForActivityAsync(string projectId, CancellationToken token)
  {
    return Register(ActivityPrefix + projectId).WaitAsync(token);
  }

  public void PulseActivity(string projectId)
  {
    Release(ActivityPrefix + projectId);
  }

  public void Pulse(string projectId, string memberId)
  {
    Release(MemberPrefix + memberId);
    Release(ProjectPrefix + projectId);
  }

  private Task Register(string key)
  {
    lock (gate)
    {
      if (!waiters.TryGetValue(key, out TaskCompletionSource? waiter))
      {
        waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        waiters[key] = waiter;
      }

      return waiter.Task;
    }
  }

  private void Release(string key)
  {
    TaskCompletionSource? waiter;
    lock (gate)
    {
      if (!waiters.Remove(key, out waiter))
      {
        return;
      }
    }

    waiter.TrySetResult();
  }

  private static async Task WaitOrTimeOutAsync(Task signal, TimeSpan timeout, CancellationToken token)
  {
    try
    {
      await signal.WaitAsync(timeout, token);
    }
    catch (TimeoutException)
    {
    }
  }
}
