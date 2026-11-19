namespace Holodeck.Web.Rooms;

// Peers: everyone the agent shares a channel with. Channels: the channels it is in, with the other members' names.
public sealed record AgentIdentity(string Name, string ProjectId, List<string> Peers, List<AgentChannel> Channels, string MemberId, List<string>? Capabilities = null);
