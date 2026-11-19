namespace CreativeStudio;

public sealed record NotebookWrite(string Channel, string Title, string Prose, List<string> Files, float[] Vector, string EmbeddingTag, long SourcePacketId, string? NoteId);
