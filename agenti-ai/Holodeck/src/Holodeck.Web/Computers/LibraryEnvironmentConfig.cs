namespace Holodeck.Web.Computers;

internal sealed record LibraryEnvironmentConfig(string Fingerprint, string ControlToken, List<LibraryEnvironmentProcessConfig> Processes);
