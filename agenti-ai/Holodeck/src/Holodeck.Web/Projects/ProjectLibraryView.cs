namespace Holodeck.Web.Projects;

public sealed record ProjectLibraryView(string Id, string Version, string Name, string Description, bool IsAvailable, string? Error);
