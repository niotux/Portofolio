namespace Holodeck.Web.Computers;

public sealed record AgentComputerState(string Key, bool Running, string StartedAt = "");
