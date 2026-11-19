namespace Holodeck.Web.Activity;

// Agent-authored transport metadata around opaque prose. A model never fills this object.
public sealed record ActivityUpdate(string Id, string ChannelId, long? PacketId, int Ordinal, string Phase, string Label, string Text, bool IsTyping, bool IsComplete, string State);
