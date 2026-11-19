namespace Humans;

public sealed record Unread(string ChannelId, int Count, List<string> Senders);
