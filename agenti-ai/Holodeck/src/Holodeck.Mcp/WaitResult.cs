using Holodeck.Web.Rooms;

namespace Holodeck.Mcp;

// Arrived is false when the wait ran out. LastId is the id to wait or read after next.
public sealed record WaitResult(bool Arrived, long LastId, List<Packet> Messages);
