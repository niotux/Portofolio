using System.Text;
using System.Text.Json;

namespace CreativeStudio;

public sealed class StudioConversation(CreativeModel model, CreativeStore store, PromptRegistry registry, StudioTools instruments, StudioToolCatalog catalog, WorkflowCatalog workflows, RuntimeOptions options, HolodeckClient transport)
{
  private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
  private readonly ContextBuilder context = new(options.Context);

  public async Task HandleAsync(StoredReceipt receipt, CancellationToken token)
  {
    PromptSession prompts = new(registry);
    await CompactAsync(receipt.ChannelId, prompts, token);
    StudioTurn turn = new(receipt);
    List<StoredReference> references = await store.RecentReferencesAsync(receipt.ChannelId, options.SearchResults, options.ReferenceDescriptionCharacters, token);
    ChatMessage current = ChatMessage.Text("user", turn.CurrentMessage(references));
    List<ChatMessage> recent = await HistoryAsync(receipt.ChannelId, token);
    string system = await SystemAsync(receipt.ChannelId, prompts, token);
    List<ToolDefinition> controls = await catalog.ReadAsync(prompts, token);
    int controlsSize = JsonSerializer.Serialize(controls, Json).Length;
    string hands = await prompts.ReadAsync("hands", token);
    string reply = "";
    await using ActivityReporter activity = new(transport, receipt);
    activity.Begin("attention", "Considering the request");
    for (int round = 0; round < options.MaxToolRounds; round++)
    {
      string continuation = round > 0 ? await prompts.ReadAsync("continue-tools", token) : "";
      int reserved = controlsSize + options.OutputReserveCharacters + continuation.Length;
      List<ChatMessage> attention = context.Build(system + "\n\n" + hands, recent, current, turn.Experiences, reserved);
      if (continuation.Length > 0)
      {
        attention.Add(ChatMessage.Text("user", continuation));
      }

      ModelAction action = await model.ActAsync(attention, controls, token);
      Console.WriteLine($"Creative attention used {action.Usage.PromptTokens} input and {action.Usage.CompletionTokens} output tokens.");
      if (action.Calls.Count == 0)
      {
        reply = action.Text.Trim();
        break;
      }

      foreach (ToolCall call in action.Calls)
      {
        try
        {
          await instruments.ApplyAsync(call, turn, token);
          Console.WriteLine("Completed instrument operation: " + call.Name);
        }
        catch (Exception exception) when (exception is ArgumentException || exception is JsonException || exception is KeyNotFoundException)
        {
          Console.WriteLine("Instrument controls rejected: " + call.Name + ": " + exception.Message);
          turn.Experiences.Add(ChatMessage.Text("user", "That operation did not happen: " + exception.Message));
        }

        bool needsObservation = call.Name == "search_notebook" || call.Name == "open_reference";
        if (turn.Generation is not null || needsObservation)
        {
          // A following operation must be based on the observed result, not a guess made before reading it.
          break;
        }
      }

      if (turn.Generation is not null)
      {
        break;
      }
    }

    activity.End();
    if (reply.Length == 0)
    {
      activity.Begin("reply", "Writing", true);
      if (turn.Generation is null)
      {
        turn.Experiences.Add(ChatMessage.Text("user", "No image generation request has been prepared in this exchange."));
      }

      string instruction = await prompts.ReadAsync("reply", token);
      List<ChatMessage> speaking = context.Build(system + "\n\n" + instruction, recent, current, turn.Experiences, options.OutputReserveCharacters);
      ModelCompletion completion = await model.SayAsync(speaking, token, activity.Append);
      reply = completion.Text.Trim();
      Console.WriteLine($"Creative reply used {completion.Usage.PromptTokens} input and {completion.Usage.CompletionTokens} output tokens.");
      activity.End();
    }

    if (turn.Generation is null)
    {
      await store.CompleteTurnAsync(receipt, reply, new List<string>(), token);
    }
    else
    {
      await prompts.ReadAsync("delivery", token);
      await prompts.ReadAsync("failed", token);
      await prompts.ReadAsync("uncertain", token);
      PreparedGeneration request = new(turn.Generation, prompts.Snapshot(), turn.InspectedFiles, receipt.Note);
      StoredGeneration generation = new(turn.Generation.PromptId, receipt.PacketId, receipt.ChannelId, JsonSerializer.Serialize(request, Json), "prepared", null, null);
      await store.ScheduleGenerationAsync(generation, receipt, reply, token);
    }

    activity.Finish(true);
  }

  private async Task<List<ChatMessage>> HistoryAsync(string channel, CancellationToken token)
  {
    List<StoredTurn> stored = await store.RecentTurnsAsync(channel, options.Context.RecentMessages, token);
    List<ChatMessage> history = new();
    foreach (StoredTurn turn in stored)
    {
      history.Add(ChatMessage.Text(turn.Role, turn.Text));
    }

    return history;
  }

  private async Task<string> SystemAsync(string channel, PromptSession prompts, CancellationToken token)
  {
    StringBuilder text = new(await prompts.ReadAsync("conversation", token));
    string brief = await store.BriefAsync(channel, token);
    if (brief.Length > 0)
    {
      text.Append("\n\nThe working brief from our earlier conversation:\n").Append(brief);
    }

    text.Append("\n\nAvailable image instruments:\n");
    foreach (WorkflowDefinition workflow in workflows.List())
    {
      text.Append(workflow.Id).Append(": ").Append(workflow.Name).Append(". ").Append(workflow.Description).Append('\n');
    }

    return text.ToString();
  }

  private async Task CompactAsync(string channel, PromptSession prompts, CancellationToken token)
  {
    List<StoredTurn> turns = await store.OldestTurnsAsync(channel, options.Context.RecentMessages, token);
    if (turns.Count < options.Context.RecentMessages)
    {
      return;
    }

    int count = turns.Count - options.KeepRecentMessages;
    StringBuilder text = new(await store.BriefAsync(channel, token));
    long through = 0;
    for (int index = 0; index < count; index++)
    {
      StoredTurn turn = turns[index];
      text.Append('\n').Append(turn.Role == "user" ? "They: " : "I: ").Append(turn.Text);
      through = turn.Position;
    }

    string instruction = await prompts.ReadAsync("compact", token);
    List<ChatMessage> compacting = context.Build(instruction, new List<ChatMessage>(), ChatMessage.Text("user", text.ToString()), reservedCharacters: options.OutputReserveCharacters);
    ModelCompletion summary = await model.SayAsync(compacting, token);
    if (string.IsNullOrWhiteSpace(summary.Text))
    {
      throw new IOException("The working brief was empty; original conversation was retained.");
    }

    await store.SaveBriefAsync(channel, summary.Text.Trim(), through, token);
  }
}
