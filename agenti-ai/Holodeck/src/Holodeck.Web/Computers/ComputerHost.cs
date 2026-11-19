using System.Net;

namespace Holodeck.Web.Computers;

// What Holodeck learned about its own host at startup: where its folders are, and the agent network's subnets.
// Until then (or without Docker) agents cannot start and grants cannot be checked.
public sealed class ComputerHost
{
  private readonly Lock gate = new();
  private HostPaths? paths;
  private List<IPNetwork> agentSubnets = new();

  public HostPaths Paths
  {
    get
    {
      lock (gate)
      {
        return paths ?? throw new InvalidOperationException("Agent computers are not ready: Holodeck could not inspect its Docker host.");
      }
    }
  }

  public List<IPNetwork> AgentSubnets
  {
    get
    {
      lock (gate)
      {
        return agentSubnets;
      }
    }
  }

  public void Learn(HostPaths learned, List<IPNetwork> subnets)
  {
    lock (gate)
    {
      paths = learned;
      agentSubnets = subnets;
    }
  }
}
