namespace Holodeck.Web.Rooms;

// The agent a request's key belongs to, and the session that key started.
public sealed record AgentCaller(RoomMember Member, string SessionHash);
