using System.Net;
using System.Text;

namespace Humans.Tests;

public sealed class StreamingHandler(bool completed = true) : HttpMessageHandler
{
  public string? Body { get; private set; }
  public Uri? Address { get; private set; }
  public string? Authorization { get; private set; }

  protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
  {
    Body = await request.Content!.ReadAsStringAsync(token);
    Address = request.RequestUri;
    Authorization = request.Headers.Authorization?.ToString();
    const string Events = """
      data: {"choices":[{"delta":{"reasoning_content":"NATIVE THINKING MUST NOT APPEAR"}}]}

      data: {"choices":[{"delta":{"content":"I feel curious. "}}]}

      data: {"choices":[{"delta":{"content":"I should check my assumption."}}]}

      data: {"choices":[],"usage":{"prompt_tokens":14,"completion_tokens":9}}

      data: [DONE]

      """;
    string events = completed ? Events : Events.Replace("data: [DONE]", "");
    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(events, Encoding.UTF8, "text/event-stream") };
  }
}
