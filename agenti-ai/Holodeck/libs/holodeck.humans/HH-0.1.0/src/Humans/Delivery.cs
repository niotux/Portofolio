namespace Humans;

public sealed record Delivery(long PacketId, string ChannelId, string Channel, string SenderId, string From, string Note, List<string> Files);
