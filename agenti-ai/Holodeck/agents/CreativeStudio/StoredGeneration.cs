namespace CreativeStudio;

public sealed record StoredGeneration(string Id, long PacketId, string Channel, string RequestJson, string State, string? ExternalId, string? Error);
