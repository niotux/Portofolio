using Holodeck.Web.Configuration;
using Microsoft.Extensions.Options;

namespace Holodeck.Web.Rooms;

// Where the library, each agent's own files, and packet files live under the data directory.
public sealed class RoomPaths(IOptions<StudioOptions> options)
{
  private readonly string root = Path.GetFullPath(options.Value.DataPath);

  public string Root => root;

  public string Libraries => Path.GetFullPath(options.Value.LibrariesPath ?? Path.Combine(root, "libs"));

  public string LibraryCache => Path.Combine(root, "library-cache");

  public string AgentDirectory(string projectId, string agentId)
  {
    return Path.Combine(root, "rooms", projectId, "agents", agentId);
  }

  public string Table(string projectId, string agentId)
  {
    return Path.Combine(AgentDirectory(projectId, agentId), "table");
  }

  // The agent's home inside its computer: user-level installs live here and survive restarts.
  public string Home(string projectId, string agentId)
  {
    return Path.Combine(AgentDirectory(projectId, agentId), "home");
  }

  public string SettingsFile(string projectId, string agentId)
  {
    return Path.Combine(AgentDirectory(projectId, agentId), "settings.json");
  }

  public string LogFile(string projectId, string agentId)
  {
    return Path.Combine(AgentDirectory(projectId, agentId), "log.txt");
  }

  public string PacketDirectory(string projectId, long packetId)
  {
    return Path.Combine(root, "rooms", projectId, "packets", packetId.ToString());
  }
}
