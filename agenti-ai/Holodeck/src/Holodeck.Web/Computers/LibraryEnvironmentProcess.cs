namespace Holodeck.Web.Computers;

public sealed record LibraryEnvironmentProcess(string Id, List<string> Command, int? Port = null, string? HealthPath = null);
