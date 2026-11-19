namespace CreativeStudio;

public sealed record SendRequest(string Channel, string Note, List<string> Files, string IdempotencyKey);
