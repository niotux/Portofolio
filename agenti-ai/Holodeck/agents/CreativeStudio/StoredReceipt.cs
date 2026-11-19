namespace CreativeStudio;

public sealed record StoredReceipt(long PacketId, string ChannelId, string SenderId, string From, string Note, List<string> Files);
