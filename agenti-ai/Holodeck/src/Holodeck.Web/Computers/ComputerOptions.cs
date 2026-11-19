namespace Holodeck.Web.Computers;

// How library environments and agent computers are made. Values come from configuration (appsettings.json and compose).
public sealed class ComputerOptions
{
  public string DockerUrl { get; set; } = "";
  public string LibraryImage { get; set; } = "";
  public string User { get; set; } = "";
  public string AgentNetwork { get; set; } = "";
  public string OpenNetwork { get; set; } = "";
  public int UserPort { get; set; }
  public int AgentPort { get; set; }
  public long MemoryBytes { get; set; }
  public double Cpus { get; set; }
  public long Pids { get; set; }
  public int PortRangeStart { get; set; }
  public int PortRangeEnd { get; set; }
  public long DiskWarningBytes { get; set; }
  public List<string> AllowedFolderRoots { get; set; } = new();
}
