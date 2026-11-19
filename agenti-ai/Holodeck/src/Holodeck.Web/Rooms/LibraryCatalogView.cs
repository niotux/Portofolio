namespace Holodeck.Web.Rooms;

public sealed record LibraryCatalogView(List<LibraryAgentView> Agents, List<LibraryTeamView> Teams, List<LibraryCatalogAppletView> Applets, List<LibraryDiagnostic> Diagnostics, List<LibrarySummary> Libraries);
