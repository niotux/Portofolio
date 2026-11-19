using System.ComponentModel;
using Holodeck.Web.Rooms;
using Holodeck.Web.Settings;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Holodeck.Mcp;

[McpServerToolType]
public sealed class AgentTools(HolodeckClient holodeck)
{
  [McpServerTool(Name = "room_view", ReadOnly = true)]
  [Description("Read a project's room: members (You and agents, with ids, names, library and status) and channels. Message senderIds refer to these member ids.")]
  public Task<string> RoomViewAsync(string projectId, CancellationToken token)
  {
    return holodeck.GetAsync(ApiRoutes.Room(projectId), token);
  }

  [McpServerTool(Name = "place_agent")]
  [Description("Place a stopped agent from a library attached to the project. Configure it, then start it.")]
  public Task<string> PlaceAgentAsync(string projectId, [Description("The agent's folder value from library_catalog.")] string library, CancellationToken token)
  {
    return holodeck.SendJsonAsync(HttpMethod.Post, $"{ApiRoutes.Room(projectId)}/agents", new PlaceInput(library), token);
  }

  [McpServerTool(Name = "place_team")]
  [Description("Place a library team: its members as stopped agents plus its channels.")]
  public Task<string> PlaceTeamAsync(string projectId, [Description("The team's folder value from library_catalog.")] string team, CancellationToken token)
  {
    return holodeck.SendJsonAsync(HttpMethod.Post, $"{ApiRoutes.Room(projectId)}/teams", new PlaceTeamInput(team), token);
  }

  [McpServerTool(Name = "rename_agent", Idempotent = true)]
  [Description("Rename an agent. Its id, settings, files and history stay the same.")]
  public Task<string> RenameAgentAsync(string projectId, string agentId, string name, CancellationToken token)
  {
    return holodeck.SendJsonAsync(HttpMethod.Put, ApiRoutes.Agent(projectId, agentId), new RenameInput(name), token);
  }

  [McpServerTool(Name = "replace_agent_definition")]
  [Description("Stop an agent and point it at another library agent definition, keeping its settings, files and history.")]
  public Task<string> ReplaceAgentDefinitionAsync(string projectId, string agentId, [Description("The new agent's folder value from library_catalog.")] string library, CancellationToken token)
  {
    return holodeck.SendJsonAsync(HttpMethod.Post, $"{ApiRoutes.Agent(projectId, agentId)}/definition", new ReplaceDefinitionInput(library), token);
  }

  [McpServerTool(Name = "remove_agent", Destructive = true)]
  [Description("Stop an agent and remove it from the room. Its past messages stay in channel history.")]
  public Task<string> RemoveAgentAsync(string projectId, string agentId, CancellationToken token)
  {
    return holodeck.SendAsync(HttpMethod.Delete, ApiRoutes.Agent(projectId, agentId), token);
  }

  [McpServerTool(Name = "remove_unresolved_agents", Destructive = true)]
  [Description("Remove every agent whose library is no longer attached or available.")]
  public Task<string> RemoveUnresolvedAgentsAsync(string projectId, CancellationToken token)
  {
    return holodeck.SendAsync(HttpMethod.Delete, $"{ApiRoutes.Room(projectId)}/unresolved", token);
  }

  [McpServerTool(Name = "start_agent", Idempotent = true)]
  [Description("Start an agent as its own user in its library's project environment.")]
  public Task<string> StartAgentAsync(string projectId, string agentId, CancellationToken token)
  {
    return holodeck.SendAsync(HttpMethod.Post, $"{ApiRoutes.Agent(projectId, agentId)}/start", token);
  }

  [McpServerTool(Name = "stop_agent", Idempotent = true)]
  [Description("Stop an agent. Its workspace, settings and undelivered messages are kept.")]
  public Task<string> StopAgentAsync(string projectId, string agentId, CancellationToken token)
  {
    return holodeck.SendAsync(HttpMethod.Post, $"{ApiRoutes.Agent(projectId, agentId)}/stop", token);
  }

  [McpServerTool(Name = "agent_log", ReadOnly = true)]
  [Description("Read the agent program's recent output.")]
  public Task<string> AgentLogAsync(string projectId, string agentId, CancellationToken token)
  {
    return holodeck.GetAsync($"{ApiRoutes.Agent(projectId, agentId)}/log", token);
  }

  [McpServerTool(Name = "read_agent_settings", ReadOnly = true)]
  [Description("Read an agent's settings: content, revision, schema and schemaRevision (form mode), and current errors. save_agent_settings needs the two revisions.")]
  public Task<string> ReadAgentSettingsAsync(string projectId, string agentId, CancellationToken token)
  {
    return holodeck.GetAsync($"{ApiRoutes.Agent(projectId, agentId)}/settings/form", token);
  }

  [McpServerTool(Name = "save_agent_settings")]
  [Description("Save an agent's settings against the revisions they were read at; a stale revision is refused with the current settings. Give either content, the whole settings text, or changes to individual fields, which leave every other value as stored. Saving does not restart the agent.")]
  public Task<string> SaveAgentSettingsAsync(string projectId, string agentId, [Description("revision from read_agent_settings.")] string baseRevision, [Description("schemaRevision from read_agent_settings, when it has one.")] string? schemaRevision = null, [Description("The whole new settings text.")] string? content = null, [Description("Field changes. path is a JSON Pointer such as /model/name; set value, or remove to delete the stored value.")] List<SettingsChange>? changes = null, CancellationToken token = default)
  {
    bool hasContent = content is not null;
    bool hasChanges = changes is not null;
    if (hasContent == hasChanges)
    {
      throw new McpException("Give either content or changes.");
    }

    return holodeck.SendJsonAsync(HttpMethod.Put, $"{ApiRoutes.Agent(projectId, agentId)}/settings/form", new SettingsWrite(baseRevision, schemaRevision, content, changes), token);
  }

  [McpServerTool(Name = "list_agent_files", ReadOnly = true)]
  [Description("List files on the agent's table (its workspace), with sizes.")]
  public Task<string> ListAgentFilesAsync(string projectId, string agentId, CancellationToken token)
  {
    return holodeck.GetAsync($"{ApiRoutes.Agent(projectId, agentId)}/table", token);
  }

  [McpServerTool(Name = "read_agent_file", ReadOnly = true)]
  [Description("Read a file from the agent's table. Small UTF-8 text comes back inline; other files are saved locally and the answer gives the path.")]
  public Task<string> ReadAgentFileAsync(string projectId, string agentId, [Description("Path relative to the table, as list_agent_files gives it.")] string path, CancellationToken token)
  {
    return holodeck.DownloadAsync($"{ApiRoutes.Agent(projectId, agentId)}/table/file?path={HolodeckClient.Segment(path)}", path, token);
  }
}
