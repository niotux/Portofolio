using Holodeck.Web.Activity;
using Holodeck.Web.Rooms;

namespace Holodeck.Web.Endpoints;

public static class ActivityEndpoints
{
  public static void Map(WebApplication app)
  {
    app.MapPost("/api/agent/activity", (HttpContext context, ActivityUpdate input, RoomStore room, ActivityStore activity, RoomSignals signals) => EndpointErrors.RunAsync(async () =>
    {
      AgentCaller caller = await AgentAuthentication.AuthenticateAsync(context, room);
      await activity.WriteAsync(caller, input);
      signals.PulseActivity(caller.Member.ProjectId);
      return Results.NoContent();
    }));
    app.MapGet("/api/projects/{projectId}/room/channels/{channelId}/activity", (string projectId, string channelId, RoomStore room, ActivityStore activity) => EndpointErrors.RunAsync(async () =>
    {
      await room.GetChannelAsync(projectId, channelId);
      return Results.Ok(await activity.ReadAsync(projectId, channelId));
    }));
  }
}
