using System.Net;

namespace Holodeck.Web.Computers;

// Agents reach Holodeck only on the agent port, and there only the agent API. The user port refuses anything
// coming from the agent network, so an agent cannot use the user API (grants, files, other projects).
public static class AgentPortGuard
{
  public const string AgentApiPrefix = "/api/agent";
  public const int Allowed = 0;

  public static int Decide(int localPort, IPAddress? remote, string path, List<IPNetwork> agentSubnets, int userPort, int agentPort)
  {
    if (localPort == agentPort)
    {
      return IsAgentApi(path) ? Allowed : StatusCodes.Status404NotFound;
    }

    if (localPort == userPort && remote is not null && IsAgent(remote, agentSubnets))
    {
      return StatusCodes.Status403Forbidden;
    }

    return Allowed;
  }

  private static bool IsAgentApi(string path)
  {
    ReadOnlySpan<char> value = path.AsSpan();
    if (!value.StartsWith(AgentApiPrefix, StringComparison.Ordinal))
    {
      return false;
    }

    return value.Length == AgentApiPrefix.Length || value[AgentApiPrefix.Length] == '/';
  }

  private static bool IsAgent(IPAddress remote, List<IPNetwork> agentSubnets)
  {
    IPAddress address = remote.IsIPv4MappedToIPv6 ? remote.MapToIPv4() : remote;
    foreach (IPNetwork subnet in agentSubnets)
    {
      if (subnet.Contains(address))
      {
        return true;
      }
    }

    return false;
  }
}
