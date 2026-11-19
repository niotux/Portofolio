using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Humans;

// Holodeck's shared database of this library. A person reaches it through the agent API with its session key; the
// prompt tools reach the same data through the user API without one.
public sealed class LibraryDatabase(HttpClient http, Uri endpoint, string? key)
{
  private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

  public async Task<List<SqlResult>> RunAsync(List<SqlStatement> statements, CancellationToken token)
  {
    using HttpRequestMessage request = new(HttpMethod.Post, endpoint);
    if (key is not null)
    {
      request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
    }
    request.Content = JsonContent.Create(new SqlRequest(statements), options: Json);
    using HttpResponseMessage response = await http.SendAsync(request, token);
    if (response.StatusCode == HttpStatusCode.Unauthorized)
    {
      throw new UnauthorizedAccessException("The agent session was stopped.");
    }
    if (response.StatusCode == HttpStatusCode.BadRequest || response.StatusCode == HttpStatusCode.NotFound)
    {
      DatabaseError? error = await response.Content.ReadFromJsonAsync<DatabaseError>(Json, token);
      throw new IOException("The library database refused the request: " + error?.Error);
    }
    if (!response.IsSuccessStatusCode)
    {
      throw new HttpRequestException($"The library database returned HTTP {(int)response.StatusCode}.");
    }
    SqlResponse result = await response.Content.ReadFromJsonAsync<SqlResponse>(Json, token) ?? throw new IOException("The library database returned nothing.");
    return result.Results;
  }
}
