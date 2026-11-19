using System.Text.Json;

namespace Holodeck.Web.Management;

public sealed record ManagementEnqueue(string? ActionId, string? RequestId, string? DefinitionRevision, JsonElement Input);
