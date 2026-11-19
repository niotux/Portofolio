namespace Holodeck.Web.Rooms;

public sealed record RoomMember(string Id, string ProjectId, string Name, bool IsYou, string? Library, bool IsRemoved);
