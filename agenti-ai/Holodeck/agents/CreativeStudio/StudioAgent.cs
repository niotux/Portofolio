namespace CreativeStudio;

public sealed class StudioAgent(HolodeckClient transport, CreativeStore store, PromptRegistry prompts, StudioConversation conversation, GenerationWorker generationWorker, PromptActions actions, RuntimeOptions options)
{
  private readonly StudioInbox inbox = new(transport, store, options.PollSeconds);

  public async Task RunAsync(CancellationToken token)
  {
    while (!token.IsCancellationRequested)
    {
      try
      {
        await FlushAsync(token);
        await actions.ProcessAsync(token);
        StoredGeneration? generation = await store.PendingGenerationAsync(token);
        if (generation is not null)
        {
          await generationWorker.RunAsync(generation, token);
          continue;
        }

        StoredReceipt? receipt = await inbox.NextAsync(token);
        if (receipt is null)
        {
          continue;
        }

        try
        {
          await conversation.HandleAsync(receipt, token);
        }
        catch (ContextLimitException)
        {
          string message = (await prompts.GetAsync("context-limit", token)).Content;
          await store.CompleteTurnAsync(receipt, message.Trim(), new List<string>(), token);
        }
      }
      catch (Exception exception) when (exception is HttpRequestException || exception is IOException || (exception is OperationCanceledException && !token.IsCancellationRequested))
      {
        Console.WriteLine("A service is unavailable; retained work will resume. " + exception.Message);
        await Task.Delay(TimeSpan.FromSeconds(options.PollSeconds), token);
      }
    }
  }

  private async Task FlushAsync(CancellationToken token)
  {
    StoredSend? send;
    while ((send = await store.NextSendAsync(token)) is not null)
    {
      await transport.SendAsync(send, token);
      await store.SentAsync(send.Key, token);
    }
  }
}
