using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace CreativeStudio;

// Submission is deliberately separate from reconciliation: a lost response must never trigger a blind POST retry.
public sealed class ComfyClient(HttpClient http, ComfyOptions options, string workspace)
{
  private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
  private const int QueuePromptIndex = 1;
  private const int ErrorMessageLimit = 1000;

  public async Task<ComfyJobState> FindAsync(string promptId, CancellationToken token)
  {
    WorkflowCatalog.RequirePromptId(promptId);
    ComfyHistory? history = await HistoryAsync(promptId, token);
    if (history is not null)
    {
      if (history.Status.Status == "error")
      {
        return new ComfyJobState("failed", ExecutionError(history.Status));
      }
      if (history.Status.Completed)
      {
        return new ComfyJobState("completed", null);
      }
    }

    using HttpResponseMessage response = await GetAsync("queue", token);
    ComfyQueue queue = await response.Content.ReadFromJsonAsync<ComfyQueue>(Json, token) ?? throw new IOException("ComfyUI returned no queue.");
    if (Contains(queue.Running, promptId))
    {
      return new ComfyJobState("running", null);
    }
    if (Contains(queue.Pending, promptId))
    {
      return new ComfyJobState("queued", null);
    }

    // It may finish between the first history lookup and the queue lookup.
    history = await HistoryAsync(promptId, token);
    if (history is not null)
    {
      if (history.Status.Status == "error")
      {
        return new ComfyJobState("failed", ExecutionError(history.Status));
      }
      if (history.Status.Completed)
      {
        return new ComfyJobState("completed", null);
      }
    }

    return new ComfyJobState("missing", null);
  }

  public async Task SubmitAsync(GenerationPlan plan, CancellationToken token)
  {
    WorkflowCatalog.RequirePromptId(plan.PromptId);
    string hash = WorkflowHash.Compute(plan.Graph);
    if (hash != plan.GraphHash)
    {
      throw new ArgumentException("The saved generation graph no longer matches its snapshot.");
    }

    using CancellationTokenSource timeout = Timeout(token);
    ComfyPromptRequest body = new(plan.Graph, plan.PromptId, plan.PromptId);
    using HttpRequestMessage request = new(HttpMethod.Post, Address("prompt")) { Content = JsonContent.Create(body, options: Json) };
    using HttpResponseMessage response = await http.SendAsync(request, timeout.Token);
    if (response.StatusCode == HttpStatusCode.BadRequest)
    {
      throw new ComfySubmissionRejectedException("ComfyUI rejected the workflow: " + await RejectionAsync(response, timeout.Token));
    }

    await RequireAsync(response, "submit the generation", timeout.Token);
    ComfyPromptResponse submitted = await response.Content.ReadFromJsonAsync<ComfyPromptResponse>(Json, timeout.Token) ?? throw new IOException("ComfyUI returned no generation identity.");
    if (submitted.PromptId != plan.PromptId)
    {
      throw new IOException("ComfyUI returned a different generation identity; reconcile before taking further action.");
    }
  }

  public async Task<GenerationResult> CollectAsync(GenerationPlan plan, CancellationToken token)
  {
    WorkflowCatalog.RequirePromptId(plan.PromptId);
    ComfyHistory? history = await HistoryAsync(plan.PromptId, token);
    if (history is null)
    {
      throw new IOException("This generation has no completion history in ComfyUI.");
    }
    if (!history.Status.Completed)
    {
      throw new IOException("This generation has not completed in ComfyUI.");
    }
    if (history.Status.Status == "error")
    {
      throw new IOException(ExecutionError(history.Status));
    }

    List<string> files = new();
    foreach (ComfyNodeOutput output in history.Outputs.Values)
    {
      if (output.Images is null)
      {
        continue;
      }

      foreach (ComfyImage image in output.Images)
      {
        if (image.Type != "output")
        {
          continue;
        }

        files.Add(await CollectImageAsync(plan.PromptId, image, files.Count, token));
      }
    }

    if (files.Count == 0)
    {
      throw new IOException("ComfyUI completed without a saved output image.");
    }

    return new GenerationResult(plan.PromptId, files);
  }

