using Holodeck.Web.Computers;
using Holodeck.Web.Rooms;

namespace Holodeck.Tests;

// Holds container creation open so lifecycle requests can arrive before a running handle exists.
internal sealed class GatedAgentRuntime : IAgentRuntime
{
  private readonly TaskCompletionSource<AgentStart> requested = new(TaskCreationOptions.RunContinuationsAsynchronously);
  private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
  private readonly TaskCompletionSource<int> exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
  private int stops;
  private AgentComputerState? computer;

  public Task<AgentStart> Requested => requested.Task;
  public int StopCalls => Volatile.Read(ref stops);

  public Task<AgentComputerState?> InspectAsync(RoomMember member, CancellationToken token)
  {
    return Task.FromResult(computer);
  }

  public Task CreateAsync(AgentStart start, CancellationToken token)
  {
    computer ??= new AgentComputerState(start.Key, false);
    return Task.CompletedTask;
  }

  public Task StopAsync(RoomMember member, CancellationToken token)
  {
    if (computer is null || !computer.Running)
    {
      return Task.CompletedTask;
    }

    return StopAsync();
  }

  public Task ResetHomeAsync(RoomMember member, CancellationToken token)
  {
    return Task.CompletedTask;
  }

  public Task DeleteAsync(RoomMember member, CancellationToken token)
  {
    computer = null;
    return Task.CompletedTask;
  }

  public Task PrepareAsync(CancellationToken token)
  {
    return Task.CompletedTask;
  }

  public async Task<RunningAgent> StartAsync(AgentStart start, Action<string> output, CancellationToken token)
  {
    requested.SetResult(start);
    await released.Task.WaitAsync(token);
    computer = new AgentComputerState(start.Key, true);
    return new RunningAgent(exited.Task, StopAsync);
  }

  public void ReleaseStart()
  {
    released.TrySetResult();
  }

  private Task StopAsync()
  {
    Interlocked.Increment(ref stops);
    if (computer is not null)
    {
      computer = computer with { Running = false };
    }
    exited.TrySetResult(0);
    return Task.CompletedTask;
  }
}
