using Holodeck.Web.Computers;
using Holodeck.Web.Projects;
using Holodeck.Web.Rooms;

namespace Holodeck.Web.Endpoints;

// The Services applet: each declared library service in a project, its process, private log and sudo grant.
public static class ProjectServiceEndpoints
{
  public static void Map(WebApplication app)
  {
    RouteGroupBuilder services = app.MapGroup("/api/projects/{projectId}/services");
    services.MapGet("", (string projectId, ProjectApplications applications, CancellationToken token) => EndpointErrors.RunAsync(async () => Results.Ok(await applications.ServicesAsync(projectId, token))));
    services.MapPost("/{libraryId}/{version}/{serviceId}/start", (string projectId, string libraryId, string version, string serviceId, ProjectApplications applications, RoomSignals signals, CancellationToken token) => SetRunningAsync(projectId, libraryId, version, serviceId, true, applications, signals, token));
    services.MapPost("/{libraryId}/{version}/{serviceId}/stop", (string projectId, string libraryId, string version, string serviceId, ProjectApplications applications, RoomSignals signals, CancellationToken token) => SetRunningAsync(projectId, libraryId, version, serviceId, false, applications, signals, token));
    services.MapGet("/{libraryId}/{version}/{serviceId}/log", (string projectId, string libraryId, string version, string serviceId, ProjectApplications applications, CancellationToken token) => EndpointErrors.RunAsync(async () => Results.Text(await applications.ServiceLogAsync(projectId, libraryId, version, serviceId, token))));
    services.MapPut("/{libraryId}/{version}/{serviceId}/sudo", SetSudoAsync);
  }

  private static Task<IResult> SetRunningAsync(string projectId, string libraryId, string version, string serviceId, bool running, ProjectApplications applications, RoomSignals signals, CancellationToken token)
  {
    return EndpointErrors.RunAsync(async () =>
    {
      await applications.SetServiceRunningAsync(projectId, libraryId, version, serviceId, running, token);
      signals.Pulse(projectId, "");
      return Results.Ok(await applications.ServicesAsync(projectId, token));
    });
  }

  // The service restarts; a container from before sudo is replaced with the project's agents stopped meanwhile.
  private static Task<IResult> SetSudoAsync(string projectId, string libraryId, string version, string serviceId, SudoInput input, ProjectApplications applications, AgentSupervisor supervisor, RoomSignals signals, CancellationToken token)
  {
    return EndpointErrors.RunAsync(async () =>
    {
      SudoChange change = await applications.SetServiceSudoAsync(projectId, libraryId, version, serviceId, input.Enabled, token);
      if (change == SudoChange.RecreateEnvironment)
      {
        await supervisor.WhileAgentsStoppedAsync(projectId, () => applications.RecreateAsync(projectId, libraryId + "/" + version, token), token);
      }

      signals.Pulse(projectId, "");
      return Results.Ok(await applications.ServicesAsync(projectId, token));
    });
  }
}
