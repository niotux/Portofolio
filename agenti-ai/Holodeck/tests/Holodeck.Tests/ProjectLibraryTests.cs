using Holodeck.Web.Computers;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Holodeck.Web.Projects;
using Holodeck.Web.Rooms;
using Xunit;

namespace Holodeck.Tests;

public sealed class ProjectLibraryTests
{
  [DatabaseFact]
  public async Task AddingAReferenceDoesNotPlaceAgentsAndIncludesLocalDependencies()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    TestRoom setup = await PrepareAsync(database);
    WriteLibrary(database.DataPath, "utilities");
    WriteLibrary(database.DataPath, "writing", [new LibraryDependency("utilities", "1.0.0")]);
    AgentLibrary catalog = new(TestLibrary.Paths(database.DataPath));
    ProjectLibraryStore store = new(database.Database, catalog);
    await store.InitializeAsync(CancellationToken.None);

    await store.AddAsync(setup.Project.Id, "writing", "1.0.0");
    await store.AddAsync(setup.Project.Id, "writing", "1.0.0");

    List<ProjectLibraryView> libraries = await store.ListAsync(setup.Project.Id);
    Assert.Equal(2, libraries.Count);
    foreach (ProjectLibraryView reference in libraries)
    {
      Assert.True(reference.IsAvailable);
      Assert.Null(reference.Error);
    }

