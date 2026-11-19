namespace Holodeck.Web.Rooms;

internal sealed record StoredPacket(Packet Packet, List<string> RecipientIds, bool IsReplay);
