using System.Net;
using System.Net.Http.Json;
using Holodeck.Web.Computers;
using Holodeck.Web.Projects;
using Holodeck.Web.Rooms;
using Xunit;

namespace Holodeck.Tests;

public sealed class ComputerEndpointTests
{
  [DatabaseFact]
  public async Task ResetRemovesInstallsAndKeepsWorkspace()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    await using TestHost host = await TestHost.StartAsync(database);
    TestLibrary.Write(database.DataPath, "probe", "Probe", ["sh", "-c", "exec sleep 30"]);
    host.Get<ComputerHost>().Learn(new HostPaths("/srv/holodeck", "/srv/holodeck/data", "/srv/holodeck/libs"), []);
    Project project = (await (await host.Client.PostAsJsonAsync("/api/projects", new ProjectInput("Environment reset", null, null))).Content.ReadFromJsonAsync<Project>())!;
    (await host.Client.PostAsJsonAsync($"/api/projects/{project.Id}/libraries", new ProjectLibraryInput(TestLibrary.Id, TestLibrary.Version))).EnsureSuccessStatusCode();
    string root = $"/api/projects/{project.Id}/room";
    RoomMemberView agent = (await (await host.Client.PostAsJsonAsync(root + "/agents", new PlaceInput(TestLibrary.Reference("probe")))).Content.ReadFromJsonAsync<RoomMemberView>())!;
    string computer = $"{root}/agents/{agent.Id}/computer";
    RoomPaths paths = TestLibrary.Paths(database.DataPath);
    string home = paths.Home(project.Id, agent.Id);
    Directory.CreateDirectory(home);
    File.WriteAllText(Path.Combine(home, "installed"), "tool");
    string workspaceFile = Path.Combine(paths.Table(project.Id, agent.Id), "work.txt");
    File.WriteAllText(workspaceFile, "keep my work");

    Assert.Equal(HttpStatusCode.NoContent, (await host.Client.PostAsync(computer + "/reset", null)).StatusCode);
    Assert.Empty(Directory.GetFileSystemEntries(home));
    Assert.Equal("keep my work", File.ReadAllText(workspaceFile));
  }

  [DatabaseFact]
  public async Task InitialGrantsAreValidatedAndExistingComputerEditsAreRefused()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    await using TestHost host = await TestHost.StartAsync(database, ["/home/me/code"]);
    TestLibrary.Write(database.DataPath, "probe", "Probe", ["sh", "-c", "exec sleep 30"], null, new AgentNeeds([8188]));
    host.Get<ComputerHost>().Learn(new HostPaths("/srv/holodeck", "/srv/holodeck/data", "/srv/holodeck/libs"), []);
    Project project = (await (await host.Client.PostAsJsonAsync("/api/projects", new ProjectInput("Computers", null, null))).Content.ReadFromJsonAsync<Project>())!;
    (await host.Client.PostAsJsonAsync($"/api/projects/{project.Id}/libraries", new ProjectLibraryInput(TestLibrary.Id, TestLibrary.Version))).EnsureSuccessStatusCode();
    string root = $"/api/projects/{project.Id}/room";
    ComputerInput input = new([8188], [new FolderGrant("repo", "/home/me/code/repo", true)]);
    RoomMemberView agent = (await (await host.Client.PostAsJsonAsync(root + "/agents", new PlaceInput(TestLibrary.Reference("probe"), input))).Content.ReadFromJsonAsync<RoomMemberView>())!;
    string computer = $"{root}/agents/{agent.Id}/computer";

    ComputerView initial = (await host.Client.GetFromJsonAsync<ComputerView>(computer))!;
    Assert.Equal([8188], initial.Needs.Ports);
    Assert.Equal(new FolderGrant("repo", "/home/me/code/repo", true), Assert.Single(initial.Grants.Folders));

    HttpResponseMessage saved = await host.Client.PutAsJsonAsync(computer, input);
    Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
    ComputerView granted = (await saved.Content.ReadFromJsonAsync<ComputerView>())!;
    Assert.Equal(new PortGrant(8188, 20000), Assert.Single(granted.Grants.Ports));
    Assert.Equal(new FolderGrant("repo", "/home/me/code/repo", true), Assert.Single(granted.Grants.Folders));
    Assert.Single((await host.Client.GetFromJsonAsync<ComputerView>(computer))!.Grants.Ports);

    ComputerInput changed = new([], []);
    Assert.Equal(HttpStatusCode.Conflict, (await host.Client.PutAsJsonAsync(computer, changed)).StatusCode);
    ComputerView unchanged = (await host.Client.GetFromJsonAsync<ComputerView>(computer))!;
    Assert.Equal(granted.Grants.Ports, unchanged.Grants.Ports);
    Assert.Equal(granted.Grants.Folders, unchanged.Grants.Folders);

    ComputerInput outside = new([], [new FolderGrant("etc", "/etc", false)]);
    Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.PutAsJsonAsync(computer, outside)).StatusCode);
    ComputerInput own = new([], [new FolderGrant("data", "/srv/holodeck/data", false)]);
    Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.PutAsJsonAsync(computer, own)).StatusCode);

    string home = TestLibrary.Paths(database.DataPath).Home(project.Id, agent.Id);
    Directory.CreateDirectory(Path.Combine(home, ".local"));
    File.WriteAllText(Path.Combine(home, ".local", "installed"), "x");
    (await host.Client.PostAsync($"{root}/agents/{agent.Id}/start", null)).EnsureSuccessStatusCode();
    Assert.Equal(HttpStatusCode.Conflict, (await host.Client.PostAsync(computer + "/reset", null)).StatusCode);
    (await host.Client.PostAsync($"{root}/agents/{agent.Id}/stop", null)).EnsureSuccessStatusCode();
    Assert.Equal(HttpStatusCode.NoContent, (await host.Client.PostAsync(computer + "/reset", null)).StatusCode);
    Assert.True(Directory.Exists(home));
    Assert.Empty(Directory.GetFileSystemEntries(home));
  }
}
