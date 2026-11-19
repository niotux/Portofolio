using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Humans;

// Identity and packet IDs never leave this transport/store boundary for a model prompt.
public sealed class Holodeck(HttpClient http, Uri address, string key)
{
  private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

  public async Task<Identity> MeAsync(CancellationToken token)
  {
    using HttpResponseMessage response = await RequestAsync(HttpMethod.Get, "me", null, token);
    return await response.Content.ReadFromJsonAsync<Identity>(Json, token) ?? throw new IOException("Holodeck returned no identity.");
  }

  public async Task<Delivery?> NextAsync(int wait, CancellationToken token)
  {
    using HttpResponseMessage response = await RequestAsync(HttpMethod.Get, "next?wait=" + wait, null, token);
    if (response.StatusCode == HttpStatusCode.NoContent)
    {
      return null;
    }
    return await response.Content.ReadFromJsonAsync<Delivery>(Json, token) ?? throw new IOException("Holodeck returned no delivery.");
  }

  public async Task SendAsync(PendingSend send, CancellationToken token)
  {
    using HttpResponseMessage response = await RequestAsync(HttpMethod.Post, "send", new SendBody(send.Channel, send.Text, send.Key), token);
  }

  public async Task ActivityAsync(ActivityUpdate update, CancellationToken token)
  {
    using HttpRequestMessage request = new(HttpMethod.Post, new Uri(address, "api/agent/activity"));
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
    request.Content = JsonContent.Create(update, options: Json);
    using HttpResponseMessage response = await http.SendAsync(request, token);
    if (!response.IsSuccessStatusCode)
    {
      throw new HttpRequestException($"Activity report returned HTTP {(int)response.StatusCode}.");
    }
  }

  // My own database in Holodeck's PostgreSQL: the statements run as one transaction in my own schema.
  public async Task<List<SqlResult>> DatabaseAsync(List<SqlStatement> statements, CancellationToken token)
  {
    using HttpRequestMessage request = new(HttpMethod.Post, new Uri(address, "api/agent/database"));
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
    request.Content = JsonContent.Create(new SqlRequest(statements), options: Json);
    using HttpResponseMessage response = await http.SendAsync(request, token);
    if (response.StatusCode == HttpStatusCode.Unauthorized)
    {
      throw new UnauthorizedAccessException("The agent session was stopped.");
    }
    if (response.StatusCode == HttpStatusCode.BadRequest)
    {
      DatabaseError? error = await response.Content.ReadFromJsonAsync<DatabaseError>(Json, token);
      throw new IOException("My database refused a statement: " + error?.Error);
    }
    if (!response.IsSuccessStatusCode)
    {
      throw new HttpRequestException($"Holodeck's database returned HTTP {(int)response.StatusCode}.");
    }
    SqlResponse result = await response.Content.ReadFromJsonAsync<SqlResponse>(Json, token) ?? throw new IOException("Holodeck's database returned nothing.");
    return result.Results;
  }

  private async Task<HttpResponseMessage> RequestAsync(HttpMethod method, string route, SendBody? body, CancellationToken token)
  {
    using HttpRequestMessage request = new(method, new Uri(address, "api/agent/" + route));
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
    if (body is not null)
    {
      request.Content = JsonContent.Create(body, options: Json);
    }
    HttpResponseMessage response = await http.SendAsync(request, token);
    if (response.StatusCode == HttpStatusCode.Unauthorized)
    {
      response.Dispose();
      throw new UnauthorizedAccessException("The agent session was stopped.");
    }
    if (!response.IsSuccessStatusCode)
    {
      int status = (int)response.StatusCode;
      response.Dispose();
      throw new HttpRequestException($"Holodeck returned HTTP {status}.");
    }
    return response;
  }
}
