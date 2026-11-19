using System.Net;
using Holodeck.Web.Computers;
using Xunit;

namespace Holodeck.Tests;

public sealed class AgentPortGuardTests
{
  private const int UserPort = 8080;
  private const int AgentPort = 8081;
  private static readonly List<IPNetwork> AgentSubnets = [IPNetwork.Parse("172.30.0.0/16")];

  [Theory]
  [InlineData(AgentPort, "172.30.0.5", "/api/agent/next", 0)]
  [InlineData(AgentPort, "172.30.0.5", "/api/agent", 0)]
  [InlineData(AgentPort, "172.30.0.5", "/api/projects", 404)]
  [InlineData(AgentPort, "172.30.0.5", "/api/agentish", 404)]
  [InlineData(AgentPort, "172.30.0.5", "/", 404)]
  [InlineData(UserPort, "172.30.0.5", "/api/projects", 403)]
  [InlineData(UserPort, "172.30.0.5", "/api/agent/next", 403)]
  [InlineData(UserPort, "::ffff:172.30.0.5", "/", 403)]
  [InlineData(UserPort, "127.0.0.1", "/api/projects", 0)]
  [InlineData(UserPort, "172.18.0.1", "/api/projects", 0)]
  [InlineData(5000, "172.30.0.5", "/api/projects", 0)]
  public void KeepsAgentsOnTheAgentApi(int localPort, string remote, string path, int expected)
  {
    Assert.Equal(expected, AgentPortGuard.Decide(localPort, IPAddress.Parse(remote), path, AgentSubnets, UserPort, AgentPort));
  }
}
