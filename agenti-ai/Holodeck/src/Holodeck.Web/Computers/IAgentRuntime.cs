using Holodeck.Web.Rooms;

namespace Holodeck.Web.Computers;

// An agent owns one computer for its lifetime: its user, home and work folder in its library's project environment.
// Start and stop retain that computer.
public interface IAgentRuntime
{
  Task PrepareAsync(CancellationToken token);
  Task<AgentComputerState?> InspectAsync(RoomMember member, CancellationToken token);
  Task CreateAsync(AgentStart start, CancellationToken token);
  Task StopAsync(RoomMember member, CancellationToken token);
  Task DeleteAsync(RoomMember member, CancellationToken token);
  Task<RunningAgent> StartAsync(AgentStart start, Action<string> output, CancellationToken token);
  Task ResetHomeAsync(RoomMember member, CancellationToken token);
  // Sudo inside the agent's computer, granted only by the user. A runtime without it never grants it.
  Task<bool> SudoAsync(RoomMember member, CancellationToken token) => Task.FromResult(false);
  Task<SudoChange> SetSudoAsync(RoomMember member, bool enabled, CancellationToken token) => throw new InvalidOperationException("This agent runtime cannot grant sudo.");
  Task RecreateEnvironmentAsync(RoomMember member, CancellationToken token) => Task.CompletedTask;
}
