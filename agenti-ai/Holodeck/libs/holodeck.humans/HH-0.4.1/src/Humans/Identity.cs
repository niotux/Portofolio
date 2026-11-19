namespace Humans;

public sealed record Identity(string Name, string MemberId, List<AgentChannel> Channels, List<string>? Capabilities);
