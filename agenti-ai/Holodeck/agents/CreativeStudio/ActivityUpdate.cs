namespace CreativeStudio;

public sealed record ActivityUpdate(string Id, string ChannelId, long? PacketId, int Ordinal, string Phase, string Label, string Text, bool IsTyping, bool IsComplete, string State);
