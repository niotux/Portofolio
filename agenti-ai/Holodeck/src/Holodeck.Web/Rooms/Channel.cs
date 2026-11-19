namespace Holodeck.Web.Rooms;

// A conversation among the members listed in MemberIds. Legacy channels were two-party wires before channels existed.
public sealed record Channel(string Id, string ProjectId, string Name, bool IsLegacy, bool IsPaused, List<string> MemberIds);
