using System.Text;

namespace Humans;

// Person's separate formation client/ordered chain, backed by a durable queue. An exchange is dissected into a
// first-person prose memory before it is embedded. Raw packets are never embedded or searched.
public sealed class MemoryKeeper(ChatModel background, Embedder embedder, MemoryStore store, Page page, RuntimeOptions options, TextWriter log, Holodeck? transport = null)
{
  public async Task RunAsync(CancellationToken token)
  {
    while (!token.IsCancellationRequested)
    {
      try
      {
        await FormNextAsync(token);
      }
      catch (Exception exception) when (exception is HttpRequestException || exception is IOException || (exception is OperationCanceledException && !token.IsCancellationRequested))
      {
        log.WriteLine("Memory formation is waiting for its services; its exchange and any drafted prose are retained.");
        await Task.Delay(TimeSpan.FromSeconds(options.PollSeconds), token);
      }
    }
  }

  private async Task FormNextAsync(CancellationToken token)
  {
    Formation? form = await store.ClaimFormationAsync(options.FormationAttempts, token);
    if (form is null)
    {
      await Task.Delay(TimeSpan.FromSeconds(options.PollSeconds), token);
      return;
    }
    Delivery source = new(form.PacketId, form.Channel, "", "", "", "", new List<string>());
    await using ActivityReporter? activity = transport is null ? null : new ActivityReporter(transport, source);
    try
    {
      activity?.Begin("formation", "Forming a memory");
      string memory;
      if (form.Draft is null)
      {
        StringBuilder input = new();
        if (form.Earlier.Length > 0)
        {
          input.Append("Earlier in the chat:\n").Append(form.Earlier).Append("\n\n");
        }
        input.Append("The exchange:\n").Append(form.Exchange);
        Action<string>? onText = activity is null ? null : activity.Append;
        // Remembering what happened is a utility job: instructed and grounded, not the person speaking.
        Completion result = await background.SayAsync([new Message("system", page.Instruction("form")), new Message("user", input.ToString())], token, onText, options.UtilityTemperature);
        memory = result.Text.Trim();
        await store.SaveDraftAsync(form.PacketId, memory, token);
      }
      else
      {
        memory = form.Draft;
        activity?.Append(memory);
      }
      activity?.End();
      activity?.Begin("embedding", "Keeping memory");
      float[]? vector = string.IsNullOrWhiteSpace(memory) ? null : await embedder.EmbedAsync(memory, false, token);
      await store.KeepAsync(form, memory, vector, embedder.Tag, token);
      activity?.End();
      activity?.Finish(true);
      log.WriteLine(vector is null ? "No memory formed for the exchange." : "A prose memory was formed and embedded.");
    }
    catch (OperationCanceledException) when (token.IsCancellationRequested)
    {
      throw;
    }
    catch (Exception exception) when (exception is HttpRequestException || exception is IOException || exception is OperationCanceledException)
    {
      activity?.Finish(false);
      await store.FormationFailedAsync(form, options.FormationAttempts, token);
      throw;
    }
  }
}
