using System.Text.Json;

namespace Holodeck.Web.Management;

public sealed record ManagementJob(string Id, string ActionId, string RequestId, string DefinitionRevision, string SettingsRevision, string Status, JsonElement Input, JsonElement? Result, int? Progress, string? Message, string? Error, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, ManagementAction Action);
