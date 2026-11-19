namespace CreativeStudio;

public sealed record WorkflowDefinition(string Id, string Name, string Description, string Family, string GraphFile, string License, WorkflowBindings Bindings, WorkflowSize Size, bool SupportsReferences, List<string> RequiredNodes, List<WorkflowArtifact> Artifacts)
{
  public int DefaultWidth => Size.DefaultWidth;
  public int DefaultHeight => Size.DefaultHeight;
}
