namespace Holodeck.Web.Rooms;

// Members keeps the other members' display names for older programs; Participants identifies every active member.
public sealed record AgentChannel(string ChannelId, string Channel, List<string> Members, List<AgentParticipant> Participants);
