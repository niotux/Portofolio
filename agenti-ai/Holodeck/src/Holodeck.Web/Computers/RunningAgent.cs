namespace Holodeck.Web.Computers;

// A started agent: Exited completes with the exit code; Stop ends the process and retains its computer.
public sealed class RunningAgent(Task<int> exited, Func<Task> stop)
{
  public Task<int> Exited { get; } = exited;

  public Task StopAsync()
  {
    return stop();
  }
}
