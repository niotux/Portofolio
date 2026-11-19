namespace Holodeck.Web.Rooms;

// A channel a team template creates: its name and the team member IDs (or You) in it.
public sealed record LibraryTeamChannel(string Name, List<string> Members);
