namespace Humans;

public sealed record PendingSend(long PacketId, string Channel, string Text, string Key);
