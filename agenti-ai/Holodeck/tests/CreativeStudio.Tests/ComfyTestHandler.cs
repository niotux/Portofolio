using System.Net;
using System.Text;

namespace CreativeStudio.Tests;

internal sealed class ComfyTestHandler(string promptId) : HttpMessageHandler
{
  public bool LoseSubmissionResponse { get; set; }
  public bool Completed { get; set; }
  public bool ReturnTraversalPath { get; set; }
  public int SubmissionCount { get; private set; }
  public int DownloadCount { get; private set; }

  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
  {
    string path = request.RequestUri!.AbsolutePath;
    if (path == "/prompt")
    {
      SubmissionCount++;
      if (LoseSubmissionResponse)
      {
        throw new HttpRequestException("The queue accepted work, but the response was lost.");
      }
      return Task.FromResult(Json("{\"prompt_id\":\"" + promptId + "\"}"));
    }
    if (path.StartsWith("/history/", StringComparison.Ordinal))
    {
      string history = "{}";
      if (Completed)
      {
        string filename = ReturnTraversalPath ? "../../outside.png" : "output.png";
        history = "{\"" + promptId + "\":{\"outputs\":{\"1\":{\"images\":[{\"filename\":\"" + filename + "\",\"subfolder\":\"creative\",\"type\":\"output\"}]}},\"status\":{\"status_str\":\"success\",\"completed\":true,\"messages\":[]}}}";
      }
      return Task.FromResult(Json(history));
    }
    if (path == "/queue")
    {
      return Task.FromResult(Json("{\"queue_running\":[[1,\"" + promptId + "\"]],\"queue_pending\":[]}"));
    }
    if (path == "/view")
    {
      DownloadCount++;
      return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3, 4]) });
    }
    throw new InvalidOperationException("Unexpected ComfyUI test route.");
  }

  private static HttpResponseMessage Json(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
}
