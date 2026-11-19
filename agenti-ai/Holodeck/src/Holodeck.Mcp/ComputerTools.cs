using System.ComponentModel;
using Holodeck.Web.Computers;
using ModelContextProtocol.Server;

namespace Holodeck.Mcp;

[McpServerToolType]
public sealed class ComputerTools(HolodeckClient holodeck)
{
  [McpServerTool(Name = "read_agent_computer", ReadOnly = true)]
  [Description("Read an agent's computer: what its definition asks for, its recorded grants, the services its library runs in the project environment, the folder roots this machine offers, and disk use.")]
  public Task<string> ReadAgentComputerAsync(string projectId, string agentId, CancellationToken token)
  {
    return holodeck.GetAsync(ComputerPath(projectId, agentId), token);
  }

  [McpServerTool(Name = "set_agent_computer", Idempotent = true)]
  [Description("Replace an agent's grants. Folder and port grants are not available yet, so a change is refused once the agent has its computer.")]
  public Task<string> SetAgentComputerAsync(string projectId, string agentId, [Description("Agent ports to publish; Holodeck assigns the host ports.")] List<int>? ports = null, [Description("Host folders under an allowed root, mounted at /mnt/<name>.")] List<FolderGrant>? folders = null, CancellationToken token = default)
  {
    return holodeck.SendJsonAsync(HttpMethod.Put, ComputerPath(projectId, agentId), new ComputerInput(ports, folders), token);
  }

  [McpServerTool(Name = "reset_agent_computer", Destructive = true)]
  [Description("Reset the agent's installed environment (its home). Its workspace files are kept.")]
  public Task<string> ResetAgentComputerAsync(string projectId, string agentId, CancellationToken token)
  {
    return holodeck.SendAsync(HttpMethod.Post, $"{ComputerPath(projectId, agentId)}/reset", token);
  }

  private static string ComputerPath(string projectId, string agentId)
  {
    return $"{ApiRoutes.Agent(projectId, agentId)}/computer";
  }
}
