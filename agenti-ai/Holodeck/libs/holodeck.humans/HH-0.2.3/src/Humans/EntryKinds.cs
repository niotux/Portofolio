namespace Humans;

// Code's names for kinds of history entry. The model never writes or reads them; the page renders each as prose.
public static class EntryKinds
{
  public const string Thought = "thought";
  public const string Recollection = "recollection";
  public const string Message = "message";
  public const string Said = "said";
  // Careful working-out the person did on purpose, written as their own reasoning.
  public const string Worked = "worked";
  // Narration of what the body did or met, already written as prose from the page's labels.
  public const string Event = "event";
}
