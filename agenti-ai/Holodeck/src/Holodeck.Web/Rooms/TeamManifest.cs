namespace Holodeck.Web.Rooms;

internal sealed record TeamManifest(string? Id, string? Name, string? Description, List<TeamMemberManifest>? Members, List<List<string>>? Wires, List<TeamChannelManifest>? Channels = null);
