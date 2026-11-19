using System.Text.Json;

namespace Holodeck.Web.AgentData;

// One statement with positional parameters ($1, $2, ...).
public sealed record SqlStatement(string Sql, List<JsonElement>? Parameters);
