namespace CreativeStudio;

public sealed record IncomingMessage(long PacketId, string ChannelId, string SenderId, string From, string Note, List<string> Files);
