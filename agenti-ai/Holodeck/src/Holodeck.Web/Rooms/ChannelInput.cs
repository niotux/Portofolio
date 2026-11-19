namespace Holodeck.Web.Rooms;

// A partial update: only the fields given change.
public sealed record ChannelInput(string? Name, bool? IsPaused);
