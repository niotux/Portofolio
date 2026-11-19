using Holodeck.Web.AgentData;
using Holodeck.Web.Projects;
using Holodeck.Web.Rooms;

namespace Holodeck.Web.Endpoints;

public static class ProjectEndpoints
{
  public static void Map(WebApplication app)
  {
    app.MapGet("/api/filesystem", (ProjectStore store, CancellationToken token) => EndpointErrors.RunAsync(async () => Results.Ok(await store.ListAsync(token))));
    app.MapPost("/api/projects", CreateProjectAsync);
    app.MapPut("/api/projects/{id}", (string id, ProjectInput input, ProjectStore store, CancellationToken token) => EndpointErrors.RunAsync(async () =>
    {
      await store.UpdateAsync(id, input, token);
      return Results.NoContent();
    }));
    app.MapDelete("/api/projects/{id}", DeleteProjectAsync);
    app.MapPost("/api/folders", (FolderInput input, ProjectStore store, CancellationToken token) => EndpointErrors.RunAsync(async () => Results.Ok(await store.CreateFolderAsync(input, token))));
    app.MapPut("/api/folders/{id}", (string id, FolderInput input, ProjectStore store, CancellationToken token) => EndpointErrors.RunAsync(async () =>
    {
      await store.UpdateFolderAsync(id, input, token);
      return Results.NoContent();
    }));
    app.MapDelete("/api/folders/{id}", DeleteFolderAsync);
  }

  private static Task<IResult> CreateProjectAsync(ProjectInput input, ProjectStore store, RoomStore room, CancellationToken token)
  {
    return EndpointErrors.RunAsync(async () =>
    {
      Project project = await store.CreateAsync(input, token);
      await room.EnsureYouAsync(project.Id);
      return Results.Created($"/api/projects/{project.Id}", project);
    });
  }

  private static Task<IResult> DeleteProjectAsync(string id, ProjectStore store, RoomStore room, AgentSupervisor supervisor, AgentDatabases databases, ProjectApplications applications, CancellationToken token)
  {
    return EndpointErrors.RunAsync(async () =>
    {
      await RemoveProjectAsync(id, store, room, supervisor, databases, applications, token);
      return Results.NoContent();
    });
  }

  // Deleting a folder deletes everything inside it, except the Default project, which moves to the top level.
  private static Task<IResult> DeleteFolderAsync(string id, ProjectStore store, RoomStore room, AgentSupervisor supervisor, AgentDatabases databases, ProjectApplications applications, CancellationToken token)
  {
    return EndpointErrors.RunAsync(async () =>
    {
      List<string> projects = await store.ListProjectsInFolderAsync(id, token);
      foreach (string project in projects)
      {
        await RemoveProjectAsync(project, store, room, supervisor, databases, applications, token);
      }

      await store.DeleteFolderAsync(id, token);
      return Results.Ok(projects);
    });
  }

  // Deleting a project also deletes its agents' containers.
  private static async Task RemoveProjectAsync(string id, ProjectStore store, RoomStore room, AgentSupervisor supervisor, AgentDatabases databases, ProjectApplications applications, CancellationToken token)
  {
    foreach (RoomMember member in await room.ListMembersAsync(id))
    {
      if (!member.IsYou && !member.IsRemoved)
      {
        await supervisor.DeleteAgentAsync(member, token);
      }
    }

    await applications.DeleteAsync(id, async () =>
    {
      await databases.DropProjectAsync(id);
      await store.DeleteAsync(id, token);
    }, token);
  }
}
