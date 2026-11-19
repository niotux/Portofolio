using Holodeck.Web.Computers;
using Holodeck.Web.Rooms;
using Xunit;

namespace Holodeck.Tests;

public sealed class AgentGrantsTests
{
  private static readonly HostPaths Host = new("/srv/holodeck", "/srv/holodeck/data", "/srv/holodeck/libs");
  private static readonly AgentGrants None = new([], []);

  [Fact]
  public void ValidGrantsPassAndNewPortsGetFreeHostPorts()
  {
    AgentGrants requested = new([new PortGrant(8188, 0), new PortGrant(9000, 0)], [new FolderGrant("repo", "/home/me/code/repo/", false)]);
    AgentGrants granted = GrantRules.Validate(requested, None, Options(), Host, [20000]);
    Assert.Equal(new PortGrant(8188, 20001), granted.Ports[0]);
    Assert.Equal(new PortGrant(9000, 20002), granted.Ports[1]);
    Assert.Equal("/home/me/code/repo", granted.Folders[0].HostPath);
  }

  [Fact]
  public void ExistingPortAssignmentsAreKept()
  {
    AgentGrants current = new([new PortGrant(8188, 20007)], []);
    AgentGrants granted = GrantRules.Validate(new AgentGrants([new PortGrant(8188, 0)], []), current, Options(), Host, [20007]);
    Assert.Equal(new PortGrant(8188, 20007), Assert.Single(granted.Ports));
  }

  [Theory]
  [InlineData("relative/repo")]
  [InlineData("/home/me/code/../../etc")]
  [InlineData("/home/me/code/./repo")]
  [InlineData("/etc")]
  [InlineData("/home/me")]
  [InlineData("/srv/holodeck/data/rooms")]
  [InlineData("/srv")]
  [InlineData("/srv/holodeck")]
  public void FoldersOutsideTheRootsOrOverlappingHolodeckAreRefused(string path)
  {
    AgentGrants requested = new([], [new FolderGrant("repo", path, false)]);
    Assert.Throws<ArgumentException>(() => GrantRules.Validate(requested, None, Options(["/home/me/code", "/srv"]), Host, []));
  }

  [Theory]
  [InlineData("Repo")]
  [InlineData("-repo")]
  [InlineData("re po")]
  [InlineData("")]
  public void FolderNamesAreSimple(string name)
  {
    AgentGrants requested = new([], [new FolderGrant(name, "/home/me/code/repo", false)]);
    Assert.Throws<ArgumentException>(() => GrantRules.Validate(requested, None, Options(), Host, []));
  }

  [Fact]
  public void RejectsBadPortsAndDuplicates()
  {
    Assert.Throws<ArgumentException>(() => GrantRules.Validate(new AgentGrants([new PortGrant(0, 0)], []), None, Options(), Host, []));
    Assert.Throws<ArgumentException>(() => GrantRules.Validate(new AgentGrants([new PortGrant(70000, 0)], []), None, Options(), Host, []));
    Assert.Throws<ArgumentException>(() => GrantRules.Validate(new AgentGrants([new PortGrant(80, 0), new PortGrant(80, 0)], []), None, Options(), Host, []));
    List<FolderGrant> twice = [new FolderGrant("repo", "/home/me/code/a", false), new FolderGrant("repo", "/home/me/code/b", false)];
    Assert.Throws<ArgumentException>(() => GrantRules.Validate(new AgentGrants([], twice), None, Options(), Host, []));
  }

  [Fact]
  public void NeedsComeFromAgentJson()
  {
    string dataPath = Path.Combine(Path.GetTempPath(), "holodeck-needs-" + Guid.NewGuid().ToString("N"));
    try
    {
      TestLibrary.Write(dataPath, "plain", "Plain", ["sh", "${Agent}/run.sh"]);
      TestLibrary.Write(dataPath, "needy", "Needy", ["sh", "${Agent}/run.sh"], null, new AgentNeeds([8188]));
      Assert.Empty(TestLibrary.Agent(dataPath, "plain").Needs.Ports);
      AgentNeeds needs = TestLibrary.Agent(dataPath, "needy").Needs;
      Assert.Equal([8188], needs.Ports);
    }
    finally
    {
      Directory.Delete(dataPath, true);
    }
  }

  private static ComputerOptions Options(List<string>? roots = null)
  {
    return new ComputerOptions
    {
      PortRangeStart = 20000,
      PortRangeEnd = 20999,
      AgentNetwork = "holodeck-agents",
      OpenNetwork = "holodeck-network",
      AllowedFolderRoots = roots ?? ["/home/me/code"]
    };
  }
}
