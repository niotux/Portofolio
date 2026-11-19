namespace Humans;

public sealed record SendBody(string Channel, string Note, string IdempotencyKey);
