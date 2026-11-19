using System.Text.Json;

namespace CreativeStudio;

public sealed record ManagementJob(string Id, string ActionId, JsonElement Input);
