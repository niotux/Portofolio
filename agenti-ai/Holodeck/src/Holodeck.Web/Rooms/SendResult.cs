namespace Holodeck.Web.Rooms;

// IsReplay means the packet was committed by an earlier request with the same idempotency key.
public sealed record SendResult(Packet Packet, bool IsReplay);
