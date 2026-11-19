namespace Humans;

public sealed record Formation(long PacketId, string Channel, int Position, string Earlier, string Thought, string Exchange, string? Draft, int Attempts);
