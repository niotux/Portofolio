namespace Holodeck.Web.Rooms;

// What an agent receives: in which channel, who wrote, what they said, and where the files now are on its own table.
public sealed record Delivery(string ChannelId, string Channel, string From, string Note, List<string> Files, long PacketId, string SenderId, DateTimeOffset SentAt);
