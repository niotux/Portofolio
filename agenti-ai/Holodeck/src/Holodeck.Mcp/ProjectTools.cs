using System.ComponentModel;
using Holodeck.Web.Projects;
using ModelContextProtocol.Server;

namespace Holodeck.Mcp;

[McpServerToolType]
public sealed class ProjectTools(HolodeckClient holodeck)
{
  [McpServerTool(Name = "list_projects", ReadOnly = true)]
  [Description("List projects and folders. Project ids are used by every other tool.")]
  public Task<string> ListProjectsAsync(CancellationToken token)
  {
    return holodeck.GetAsync("/api/filesystem", token);
  }

  [McpServerTool(Name = "create_project")]
  [Description("Create a project. It starts with an empty room containing only You.")]
  public Task<string> CreateProjectAsync([Description("Project name.")] string name, [Description("Optional description.")] string? description = null, [Description("Optional folder id from list_projects.")] string? folderId = null, CancellationToken token = default)
  {
    return holodeck.SendJsonAsync(HttpMethod.Post, "/api/projects", new ProjectInput(name, description, folderId), token);
  }

  [McpServerTool(Name = "delete_project", Destructive = true)]
  [Description("Stop the project's agents and delete the project with its room, channels and history.")]
  public Task<string> DeleteProjectAsync(string projectId, CancellationToken token)
  {
    return holodeck.SendAsync(HttpMethod.Delete, $"/api/projects/{HolodeckClient.Segment(projectId)}", token);
  }

  [McpServerTool(Name = "library_catalog", ReadOnly = true)]
  [Description("List every local library with its agents and teams, plus package errors. An agent's or team's folder value is what place_agent and place_team take.")]
  public Task<string> LibraryCatalogAsync(CancellationToken token)
  {
    return holodeck.GetAsync("/api/library/catalog", token);
  }

  [McpServerTool(Name = "list_project_libraries", ReadOnly = true)]
  [Description("List the libraries attached to a project. Only attached libraries can be placed in its room.")]
  public Task<string> ListProjectLibrariesAsync(string projectId, CancellationToken token)
  {
    return holodeck.GetAsync(LibrariesPath(projectId), token);
  }

  [McpServerTool(Name = "attach_library", Idempotent = true)]
  [Description("Attach a library at an exact version to a project, with its local dependencies.")]
  public Task<string> AttachLibraryAsync(string projectId, [Description("Library id from library_catalog.")] string libraryId, [Description("Exact library version.")] string version, CancellationToken token)
  {
    return holodeck.SendJsonAsync(HttpMethod.Post, LibrariesPath(projectId), new ProjectLibraryInput(libraryId, version), token);
  }

  [McpServerTool(Name = "detach_library", Destructive = true)]
  [Description("Remove a library reference from a project. Agents that no longer resolve are stopped but kept, with their settings, files and history, for repair or cleanup.")]
  public Task<string> DetachLibraryAsync(string projectId, string libraryId, string version, CancellationToken token)
  {
    return holodeck.SendAsync(HttpMethod.Delete, $"{LibrariesPath(projectId)}/{HolodeckClient.Segment(libraryId)}/{HolodeckClient.Segment(version)}", token);
  }

  private static string LibrariesPath(string projectId)
  {
    return $"/api/projects/{HolodeckClient.Segment(projectId)}/libraries";
  }
}
