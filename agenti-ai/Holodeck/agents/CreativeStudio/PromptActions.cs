using System.Text;
using System.Text.Json;

namespace CreativeStudio;

public sealed class PromptActions(HolodeckClient transport, PromptRegistry prompts, CreativeStore store, ComfyClient comfy, int maximumResults)
{
  private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
  private string claimId = Guid.NewGuid().ToString("N");

  public async Task ProcessAsync(CancellationToken token)
  {
    ManagementClaim? claim = await transport.ClaimAsync(claimId, token);
    if (claim is null)
    {
      return;
    }

    try
    {
      PromptActionInput input = claim.Job.Input.Deserialize<PromptActionInput>(Json) ?? throw new ArgumentException("The action input is empty.");
      PromptActionResult result = await ExecuteAsync(claim.Job.ActionId, input, token);
      await transport.CompleteAsync(claim, result, token);
    }
    catch (Exception exception) when (exception is ArgumentException || exception is KeyNotFoundException || exception is InvalidOperationException || exception is JsonException)
    {
      await transport.FailAsync(claim, exception.Message, token);
    }

    claimId = Guid.NewGuid().ToString("N");
  }

  private async Task<PromptActionResult> ExecuteAsync(string action, PromptActionInput input, CancellationToken token)
  {
    string purpose = input.Purpose ?? "";
    switch (action)
    {
      case "read-prompt":
        return Result(await prompts.GetAsync(purpose, token), "This is the active prompt.");
      case "save-prompt":
        return Result(await prompts.SaveAsync(purpose, input.Content ?? "", token), "Draft saved. Publish its revision to use it in future work.");
      case "select-prompt":
        await prompts.SelectAsync(purpose, input.Revision ?? "", token);
        return Result(await prompts.GetAsync(purpose, token), "This revision is selected for future work.");
      case "list-prompts":
        return await ListPromptsAsync(token);
      case "list-generations":
        return await ListGenerationsAsync(token);
      case "retry-generation":
        return await RetryAsync(input.Request ?? "", token);
      default:
        throw new ArgumentException("That management action is not available.");
    }
  }

  private async Task<PromptActionResult> ListPromptsAsync(CancellationToken token)
  {
    StringBuilder text = new();
    foreach (PromptDefinition definition in prompts.Definitions.Values)
    {
      text.AppendLine(definition.Id);
      foreach (PromptRevision revision in await prompts.VersionsAsync(definition.Id, token))
      {
        text.Append(revision.Active ? "Active: " : "Revision: ").AppendLine(revision.Revision);
      }
    }

    return new PromptActionResult("", "", text.ToString(), "Registered purposes and saved revisions.");
  }

  private async Task<PromptActionResult> ListGenerationsAsync(CancellationToken token)
  {
    StringBuilder text = new();
    foreach (StoredGeneration generation in await store.ListGenerationsAsync(maximumResults, token))
    {
      text.Append("Request: ").AppendLine(generation.Id).Append("State: ").AppendLine(generation.State);
      if (generation.Error is not null)
      {
        text.Append("Detail: ").AppendLine(generation.Error);
      }
    }

    return new PromptActionResult("", "", text.ToString(), "Saved image requests. Retried requests retain their original workflow and prompt snapshot.");
  }

  private async Task<PromptActionResult> RetryAsync(string id, CancellationToken token)
  {
    StoredGeneration generation = await store.GenerationAsync(id, token) ?? throw new KeyNotFoundException("That image request does not exist.");
    if (generation.State != "needs-review")
    {
      throw new InvalidOperationException("Only a request waiting for review can be explicitly retried.");
    }

    PreparedGeneration request = JsonSerializer.Deserialize<PreparedGeneration>(generation.RequestJson, Json) ?? throw new IOException("The saved image request is empty.");
    ComfyJobState state = await comfy.FindAsync(request.Plan.PromptId, token);
    await store.ResumeGenerationAsync(id, token);
    if (state.Status == "missing")
    {
      await store.UpdateGenerationAsync(id, "prepared", request.Plan.PromptId, null, token);
    }

    return new PromptActionResult("", id, "", "The saved request is ready to resume. Existing ComfyUI work will be collected before any new submission.");
  }

  private static PromptActionResult Result(PromptRevision prompt, string message)
  {
    return new PromptActionResult(prompt.Id, prompt.Revision, prompt.Content, message);
  }
}
