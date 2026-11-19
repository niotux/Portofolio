namespace Holodeck.Web.Computers;

// A declared process as its environment's supervisor reports it.
public sealed record LibraryEnvironmentProcessState(string Id, int Uid, bool Running, int? ExitCode, string StartedAt = "", bool Sudo = false);
