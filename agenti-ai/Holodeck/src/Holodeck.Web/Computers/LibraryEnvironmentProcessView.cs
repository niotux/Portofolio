namespace Holodeck.Web.Computers;

public sealed record LibraryEnvironmentProcessView(string Id, int Uid, int? Port, string ServiceToken, bool Running, int? ExitCode, string StartedAt = "");
