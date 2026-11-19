namespace Holodeck.Web.Activity;

public sealed record ActivityRun(string Id, string MemberId, long? PacketId, string State, bool Active, bool IsTyping, DateTimeOffset StartedAt, DateTimeOffset UpdatedAt, List<ActivityStep> Steps);
