using Holodeck.Web.Computers;

namespace Holodeck.Web.Rooms;

// The supervisor's view of one agent's current or last run.
internal sealed class AgentRun
{
  public RunningAgent? Agent { get; set; }
  public AgentStatus Status { get; set; }
  public bool Stopping { get; set; }
}
