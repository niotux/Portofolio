using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace CreativeStudio.Tests;

internal sealed class InboxTestHandler(IncomingMessage message, Action? onPoll = null) : HttpMessageHandler
{
  private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

  public int Polls { get; private set; }

  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
  {
    Polls++;
    onPoll?.Invoke();
    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(message, options: Json) });
  }
}
