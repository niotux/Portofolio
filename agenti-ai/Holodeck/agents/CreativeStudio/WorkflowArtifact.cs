namespace CreativeStudio;

public sealed record WorkflowArtifact(string Path, string Repository, string Revision, string Sha256, long Bytes, string Url);
