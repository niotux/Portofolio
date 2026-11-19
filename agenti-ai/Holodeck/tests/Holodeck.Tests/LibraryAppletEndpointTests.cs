using System.Net;
using System.Net.Http.Json;
using Holodeck.Web.Computers;
using Holodeck.Web.Projects;
using Holodeck.Web.Rooms;
using Microsoft.AspNetCore.Hosting;
using Xunit;

namespace Holodeck.Tests;

public sealed class LibraryAppletEndpointTests
{
  [DatabaseFact]
  public async Task AppletListingUsesExactReferencesAndDoesNotLeakOtherProjects()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    using LibraryAppletFixture fixture = new(database.DataPath);
    fixture.Write(version: "1.0.0");
    fixture.Write(version: "2.0.0");
    fixture.Write(id: "unattached");
    await using TestHost host = await TestHost.StartAsync(database);
    Project first = await CreateProjectAsync(host, "First");
    Project second = await CreateProjectAsync(host, "Second");
    await AttachAsync(host, first, "1.0.0");
    await AttachAsync(host, second, "2.0.0");

    LibraryAppletView firstApplet = Assert.Single((await host.Client.GetFromJsonAsync<List<LibraryAppletView>>($"/api/projects/{first.Id}/applets"))!);
    LibraryAppletView secondApplet = Assert.Single((await host.Client.GetFromJsonAsync<List<LibraryAppletView>>($"/api/projects/{second.Id}/applets"))!);
    Assert.Equal("utilities", firstApplet.LibraryId);
    Assert.Equal("1.0.0", firstApplet.LibraryVersion);
    Assert.Equal("2.0.0", secondApplet.LibraryVersion);
    Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync(AssetPath(first, "2.0.0", "terminal", "applets/terminal/index.html"))).StatusCode);
    Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync(AssetPath(second, "1.0.0", "terminal", "applets/terminal/index.html"))).StatusCode);
    Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync("/api/projects/missing/applets")).StatusCode);
  }

  [DatabaseFact]
  public async Task AppletAssetsAreSandboxedAndCannotReadPrivateFilesOrSymlinks()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    using LibraryAppletFixture fixture = new(database.DataPath);
    string directory = fixture.Write();
    File.CreateSymbolicLink(Path.Combine(directory, "applets", "terminal", "private-link.txt"), Path.Combine(directory, "private.txt"));
    await using TestHost host = await TestHost.StartAsync(database);
    Project project = await CreateProjectAsync(host, "Assets");
    await AttachAsync(host, project);

    using HttpResponseMessage response = await host.Client.GetAsync(AssetPath(project, "1.0.0", "terminal", "applets/terminal/index.html"));
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.Equal("text/html", response.Content.Headers.ContentType!.MediaType);
    Assert.Contains("Test terminal", await response.Content.ReadAsStringAsync());
    string policy = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));
    Assert.Contains("sandbox allow-scripts", policy);
    Assert.DoesNotContain("allow-same-origin", policy);
    Assert.Contains("connect-src 'none'", policy);
    Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
    Assert.Equal("*", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));

    Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.GetAsync(AssetPath(project, "1.0.0", "terminal", "private.txt"))).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.GetAsync(AssetPath(project, "1.0.0", "terminal", "applets/terminal/private-link.txt"))).StatusCode);
    Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync(AssetPath(project, "1.0.0", "unknown", "applets/terminal/index.html"))).StatusCode);
  }

  [DatabaseFact]
  public async Task BridgeAssetRequiresAnAttachedLibraryAndDeclaredApplet()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    using LibraryAppletFixture fixture = new(database.DataPath);
    fixture.Write();
    await using TestHost host = await TestHost.StartAsync(database);
    string webRoot = Path.Combine(database.DataPath, "webroot");
    Directory.CreateDirectory(webRoot);
    File.WriteAllText(Path.Combine(webRoot, "library-applet-bridge.js"), "export const bridge = 'platform';");
    host.Get<IWebHostEnvironment>().WebRootPath = webRoot;
    Project project = await CreateProjectAsync(host, "Bridge");
    Project other = await CreateProjectAsync(host, "Other");
    await AttachAsync(host, project);

    using HttpResponseMessage bridge = await host.Client.GetAsync(AssetPath(project, "1.0.0", "terminal", "_holodeck/bridge.js"));
    Assert.Equal(HttpStatusCode.OK, bridge.StatusCode);
    Assert.Contains("platform", await bridge.Content.ReadAsStringAsync());
    Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync(AssetPath(project, "1.0.0", "unknown", "_holodeck/bridge.js"))).StatusCode);
    Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync(AssetPath(other, "1.0.0", "terminal", "_holodeck/bridge.js"))).StatusCode);
  }

  [DatabaseFact]
  public async Task ServiceProxyRejectsUndeclaredServicesBeforeStartingAnEnvironment()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    using LibraryAppletFixture fixture = new(database.DataPath);
    LibraryEnvironmentDefinition runtime = new("holodeck-library:1", [new LibraryEnvironmentProcess("private", ["sleep", "60"], 8090)]);
    fixture.Write(runtime: runtime);
    await using TestHost host = await TestHost.StartAsync(database);
    Project project = await CreateProjectAsync(host, "Private service");
    await AttachAsync(host, project);

    using HttpResponseMessage response = await host.Client.GetAsync($"/api/projects/{project.Id}/libraries/utilities/1.0.0/services/private/health?applet=terminal");
    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    Assert.Contains("cannot access", await response.Content.ReadAsStringAsync());
  }

  [DatabaseFact]
  public async Task CrossOriginAndOpaqueOriginServiceRequestsAreRejectedBeforeProjectLookup()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    await using TestHost host = await TestHost.StartAsync(database);
    string path = "/api/projects/not-a-project/libraries/utilities/1.0.0/services/bash/health?applet=terminal";
    foreach (string origin in new[] { "https://another.example", "null", "https://" + host.Client.BaseAddress!.Authority })
    {
      using HttpRequestMessage request = new(HttpMethod.Get, path);
      request.Headers.Add("Origin", origin);
      using HttpResponseMessage response = await host.Client.SendAsync(request);
      Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
  }

  private static string AssetPath(Project project, string version, string applet, string path)
  {
    return $"/api/projects/{project.Id}/libraries/utilities/{version}/applets/{applet}/assets/{path}";
  }

  private static async Task<Project> CreateProjectAsync(TestHost host, string name)
  {
    using HttpResponseMessage response = await host.Client.PostAsJsonAsync("/api/projects", new ProjectInput(name, null, null));
    response.EnsureSuccessStatusCode();
    return (await response.Content.ReadFromJsonAsync<Project>())!;
  }

  private static async Task AttachAsync(TestHost host, Project project, string version = "1.0.0")
  {
    using HttpResponseMessage response = await host.Client.PostAsJsonAsync($"/api/projects/{project.Id}/libraries", new ProjectLibraryInput("utilities", version));
    response.EnsureSuccessStatusCode();
  }
}
