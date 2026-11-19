namespace Holodeck.Web.Rooms;

public sealed record LibraryCatalog(List<LibraryAgent> Agents, List<LibraryTeam> Teams, List<LibraryDiagnostic> Diagnostics, List<LibrarySummary> Libraries)
{
  public List<LibraryApplicationPackage> Applications { get; init; } = new();
}
