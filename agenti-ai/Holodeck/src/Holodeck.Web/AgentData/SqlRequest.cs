namespace Holodeck.Web.AgentData;

// Statements run in order inside one transaction: all of them commit, or none do.
public sealed record SqlRequest(List<SqlStatement> Statements);
