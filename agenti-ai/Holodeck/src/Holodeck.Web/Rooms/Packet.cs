namespace Holodeck.Web.Rooms;

public sealed record Packet(long Id, string ChannelId, string SenderId, string Note, List<PacketFile> Files, DateTimeOffset SentAt);
