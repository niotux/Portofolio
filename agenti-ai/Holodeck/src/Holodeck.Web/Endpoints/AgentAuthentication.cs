using Holodeck.Web.Rooms;

namespace Holodeck.Web.Endpoints;

public static class AgentAuthentication
{
  private const string BearerPrefix = "Bearer ";

  public static async Task<AgentCaller> AuthenticateAsync(HttpContext context, RoomStore room)
  {
    string header = context.Request.Headers.Authorization.ToString();
    ReadOnlySpan<char> value = header.AsSpan();
    if (!value.StartsWith(BearerPrefix, StringComparison.Ordinal))
    {
      throw new UnauthorizedAccessException("Send your key as a Bearer token.");
    }

    string hash = AgentKeys.Hash(value[BearerPrefix.Length..].Trim().ToString());
    RoomMember? member = await room.FindBySessionAsync(hash);
    if (member is null)
    {
      throw new UnauthorizedAccessException("That key is not valid. It stops working when the agent is stopped.");
    }

    return new AgentCaller(member, hash);
  }
}
