using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace CreativeStudio;

public sealed class HolodeckClient(HttpClient http, Uri address, string key)
{
  private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

  public async Task<AgentIdentity> MeAsync(CancellationToken token)
  {
    using HttpResponseMessage response = await RequestAsync(HttpMethod.Get, "me", null, token);
    return await response.Content.ReadFromJsonAsync<AgentIdentity>(Json, token) ?? throw new IOException("Holodeck returned no identity.");
  }

  public async Task<IncomingMessage?> NextAsync(int wait, CancellationToken token)
  {
    using HttpResponseMessage response = await RequestAsync(HttpMethod.Get, "next?wait=" + wait, null, token);
    if (response.StatusCode == HttpStatusCode.NoContent)
    {
      return null;
    }

    return await response.Content.ReadFromJsonAsync<IncomingMessage>(Json, token) ?? throw new IOException("Holodeck returned an empty message.");
  }

  public async Task<List<SqlResult>> DatabaseAsync(List<SqlStatement> statements, CancellationToken token)
  {
    using HttpResponseMessage response = await RequestAsync(HttpMethod.Post, "database", JsonContent.Create(new SqlRequest(statements), options: Json), token);
    SqlResponse result = await response.Content.ReadFromJsonAsync<SqlResponse>(Json, token) ?? throw new IOException("Holodeck returned no database result.");
    return result.Results;
  }

  public async Task SendAsync(StoredSend send, CancellationToken token)
  {
    SendRequest body = new(send.Channel, send.Text, send.Files, send.Key);
    using HttpResponseMessage response = await RequestAsync(HttpMethod.Post, "send", JsonContent.Create(body, options: Json), token);
  }

  public async Task ActivityAsync(ActivityUpdate update, CancellationToken token)
  {
    using HttpResponseMessage response = await RequestAsync(HttpMethod.Post, "activity", JsonContent.Create(update, options: Json), token);
  }

  public async Task<ManagementClaim?> ClaimAsync(string claimId, CancellationToken token)
  {
    using HttpResponseMessage response = await RequestAsync(HttpMethod.Post, "jobs/claim", JsonContent.Create(new ManagementClaimRequest(claimId), options: Json), token);
    if (response.StatusCode == HttpStatusCode.NoContent)
    {
      return null;
    }

    return await response.Content.ReadFromJsonAsync<ManagementClaim>(Json, token);
  }

  public async Task CompleteAsync(ManagementClaim claim, PromptActionResult result, CancellationToken token)
  {
    using HttpResponseMessage response = await RequestAsync(HttpMethod.Post, "jobs/" + claim.Job.Id + "/complete", JsonContent.Create(new ManagementCompletion(claim.AttemptToken, result), options: Json), token);
  }

  public async Task FailAsync(ManagementClaim claim, string error, CancellationToken token)
  {
    using HttpResponseMessage response = await RequestAsync(HttpMethod.Post, "jobs/" + claim.Job.Id + "/fail", JsonContent.Create(new ManagementFailure(claim.AttemptToken, error), options: Json), token);
  }

  private async Task<HttpResponseMessage> RequestAsync(HttpMethod method, string route, HttpContent? body, CancellationToken token)
  {
    using HttpRequestMessage request = new(method, new Uri(address, "api/agent/" + route));
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
    request.Content = body;
    HttpResponseMessage response = await http.SendAsync(request, token);
    if (response.StatusCode == HttpStatusCode.Unauthorized)
    {
      response.Dispose();
      throw new UnauthorizedAccessException("The agent session was stopped.");
    }

    if (!response.IsSuccessStatusCode)
    {
      string message = await response.Content.ReadAsStringAsync(token);
      int status = (int)response.StatusCode;
      response.Dispose();
      throw new HttpRequestException($"Holodeck returned HTTP {status}: {message}", null, (HttpStatusCode)status);
    }

    return response;
  }
}
