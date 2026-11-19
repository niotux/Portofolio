namespace CreativeStudio.Tests;

internal sealed class ModelTestHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
  {
    return respond(request, cancellationToken);
  }
}
