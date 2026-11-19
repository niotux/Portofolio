using System.Text.Json;
using Holodeck.Web.Management;
using Holodeck.Web.Rooms;

namespace Holodeck.Web.Endpoints;

public static class ManagementEndpoints
{
  public static void Map(WebApplication app)
  {
    RouteGroupBuilder user = app.MapGroup("/api/projects/{projectId}/room/agents/{id}");
    user.MapGet("/actions", (string projectId, string id, RoomStore room, ManagementStore jobs) => RunAsync(async () => Results.Ok(await jobs.ActionsAsync(await room.GetMemberAsync(projectId, id)))));
    user.MapGet("/jobs", (string projectId, string id, RoomStore room, ManagementStore jobs) => RunAsync(async () => Results.Ok(await jobs.ListAsync(await room.GetMemberAsync(projectId, id)))));
    user.MapPost("/jobs", EnqueueAsync);
    user.MapPost("/jobs/{jobId}/cancel", (string projectId, string id, string jobId, RoomStore room, ManagementStore jobs) => RunAsync(async () => Results.Ok(await jobs.CancelAsync(await room.GetMemberAsync(projectId, id), jobId))));
    RouteGroupBuilder agent = app.MapGroup("/api/agent/jobs");
    agent.MapPost("/claim", ClaimAsync);
    agent.MapPost("/{jobId}/progress", (HttpContext context, string jobId, ManagementProgress input, RoomStore room, ManagementStore jobs) => RunAsync(async () => Results.Ok(await jobs.ProgressAsync(await AgentAuthentication.AuthenticateAsync(context, room), jobId, input))));
    agent.MapPost("/{jobId}/complete", (HttpContext context, string jobId, ManagementCompletion input, RoomStore room, ManagementStore jobs) => RunAsync(async () => Results.Ok(await jobs.CompleteAsync(await AgentAuthentication.AuthenticateAsync(context, room), jobId, input))));
    agent.MapPost("/{jobId}/fail", (HttpContext context, string jobId, ManagementFailure input, RoomStore room, ManagementStore jobs) => RunAsync(async () => Results.Ok(await jobs.FailAsync(await AgentAuthentication.AuthenticateAsync(context, room), jobId, input))));
  }

  private static Task<IResult> EnqueueAsync(string projectId, string id, ManagementEnqueue input, RoomStore room, ManagementStore jobs)
  {
    return RunAsync(async () =>
    {
      ManagementEnqueued result = await jobs.EnqueueAsync(await room.GetMemberAsync(projectId, id), input);
      if (result.IsReplay)
      {
        return Results.Ok(result.Job);
      }

      return Results.Created($"/api/projects/{projectId}/room/agents/{id}/jobs/{result.Job.Id}", result.Job);
    });
  }

  private static Task<IResult> ClaimAsync(HttpContext context, ManagementClaimInput input, RoomStore room, ManagementStore jobs)
  {
    return RunAsync(async () =>
    {
      ManagementClaim? claim = await jobs.ClaimAsync(await AgentAuthentication.AuthenticateAsync(context, room), input);
      if (claim is null)
      {
        return Results.NoContent();
      }

      return Results.Ok(claim);
    });
  }

  private static Task<IResult> RunAsync(Func<Task<IResult>> operation)
  {
    return EndpointErrors.RunAsync(async () =>
    {
      try
      {
        return await operation();
      }
      catch (ManagementValidationException exception)
      {
        return Results.BadRequest(new ManagementValidationError(exception.Message, exception.Errors));
      }
      catch (JsonException)
      {
        throw new ArgumentException("The action payload must be valid JSON without duplicate properties.");
      }
    });
  }
}
