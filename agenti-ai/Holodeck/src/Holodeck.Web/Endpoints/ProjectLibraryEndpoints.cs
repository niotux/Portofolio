using Holodeck.Web.Projects;
using Holodeck.Web.Rooms;

namespace Holodeck.Web.Endpoints;

public static class ProjectLibraryEndpoints
{
  public static void Map(WebApplication app)
  {
    RouteGroupBuilder libraries = app.MapGroup("/api/projects/{projectId}/libraries");
    libraries.MapGet("", (string projectId, ProjectLibraryStore store) => EndpointErrors.RunAsync(async () => Results.Ok(await store.ListAsync(projectId))));
    libraries.MapPost("", AddAsync);
    libraries.MapDelete("/{libraryId}/{version}", RemoveAsync);
  }

  private static Task<IResult> AddAsync(string projectId, ProjectLibraryInput input, ProjectLibraryStore store, RoomSignals signals, ProjectApplications applications, CancellationToken token)
  {
    return EndpointErrors.RunAsync(async () =>
    {
      await applications.AddReferenceAsync(projectId, input.Id, input.Version, token);
      signals.Pulse(projectId, "");
      return Results.Ok(await store.ListAsync(projectId));
    });
  }

  private static Task<IResult> RemoveAsync(string projectId, string libraryId, string version, ProjectLibraryStore store, RoomStore room, AgentSupervisor supervisor, RoomSignals signals, ProjectApplications applications, CancellationToken token)
  {
    return EndpointErrors.RunAsync(async () =>
    {
      await applications.RemoveReferenceAsync(projectId, libraryId, version, token);
      foreach (RoomMember member in await room.ListMembersAsync(projectId))
      {
        if (member.IsYou || member.IsRemoved)
        {
          continue;
        }

        if (await store.ErrorForAsync(projectId, member.Library) is not null)
        {
          await supervisor.StopAgentAsync(member, CancellationToken.None);
        }
      }

      signals.Pulse(projectId, "");
      return Results.NoContent();
    });
  }
}
