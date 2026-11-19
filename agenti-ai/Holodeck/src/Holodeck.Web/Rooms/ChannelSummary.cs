namespace Holodeck.Web.Rooms;

public sealed record ChannelSummary(string Id, string Name, bool IsLegacy, bool IsPaused, List<string> MemberIds, int PacketCount, string? LastNote, string? LastSenderId, DateTimeOffset? LastAt);
