namespace Humans;

public sealed record PendingSend(long Id, string Channel, string Text, string Key);