  private async Task<string> CollectImageAsync(string promptId, ComfyImage image, int ordinal, CancellationToken token)
  {
    RequireImagePath(image);
    string relative = $"generations/{promptId}/{ordinal}-{image.Filename}";
    string target = Path.GetFullPath(Path.Combine(workspace, relative));
    RequireLocalPath(target);
    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
    if (File.Exists(target))
    {
      if (new FileInfo(target).Length == 0)
      {
        throw new IOException("A previously collected output file is empty.");
      }
      return relative;
    }

    string query = "view?type=output&filename=" + Uri.EscapeDataString(image.Filename) + "&subfolder=" + Uri.EscapeDataString(image.Subfolder);
    using CancellationTokenSource timeout = Timeout(token);
    using HttpResponseMessage response = await http.GetAsync(Address(query), HttpCompletionOption.ResponseHeadersRead, timeout.Token);
    await RequireAsync(response, "collect the output image", timeout.Token);
    string partial = target + ".part";
    RequireLocalPath(partial);
    if (File.Exists(partial))
    {
      File.Delete(partial);
    }
    try
    {
      await using (FileStream destination = new(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None))
      {
        await response.Content.CopyToAsync(destination, timeout.Token);
      }
      if (new FileInfo(partial).Length == 0)
      {
        throw new IOException("ComfyUI returned an empty image file.");
      }
      File.Move(partial, target);
    }
    finally
    {
      if (File.Exists(partial))
      {
        File.Delete(partial);
      }
    }

    return relative;
  }

  private async Task<ComfyHistory?> HistoryAsync(string promptId, CancellationToken token)
  {
    using HttpResponseMessage response = await GetAsync("history/" + promptId, token);
    Dictionary<string, ComfyHistory>? history = await response.Content.ReadFromJsonAsync<Dictionary<string, ComfyHistory>>(Json, token);
    if (history is not null)
    {
      if (history.TryGetValue(promptId, out ComfyHistory? found))
      {
        return found;
      }
    }
    return null;
  }

  private async Task<HttpResponseMessage> GetAsync(string route, CancellationToken token)
  {
    using CancellationTokenSource timeout = Timeout(token);
    HttpResponseMessage response = await http.GetAsync(Address(route), timeout.Token);
    try
    {
      await RequireAsync(response, "read generation state", timeout.Token);
      return response;
    }
    catch
    {
      response.Dispose();
      throw;
    }
  }

  private Uri Address(string route) => new(new Uri(options.BaseUrl.TrimEnd('/') + "/"), route);

  private CancellationTokenSource Timeout(CancellationToken token)
  {
    CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
    timeout.CancelAfter(TimeSpan.FromSeconds(options.RequestTimeoutSeconds));
    return timeout;
  }

  private static bool Contains(List<List<JsonElement>> entries, string promptId)
  {
    foreach (List<JsonElement> entry in entries)
    {
      if (entry.Count > QueuePromptIndex && entry[QueuePromptIndex].GetString() == promptId)
      {
        return true;
      }
    }
    return false;
  }

  private static string ExecutionError(ComfyExecutionStatus status)
  {
    if (status.Messages is not null)
    {
      foreach (JsonElement message in status.Messages)
      {
        if (message.ValueKind != JsonValueKind.Array || message.GetArrayLength() < 2)
        {
          continue;
        }
        JsonElement detail = message[1];
        if (detail.ValueKind == JsonValueKind.Object && detail.TryGetProperty("exception_message", out JsonElement text))
        {
          string error = text.GetString() ?? "The workflow failed.";
          return error.Length > ErrorMessageLimit ? error[..ErrorMessageLimit] : error;
        }
      }
    }
    return "ComfyUI could not complete the generation.";
  }

  private static async Task<string> RejectionAsync(HttpResponseMessage response, CancellationToken token)
  {
    string body = await response.Content.ReadAsStringAsync(token);
    return body.Length > ErrorMessageLimit ? body[..ErrorMessageLimit] : body;
  }

  private static async Task RequireAsync(HttpResponseMessage response, string operation, CancellationToken token)
  {
    if (!response.IsSuccessStatusCode)
    {
      string detail = await RejectionAsync(response, token);
      throw new HttpRequestException($"ComfyUI could not {operation}: HTTP {(int)response.StatusCode}. {detail}");
    }
  }

  private static void RequireImagePath(ComfyImage image)
  {
    if (string.IsNullOrWhiteSpace(image.Filename) || image.Subfolder is null)
    {
      throw new IOException("ComfyUI returned an empty output file path.");
    }
    bool filenameValid = image.Filename.IndexOfAny(['/', '\\']) < 0;
    bool subfolderValid = !Path.IsPathRooted(image.Subfolder);
    foreach (string part in image.Subfolder.Replace('\\', '/').Split('/'))
    {
      if (part == "..")
      {
        subfolderValid = false;
      }
    }
    if (!filenameValid || !subfolderValid)
    {
      throw new IOException("ComfyUI returned an invalid output file path.");
    }
  }

  private void RequireLocalPath(string target)
  {
    string root = Path.GetFullPath(workspace);
    if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
    {
      throw new IOException("A generated output would leave the workspace.");
    }
    string current = target;
    while (current.Length > root.Length)
    {
      FileSystemInfo entry = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
      if (entry.LinkTarget is not null)
      {
        throw new IOException("Generated output paths must not pass through symbolic links.");
      }
      current = Path.GetDirectoryName(current)!;
    }
  }
}
