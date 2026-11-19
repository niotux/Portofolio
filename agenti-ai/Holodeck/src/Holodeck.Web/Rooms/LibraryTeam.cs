namespace Holodeck.Web.Rooms;

public sealed record LibraryTeam(string Folder, string Name, string Description, List<LibraryTeamMember> Members, List<List<string>> Wires, List<LibraryTeamChannel>? Channels = null);
