namespace CreativeStudio;

// Selects whole messages, counts prose separately from image payload bytes, and never injects the notebook wholesale.
public sealed class ContextBuilder(ContextOptions options)
{
  public List<ChatMessage> Build(string systemPrompt, List<ChatMessage> recent, ChatMessage current, List<ChatMessage>? retrieved = null, int reservedCharacters = 0)
  {
    int available = options.MaxCharacters - reservedCharacters;
    int required = systemPrompt.Length + current.TextContent.Length;
    if (required > available || current.ImageCount > options.MaxImages)
    {
      throw new ContextLimitException("The current request exceeds the configured context allowance.");
    }

    List<ChatMessage> notes = SelectNotes(retrieved, available - required, options.MaxImages - current.ImageCount);
    int noteCharacters = CountCharacters(notes);
    int noteImages = CountImages(notes);
    List<ChatMessage> history = SelectHistory(recent, available - required - noteCharacters);
    List<ChatMessage> messages = new() { ChatMessage.Text("system", systemPrompt) };
    foreach (ChatMessage note in notes)
    {
      messages.Add(note);
    }
    foreach (ChatMessage message in history)
    {
      messages.Add(message);
    }
    messages.Add(current);
    if (noteImages + current.ImageCount > options.MaxImages)
    {
      throw new ContextLimitException("The selected references exceed the configured vision allowance.");
    }
    return messages;
  }

  public static int CountCharacters(List<ChatMessage> messages)
  {
    int count = 0;
    foreach (ChatMessage message in messages)
    {
      count += message.TextContent.Length;
    }
    return count;
  }

  private List<ChatMessage> SelectNotes(List<ChatMessage>? retrieved, int available, int images)
  {
    List<ChatMessage> selected = new();
    if (retrieved is null)
    {
      return selected;
    }
    int remaining = Math.Min(options.MaxNoteCharacters, available);
    for (int index = retrieved.Count - 1; index >= 0; index--)
    {
      ChatMessage note = retrieved[index];
      if (note.TextContent.Length > remaining || note.ImageCount > images)
      {
        continue;
      }
      selected.Add(note);
      remaining -= note.TextContent.Length;
      images -= note.ImageCount;
    }
    selected.Reverse();
    return selected;
  }

  private List<ChatMessage> SelectHistory(List<ChatMessage> recent, int available)
  {
    List<ChatMessage> selected = new();
    for (int index = recent.Count - 1; index >= 0 && selected.Count < options.RecentMessages; index--)
    {
      ChatMessage message = recent[index];
      if (message.ImageCount > 0)
      {
        continue;
      }
      if (message.TextContent.Length > available)
      {
        break;
      }
      selected.Add(message);
      available -= message.TextContent.Length;
    }
    selected.Reverse();
    if (selected.Count > 0 && selected[0].Role == "assistant")
    {
      selected.RemoveAt(0);
    }
    return selected;
  }

  private static int CountImages(List<ChatMessage> messages)
  {
    int count = 0;
    foreach (ChatMessage message in messages)
    {
      count += message.ImageCount;
    }
    return count;
  }
}
