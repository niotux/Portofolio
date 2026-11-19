using System.Net;
using System.Text;

namespace Humans.Tests;

internal sealed class FormationOutageHandler : HttpMessageHandler
{
  private readonly TaskCompletionSource recovered = new(TaskCreationOptions.RunContinuationsAsynchronously);
  private int requests;

  public Task Recovered => recovered.Task;

  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
  {
    if (Interlocked.Increment(ref requests) == 1)
    {
      throw new HttpRequestException("Holodeck is restarting.");
    }

    recovered.TrySetResult();
    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"results\":[{\"rows\":[],\"affected\":0}]}", Encoding.UTF8, "application/json") });
  }
}
