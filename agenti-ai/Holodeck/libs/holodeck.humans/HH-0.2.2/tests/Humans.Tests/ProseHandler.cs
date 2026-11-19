using System.Net;
using System.Text;
using System.Text.Json;

namespace Humans.Tests;

public sealed class ProseHandler(string prose) : HttpMessageHandler
{
  public string? Body { get; private set; }

  protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
  {
    Body=await request.Content!.ReadAsStringAsync(token);
    ChatResponse reply=new([new ChatChoice(new Message("assistant",prose))],new ChatUsage(10,10));
    return new HttpResponseMessage(HttpStatusCode.OK) { Content=new StringContent(JsonSerializer.Serialize(reply,new JsonSerializerOptions(JsonSerializerDefaults.Web)),Encoding.UTF8,"application/json") };
  }
}
