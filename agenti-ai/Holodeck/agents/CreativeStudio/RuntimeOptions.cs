namespace CreativeStudio;

public sealed class RuntimeOptions
{
  public required int PollSeconds { get; init; }
  public required int DatabaseTimeoutSeconds { get; init; }
  public required int MaxToolRounds { get; init; }
  public required int SearchResults { get; init; }
  public required int MaxPromptCharacters { get; init; }
  public required int MaxToolResultCharacters { get; init; }
  public required int MaxNotebookCharacters { get; init; }
  public required int RenderWaitSeconds { get; init; }
  public required int ReconcileSeconds { get; init; }
  public required int KeepRecentMessages { get; init; }
  public required int OutputReserveCharacters { get; init; }
  public required int MaxReferenceEdge { get; init; }
  public required int PreviewTimeoutSeconds { get; init; }
  public required int ReferenceDescriptionCharacters { get; init; }
  public required ContextOptions Context { get; init; }
}
