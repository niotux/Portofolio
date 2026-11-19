namespace Humans;

// A separate formation chain over a durable queue: a stretch of my history becomes first-person prose memories, which
// are embedded. Raw messages and history entries are never embedded or searched.
public sealed class MemoryKeeper(ChatModel background, Embedder embedder, Store store, Page page, RuntimeOptions options, LivePrompts prompts, string workspace)
{
  // A stretch can hold a few separate things worth remembering; more is a summary, not memory.
  private const int MostPerStretch = 3;

  public async Task RunAsync(CancellationToken token)
  {
    while (!token.IsCancellationRequested)
    {
      try
      {
        await FormNextAsync(token);
      }
      catch (Exception exception) when (exception is HttpRequestException || exception is IOException || exception is ArgumentException || (exception is OperationCanceledException && !token.IsCancellationRequested))
      {
        Console.WriteLine("Memory formation is waiting for its services; its stretch and any drafted prose are retained. " + exception.Message);
        await Task.Delay(TimeSpan.FromSeconds(options.PollSeconds * 5), token);
      }
    }
  }

  private async Task FormNextAsync(CancellationToken token)
  {
    Formation? form = await store.ClaimFormationAsync(options.FormationAttempts, token);
    if (form is null)
    {
      await Task.Delay(TimeSpan.FromSeconds(options.PollSeconds * 2), token);
      return;
    }
    try
    {
      string memory;
      if (form.Draft is null)
      {
        page.Use(await prompts.ReadAsync(token));
        LivePrompts.Record(workspace, "last-memory-prompts.json", page.Live!);
        // Remembering what happened is a utility job: instructed and grounded, not the person speaking.
        Completion result = await background.SayAsync([new Message("system", page.Instruction("form.txt")), new Message("user", form.Text)], token, null, options.UtilityTemperature);
        memory = result.Text.Trim();
        await store.SaveDraftAsync(form.Id, memory, token);
      }
      else
      {
        memory = form.Draft;
      }
      List<string> texts = Paragraphs(memory);
      List<float[]> vectors = new();
      foreach (string text in texts)
      {
        vectors.Add(await embedder.EmbedAsync(text, false, token));
      }
      await store.KeepAsync(form, texts, vectors, embedder.Tag, token);
      Console.WriteLine(texts.Count == 0 ? "Nothing from that stretch was worth keeping as memory." : texts.Count + " prose memories were formed and embedded.");
    }
    catch (OperationCanceledException) when (token.IsCancellationRequested)
    {
      throw;
    }
    catch (Exception exception) when (exception is HttpRequestException || exception is IOException || exception is OperationCanceledException)
    {
      await store.FormationFailedAsync(form, options.FormationAttempts, token);
      throw;
    }
  }

  // Separate memories are separate paragraphs.
  public static List<string> Paragraphs(string text)
  {
    List<string> paragraphs = new();
    ReadOnlySpan<char> rest = text.AsSpan();
    while (rest.Length > 0 && paragraphs.Count < MostPerStretch)
    {
      int end = rest.IndexOf("\n\n".AsSpan(), StringComparison.Ordinal);
      ReadOnlySpan<char> paragraph = (end < 0 ? rest : rest[..end]).Trim();
      if (paragraph.Length > 0)
      {
        paragraphs.Add(paragraph.ToString());
      }
      rest = end < 0 ? ReadOnlySpan<char>.Empty : rest[(end + 2)..];
    }
    return paragraphs;
  }
}
