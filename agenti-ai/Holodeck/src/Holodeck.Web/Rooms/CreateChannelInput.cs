namespace Holodeck.Web.Rooms;

public sealed record CreateChannelInput(string? Name, List<string>? MemberIds);
