using System.Text.Json;

namespace Holodeck.Web.Management;

public sealed record ManagementCompletion(string? AttemptToken, JsonElement Result);
