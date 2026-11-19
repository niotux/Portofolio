namespace Holodeck.Web.Rooms;

public sealed record RoomView(List<RoomMemberView> Members, List<ChannelSummary> Channels);
