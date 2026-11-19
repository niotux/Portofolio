namespace Holodeck.Web.Computers;

public sealed record LibraryEnvironmentAgentState(string Key, bool Running, string StartedAt, int? ExitCode);
