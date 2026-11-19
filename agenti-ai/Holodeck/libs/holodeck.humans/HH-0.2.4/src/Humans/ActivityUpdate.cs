namespace Humans;

// Constructed by application code. The model produces only Text's prose, never this transport envelope.
public sealed record ActivityUpdate(string Id, string ChannelId, long? PacketId, int Ordinal, string Phase, string Label, string Text, bool IsTyping, bool IsComplete, string State);
