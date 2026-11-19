namespace CreativeStudio;

public sealed record StoredSend(string Key, string Channel, string Text, List<string> Files);
