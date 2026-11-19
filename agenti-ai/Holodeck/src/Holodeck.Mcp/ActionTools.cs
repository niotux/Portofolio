using System.ComponentModel;
using System.Text.Json;
using Holodeck.Web.Management;
using ModelContextProtocol.Server;

namespace Holodeck.Mcp;

// Management actions an agent definition declares, such as generating prompts, run as jobs the agent claims.
[McpServerToolType]
public sealed class ActionTools(HolodeckClient holodeck)
{
  [McpServerTool(Name = "list_agent_actions", ReadOnly = true)]
  [Description("List the management actions an agent's definition declares, with their input schemas and the definitionRevision run_agent_action needs.")]
  public Task<string> ListAgentActionsAsync(string projectId, string agentId, CancellationToken token)
  {
    return holodeck.GetAsync($"{ApiRoutes.Agent(projectId, agentId)}/actions", token);
  }

  [McpServerTool(Name = "list_agent_jobs", ReadOnly = true)]
  [Description("List an agent's management jobs with status, progress and results.")]
  public Task<string> ListAgentJobsAsync(string projectId, string agentId, CancellationToken token)
  {
    return holodeck.GetAsync($"{ApiRoutes.Agent(projectId, agentId)}/jobs", token);
  }

  [McpServerTool(Name = "run_agent_action")]
  [Description("Queue a management action for a running agent. Actions use the saved settings. Repeating a call with the same requestId returns the same job instead of queueing another.")]
  public Task<string> RunAgentActionAsync(string projectId, string agentId, string actionId, [Description("definitionRevision from list_agent_actions.")] string definitionRevision, [Description("Input matching the action's input schema.")] JsonElement input, [Description("A unique id for this request; reuse it only to retry the same request. Omit to have one made.")] string? requestId = null, CancellationToken token = default)
  {
    ManagementEnqueue body = new(actionId, requestId ?? Guid.NewGuid().ToString("N"), definitionRevision, input);
    return holodeck.SendJsonAsync(HttpMethod.Post, $"{ApiRoutes.Agent(projectId, agentId)}/jobs", body, token);
  }

  [McpServerTool(Name = "cancel_agent_job", Idempotent = true)]
  [Description("Cancel a queued or running management job.")]
  public Task<string> CancelAgentJobAsync(string projectId, string agentId, string jobId, CancellationToken token)
  {
    return holodeck.SendAsync(HttpMethod.Post, $"{ApiRoutes.Agent(projectId, agentId)}/jobs/{HolodeckClient.Segment(jobId)}/cancel", token);
  }
}