    Assert.Single(await setup.Room.ListMembersAsync(setup.Project.Id));
    await store.RequireAgentAsync(setup.Project.Id, catalog.Get("writing/1.0.0/agents/probe"));
    await store.RemoveAsync(setup.Project.Id, "utilities", "1.0.0");
    ProjectLibraryView writing = Assert.Single(await store.ListAsync(setup.Project.Id));
    Assert.False(writing.IsAvailable);
    Assert.Contains("utilities", writing.Error);
    await Assert.ThrowsAsync<InvalidOperationException>(() => store.RequireAgentAsync(setup.Project.Id, catalog.Get("writing/1.0.0/agents/probe")));
  }

  [DatabaseFact]
  public async Task RemovingAReferenceLeavesGhostsAndReaddingRepairsThemWithoutChangingFiles()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    TestRoom setup = await PrepareAsync(database);
    WriteLibrary(database.DataPath, "sample");
    AgentLibrary catalog = new(TestLibrary.Paths(database.DataPath));
    ProjectLibraryStore store = new(database.Database, catalog);
    await store.InitializeAsync(CancellationToken.None);
    await store.AddAsync(setup.Project.Id, "sample", "1.0.0");
    LibraryAgent definition = catalog.Get("sample/1.0.0/agents/probe");
    RoomMember agent = await setup.Room.PlaceAsync(setup.Project.Id, definition);
    RoomMember you = await setup.Room.EnsureYouAsync(setup.Project.Id);
    Channel wire = await setup.Room.EnsurePairChannelAsync(setup.Project.Id, you.Id, agent.Id);
    RoomPaths paths = TestLibrary.Paths(database.DataPath);
    string tableFile = Path.Combine(paths.Table(setup.Project.Id, agent.Id), "draft.txt");
    File.WriteAllText(tableFile, "keep this");
    File.WriteAllText(paths.SettingsFile(setup.Project.Id, agent.Id), "{\"edited\":true}");

    await store.RemoveAsync(setup.Project.Id, "sample", "1.0.0");

    Assert.Empty(await store.ListAsync(setup.Project.Id));
    Assert.Contains("not referenced", await store.ErrorForAsync(setup.Project.Id, agent.Library));
    Assert.False((await setup.Room.GetMemberAsync(setup.Project.Id, agent.Id)).IsRemoved);
    Assert.Equal(wire.Id, Assert.Single(await setup.Room.ListChannelsAsync(setup.Project.Id)).Id);
    Assert.Equal("keep this", File.ReadAllText(tableFile));
    await store.AddAsync(setup.Project.Id, "sample", "1.0.0");
    Assert.Null(await store.ErrorForAsync(setup.Project.Id, agent.Library));
    Assert.Equal("{\"edited\":true}", File.ReadAllText(paths.SettingsFile(setup.Project.Id, agent.Id)));
  }

  [DatabaseFact]
  public async Task MissingLocalSourceKeepsTheReferenceWithAnError()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    TestRoom setup = await PrepareAsync(database);
    string directory = WriteLibrary(database.DataPath, "sample");
    ProjectLibraryStore store = new(database.Database, new AgentLibrary(TestLibrary.Paths(database.DataPath)));
    await store.InitializeAsync(CancellationToken.None);
    await store.AddAsync(setup.Project.Id, "sample", "1.0.0");
    Directory.Delete(directory, true);

    ProjectLibraryView missing = Assert.Single(await store.ListAsync(setup.Project.Id));

    Assert.Equal("sample", missing.Id);
    Assert.Equal("sample library", missing.Name);
    Assert.False(missing.IsAvailable);
    Assert.Contains("unavailable", missing.Error);
  }

  [DatabaseFact]
  public async Task ExistingPackagedMembersBackfillOnceIncludingDependencies()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    TestRoom setup = await PrepareAsync(database);
    WriteLibrary(database.DataPath, "utilities");
    WriteLibrary(database.DataPath, "sample", [new LibraryDependency("utilities", "1.0.0")]);
    AgentLibrary catalog = new(TestLibrary.Paths(database.DataPath));
    RoomMember agent = await setup.Room.PlaceAsync(setup.Project.Id, catalog.Get("sample/1.0.0/agents/probe"));
    ProjectLibraryStore store = new(database.Database, catalog);

    await store.InitializeAsync(CancellationToken.None);
    Assert.Equal(2, (await store.ListAsync(setup.Project.Id)).Count);
    await store.RemoveAsync(setup.Project.Id, "sample", "1.0.0");
    await store.InitializeAsync(CancellationToken.None);

    Assert.Equal("utilities", Assert.Single(await store.ListAsync(setup.Project.Id)).Id);
    Assert.NotNull(await store.ErrorForAsync(setup.Project.Id, agent.Library));
  }

  [DatabaseFact]
  public async Task InvalidReferencesAndMissingProjectsDoNotMutateReferences()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    TestRoom setup = await PrepareAsync(database);
    WriteLibrary(database.DataPath, "sample");
    ProjectLibraryStore store = new(database.Database, new AgentLibrary(TestLibrary.Paths(database.DataPath)));
    await store.InitializeAsync(CancellationToken.None);

    await Assert.ThrowsAsync<KeyNotFoundException>(() => store.AddAsync(setup.Project.Id, "sample", "9.0.0"));
    await Assert.ThrowsAsync<ArgumentException>(() => store.AddAsync(setup.Project.Id, "../sample", "1.0.0"));
    await Assert.ThrowsAsync<KeyNotFoundException>(() => store.AddAsync("missing", "sample", "1.0.0"));
    await Assert.ThrowsAsync<KeyNotFoundException>(() => store.ListAsync("missing"));
    Assert.Empty(await store.ListAsync(setup.Project.Id));
  }

  [DatabaseFact]
  public async Task RemovingAReferenceStopsItsProgramAndRestoringItKeepsTheSameMember()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    WriteLibrary(database.DataPath, "sample");
    await using TestHost host = await TestHost.StartAsync(database);
    HttpResponseMessage created = await host.Client.PostAsJsonAsync("/api/projects", new ProjectInput("Reference lifecycle", null, null));
    Project project = (await created.Content.ReadFromJsonAsync<Project>())!;
    string libraries = $"/api/projects/{project.Id}/libraries";
    string root = $"/api/projects/{project.Id}/room";
    HttpResponseMessage attached = await host.Client.PostAsJsonAsync(libraries, new ProjectLibraryInput("sample", "1.0.0"));
    attached.EnsureSuccessStatusCode();
    HttpResponseMessage placed = await host.Client.PostAsJsonAsync(root + "/agents", new PlaceInput("sample/1.0.0/agents/probe"));
    placed.EnsureSuccessStatusCode();
    RoomMemberView member = (await placed.Content.ReadFromJsonAsync<RoomMemberView>())!;
    string agent = root + "/agents/" + member.Id;
    (await host.Client.PostAsync(agent + "/start", null)).EnsureSuccessStatusCode();
    (await host.Client.PutAsync(agent + "/settings", new StringContent("{\"keep\":true}"))).EnsureSuccessStatusCode();

    Assert.Equal(HttpStatusCode.NoContent, (await host.Client.DeleteAsync(libraries + "/sample/1.0.0")).StatusCode);

    Assert.Equal(AgentStatus.Stopped, host.Get<AgentSupervisor>().Status(member.Id));
    RoomView ghostView = (await host.Client.GetFromJsonAsync<RoomView>(root))!;
    Assert.False(ghostView.Members[1].IsRemoved);
    Assert.Equal(member.Id, ghostView.Members[1].Id);
    Assert.Equal("{\"keep\":true}", await host.Client.GetStringAsync(agent + "/settings"));
    Assert.Equal(HttpStatusCode.Conflict, (await host.Client.PostAsync(agent + "/start", null)).StatusCode);
    (await host.Client.PostAsJsonAsync(libraries, new ProjectLibraryInput("sample", "1.0.0"))).EnsureSuccessStatusCode();
    (await host.Client.PostAsync(agent + "/start", null)).EnsureSuccessStatusCode();
    Assert.Equal(AgentStatus.Running, host.Get<AgentSupervisor>().Status(member.Id));
  }

  private static async Task<TestRoom> PrepareAsync(TestDatabase database)
  {
    ProjectStore projects = new(database.Database);
    await projects.InitializeAsync(CancellationToken.None);
    RoomStore room = new(database.Database, TestLibrary.Paths(database.DataPath));
    await room.InitializeAsync(CancellationToken.None);
    Project project = await projects.CreateAsync(new ProjectInput("Library references", null, null), CancellationToken.None);
    await room.EnsureYouAsync(project.Id);
    return new TestRoom(room, project);
  }

  private static string WriteLibrary(string dataPath, string id, List<LibraryDependency>? dependencies = null)
  {
    string directory = Path.Combine(dataPath, "libs", id);
    string agent = Path.Combine(directory, "agents", "probe");
    Directory.CreateDirectory(agent);
    Directory.CreateDirectory(Path.Combine(directory, "teams"));
    File.WriteAllText(Path.Combine(directory, "Library.json"), JsonSerializer.Serialize(new LibraryManifest(1, id, "1.0.0", id + " library", "", dependencies, Runtime: new LibraryEnvironmentDefinition(TestLibrary.RuntimeImage, new()))));
    File.WriteAllText(Path.Combine(agent, "Agent.json"), JsonSerializer.Serialize(new PackageAgentManifest("probe", "Probe", "", new AgentLaunch("sh", ["${Agent}/run.sh"]), null)));
    File.WriteAllText(Path.Combine(agent, "run.sh"), "exec sleep 60\n");
    return directory;
  }
}
