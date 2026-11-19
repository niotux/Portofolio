namespace CreativeStudio;

public sealed record NotebookNote(string Id, string Channel, string Title, string Prose, List<string> Files, long SourcePacketId, string Revision);
