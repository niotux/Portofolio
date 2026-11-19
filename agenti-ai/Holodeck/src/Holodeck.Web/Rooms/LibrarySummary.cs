namespace Holodeck.Web.Rooms;

public sealed record LibrarySummary(string Id, string Version, string Name, string Description, List<LibraryReference>? Dependencies = null);
