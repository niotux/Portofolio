using System.Net.Http.Json;
using System.Text.Json;

namespace Holodeck.Web.Computers;

public sealed partial class DockerClient
{
  public async Task<string> CreateLibraryEnvironmentAsync(string name, LibraryEnvironmentCreateRequest request, CancellationToken token)
  {
    using HttpResponseMessage response = await SendAsync(HttpMethod.Post, $"/containers/create?name={Uri.EscapeDataString(name)}", JsonContent.Create(request), token);
    await RequireAsync(response, "create the library environment");
    using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
    return body.RootElement.GetProperty("Id").GetString()!;
  }
}
