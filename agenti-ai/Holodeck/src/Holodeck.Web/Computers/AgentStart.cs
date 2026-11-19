using Holodeck.Web.Rooms;

namespace Holodeck.Web.Computers;

public sealed record AgentStart(RoomMember Member, LibraryAgent Agent, string Key, AgentGrants Grants);
