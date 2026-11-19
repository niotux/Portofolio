namespace Holodeck.Web.Rooms;

public sealed record RoomMemberView(string Id, string Name, bool IsYou, string? Library, bool IsRemoved, string Status, string? Error = null);
