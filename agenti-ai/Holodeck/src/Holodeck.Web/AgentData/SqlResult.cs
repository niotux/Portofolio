namespace Holodeck.Web.AgentData;

public sealed record SqlResult(List<string> Columns, List<List<object?>> Rows, int Affected);
