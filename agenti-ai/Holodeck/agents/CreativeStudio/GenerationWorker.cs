using System.Diagnostics;
using System.Text.Json;

namespace CreativeStudio;

// This worker never calls the LLM. A saved request is sufficient for rendering, recovery and delivery.
public sealed class GenerationWorker(CreativeStore store, ComfyClient comfy, HolodeckClient transport, RuntimeOptions options, ComfyOptions connection)
{
  private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

  public async Task RunAsync(StoredGeneration generation, CancellationToken token)
  {
    PreparedGeneration prepared = JsonSerializer.Deserialize<PreparedGeneration>(generation.RequestJson, Json) ?? throw new IOException("The saved image request is empty.");
    GenerationPlan plan = prepared.Plan;
    StoredReceipt receipt = new(generation.PacketId, generation.Channel, "", "", "", new List<string>());
    await using ActivityReporter activity = new(transport, receipt);
    activity.Begin("render", "Generating image");
    if (generation.State == "prepared")
    {
      ComfyJobState existing = await comfy.FindAsync(plan.PromptId, token);
      await store.UpdateGenerationAsync(generation.Id, existing.Status == "missing" ? "submitting" : "submitted", plan.PromptId, null, token);
      try
      {
        if (existing.Status == "missing")
        {
          await comfy.SubmitAsync(plan, token);
        }

        await store.UpdateGenerationAsync(generation.Id, "submitted", plan.PromptId, null, token);
      }
      catch (ComfySubmissionRejectedException exception)
      {
        string message = Prompt(prepared, "failed");
        await store.FailGenerationAsync(generation.Id, message, exception.Message, token);
        activity.Finish(false);
        return;
      }
      catch (Exception exception) when (exception is HttpRequestException || exception is IOException || (exception is OperationCanceledException && !token.IsCancellationRequested))
      {
        await store.UpdateGenerationAsync(generation.Id, "uncertain", plan.PromptId, exception.Message, token);
      }
    }

    Stopwatch elapsed = Stopwatch.StartNew();
    Stopwatch missing = Stopwatch.StartNew();
    while (!token.IsCancellationRequested)
    {
      ComfyJobState state = await comfy.FindAsync(plan.PromptId, token);
      if (state.Status == "completed")
      {
        GenerationResult result = await comfy.CollectAsync(plan, token);
        string message = Prompt(prepared, "delivery");
        await store.CompleteGenerationAsync(generation.Id, message.Trim(), result.Files, token);
        activity.Finish(true);
        return;
      }

      if (state.Status == "failed")
      {
        string message = Prompt(prepared, "failed");
        await store.FailGenerationAsync(generation.Id, message.Trim(), state.Error ?? "ComfyUI reported an execution failure.", token);
        activity.Finish(false);
        return;
      }

      if (state.Status != "missing")
      {
        missing.Restart();
      }

      bool absent = state.Status == "missing" && missing.Elapsed.TotalSeconds >= options.ReconcileSeconds;
      bool overdue = elapsed.Elapsed.TotalSeconds >= options.RenderWaitSeconds;
      if (absent || overdue)
      {
        string message = Prompt(prepared, "uncertain");
        await store.ReviewGenerationAsync(generation.Id, message.Trim(), state.Error ?? "The saved request needs reconciliation with ComfyUI before any retry.", token);
        activity.Finish(false);
        return;
      }

      await Task.Delay(TimeSpan.FromSeconds(connection.PollSeconds), token);
    }
  }

  private static string Prompt(PreparedGeneration prepared, string purpose)
  {
    foreach (PreparedPrompt prompt in prepared.Prompts)
    {
      if (prompt.Purpose == purpose)
      {
        return prompt.Content;
      }
    }

    throw new IOException("The saved generation has no " + purpose + " prompt snapshot.");
  }
}
