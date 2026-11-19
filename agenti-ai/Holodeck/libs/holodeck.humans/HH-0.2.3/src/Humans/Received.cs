namespace Humans;

public sealed record Received(long PacketId, string ChannelId, string Sender, string Note, DateTimeOffset At);
