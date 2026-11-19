namespace Holodeck.Web.Computers;

internal sealed record LibraryEnvironmentProcessConfig(string Id, List<string> Command, int? Port, string? HealthPath, string ServiceToken);
