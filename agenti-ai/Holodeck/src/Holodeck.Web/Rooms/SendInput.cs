namespace Holodeck.Web.Rooms;

// Channel is a channel id or name. To is the legacy form: the name of the one member to talk to. IdempotencyKey is
// optional; the sender chooses it, and a retry with the same key and content reports the send it already made.
public sealed record SendInput(string? Channel, string? To, string? Note, List<string>? Files, string? IdempotencyKey = null);
