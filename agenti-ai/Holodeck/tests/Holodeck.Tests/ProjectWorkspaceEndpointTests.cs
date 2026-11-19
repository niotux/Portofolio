using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Holodeck.Web.Projects;
using Xunit;

namespace Holodeck.Tests;

public sealed class ProjectWorkspaceEndpointTests
{
  [DatabaseFact]
  public async Task WorkspaceRoundTripsPerProjectAndRejectsBadInput()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    await using TestHost host = await TestHost.StartAsync(database);
    Project first = await CreateProjectAsync(host, "First");
    Project second = await CreateProjectAsync(host, "Second");
    string path = $"/api/projects/{first.Id}/workspace";

    ProjectWorkspace empty = (await host.Client.GetFromJsonAsync<ProjectWorkspace>(path))!;
    Assert.Null(empty.Layout);

    ProjectWorkspace saved = new(Parse("""{"version":1,"windows":[]}"""));
    Assert.Equal(HttpStatusCode.NoContent, (await host.Client.PutAsJsonAsync(path, saved)).StatusCode);
    ProjectWorkspace read = (await host.Client.GetFromJsonAsync<ProjectWorkspace>(path))!;
    Assert.Equal(1, read.Layout!.Value.GetProperty("version").GetInt32());
    Assert.Equal(0, read.Layout!.Value.GetProperty("windows").GetArrayLength());

    ProjectWorkspace other = (await host.Client.GetFromJsonAsync<ProjectWorkspace>($"/api/projects/{second.Id}/workspace"))!;
    Assert.Null(other.Layout);

    Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync("/api/projects/missing/workspace")).StatusCode);
    Assert.Equal(HttpStatusCode.NotFound, (await host.Client.PutAsJsonAsync("/api/projects/missing/workspace", saved)).StatusCode);

    string padding = new('x', 300 * 1024);
    ProjectWorkspace oversize = new(Parse("{\"padding\":\"" + padding + "\"}"));
    Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.PutAsJsonAsync(path, oversize)).StatusCode);
    ProjectWorkspace array = new(Parse("[1,2]"));
    Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.PutAsJsonAsync(path, array)).StatusCode);
    ProjectWorkspace unchanged = (await host.Client.GetFromJsonAsync<ProjectWorkspace>(path))!;
    Assert.Equal(1, unchanged.Layout!.Value.GetProperty("version").GetInt32());

    Assert.Equal(HttpStatusCode.NoContent, (await host.Client.DeleteAsync($"/api/projects/{first.Id}")).StatusCode);
    Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync(path)).StatusCode);
  }

  private static async Task<Project> CreateProjectAsync(TestHost host, string name)
  {
    HttpResponseMessage response = await host.Client.PostAsJsonAsync("/api/projects", new ProjectInput(name, null, null));
    return (await response.Content.ReadFromJsonAsync<Project>())!;
  }

  private static JsonElement Parse(string json)
  {
    using JsonDocument document = JsonDocument.Parse(json);
    return document.RootElement.Clone();
  }
}
