namespace CreativeStudio;

public sealed record AgentIdentity(string Name, string MemberId, List<string> Capabilities, List<AgentChannel> Channels);
