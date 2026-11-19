using Holodeck.Web.Projects;
using Holodeck.Web.Rooms;
using Npgsql;
using Xunit;

namespace Holodeck.Tests;

public sealed class TeamPlacementTests
{
  [DatabaseFact]
  public async Task PlacesTeamTwiceWithIndependentSettingsAndCorrectChannels()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    TestRoom setup = await CreateAsync(database);
    RoomPaths paths = TestLibrary.Paths(database.DataPath);
    LibraryAgent agent = CreateAgent(database);
    LibraryTeam team = CreateTeam(agent);
    List<RoomMember> first = await setup.Room.PlaceTeamAsync(setup.Project.Id, team);
    List<RoomMember> second = await setup.Room.PlaceTeamAsync(setup.Project.Id, team);
    Assert.Equal(2, first.Count);
    Assert.Equal(2, second.Count);
    Assert.Equal("Writer", first[0].Name);
    Assert.Equal("Editor", first[1].Name);
    Assert.Equal("Writer 2", second[0].Name);
    Assert.Equal("Editor 2", second[1].Name);
    Assert.Equal(agent.Folder, first[0].Library);
    List<RoomMember> all = await setup.Room.ListMembersAsync(setup.Project.Id);
    Assert.Equal(5, all.Count);
    Assert.True(all[0].IsYou);
    List<ChannelSummary> channels = await setup.Room.ListChannelsAsync(setup.Project.Id);
    Assert.Equal(6, channels.Count);
    Assert.Equal("Writer", (await setup.Room.FindPairChannelAsync(setup.Project.Id, all[0].Id, first[0].Id))!.Name);
    Assert.Equal("Writer & Editor", (await setup.Room.FindPairChannelAsync(setup.Project.Id, first[0].Id, first[1].Id))!.Name);
    Assert.NotNull(await setup.Room.FindPairChannelAsync(setup.Project.Id, all[0].Id, second[0].Id));
    Assert.Equal("Writer 2 & Editor 2", (await setup.Room.FindPairChannelAsync(setup.Project.Id, second[0].Id, second[1].Id))!.Name);
    Assert.Null(await setup.Room.FindPairChannelAsync(setup.Project.Id, first[0].Id, second[1].Id));
    Assert.Equal(Sorted([all[0].Id, first[0].Id, first[1].Id]), (await setup.Room.GetChannelAsync(setup.Project.Id, "Studio")).MemberIds);
    Assert.Equal(Sorted([all[0].Id, second[0].Id, second[1].Id]), (await setup.Room.GetChannelAsync(setup.Project.Id, "Studio 2")).MemberIds);
    Assert.Empty(await setup.Room.ListShouldRunAsync());
    string settings = paths.SettingsFile(setup.Project.Id, first[0].Id);
    Assert.Equal("{\"Voice\":\"plain\"}", await File.ReadAllTextAsync(settings));
    await File.WriteAllTextAsync(settings, "{\"Voice\":\"changed\"}");
    Assert.Equal("{\"Voice\":\"plain\"}", await File.ReadAllTextAsync(paths.SettingsFile(setup.Project.Id, second[0].Id)));
    Assert.Equal("{\"Voice\":\"plain\"}", await File.ReadAllTextAsync(agent.SettingsPath!));
  }

  [DatabaseFact]
  public async Task InvalidTeamDoesNotCreateAnyRoomMembers()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    TestRoom setup = await CreateAsync(database);
    LibraryTeam team = CreateTeam(CreateAgent(database));
    team.Wires.Add(["writer", "missing"]);
    await Assert.ThrowsAsync<ArgumentException>(() => setup.Room.PlaceTeamAsync(setup.Project.Id, team));
    Assert.Empty(await setup.Room.ListMembersAsync(setup.Project.Id));
    Assert.Empty(await setup.Room.ListChannelsAsync(setup.Project.Id));
    Assert.False(Directory.Exists(Path.Combine(database.DataPath, "rooms")));
  }

  [DatabaseFact]
  public async Task DuplicateAndSelfWiresAreRejectedBeforePlacement()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    TestRoom setup = await CreateAsync(database);
    LibraryAgent agent = CreateAgent(database);
    LibraryTeam duplicate = CreateTeam(agent);
    duplicate.Wires.Add(["editor", "writer"]);
    await Assert.ThrowsAsync<ArgumentException>(() => setup.Room.PlaceTeamAsync(setup.Project.Id, duplicate));
    LibraryTeam self = CreateTeam(agent);
    self.Wires.Add(["writer", "writer"]);
    await Assert.ThrowsAsync<ArgumentException>(() => setup.Room.PlaceTeamAsync(setup.Project.Id, self));
    Assert.Empty(await setup.Room.ListMembersAsync(setup.Project.Id));
  }

  [DatabaseFact]
  public async Task MissingExplicitSettingsDoesNotPlaceMembers()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    TestRoom setup = await CreateAsync(database);
    LibraryAgent agent = CreateAgent(database);
    File.Delete(agent.SettingsPath!);
    await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Room.PlaceTeamAsync(setup.Project.Id, CreateTeam(agent)));
    Assert.Empty(await setup.Room.ListMembersAsync(setup.Project.Id));
  }

  [DatabaseFact]
  public async Task FailureAfterFirstInsertRollsBackRowsAndNewFiles()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    TestRoom setup = await CreateAsync(database);
    LibraryAgent agent = CreateAgent(database);
    RoomMember existing = await setup.Room.PlaceAsync(setup.Project.Id, agent);
    RoomPaths paths = TestLibrary.Paths(database.DataPath);
    string existingFile = Path.Combine(paths.Table(setup.Project.Id, existing.Id), "keep.txt");
    await File.WriteAllTextAsync(existingFile, "keep me");
    await database.ExecuteAsync("ALTER TABLE room_members ADD CONSTRAINT test_reject_editor CHECK (name <> 'Editor')");
    await Assert.ThrowsAsync<PostgresException>(() => setup.Room.PlaceTeamAsync(setup.Project.Id, CreateTeam(agent)));
    Assert.Equal(existing, Assert.Single(await setup.Room.ListMembersAsync(setup.Project.Id)));
    Assert.Empty(await setup.Room.ListChannelsAsync(setup.Project.Id));
    Assert.Equal("keep me", await File.ReadAllTextAsync(existingFile));
    string agentsDirectory = Path.GetDirectoryName(paths.AgentDirectory(setup.Project.Id, existing.Id))!;
    Assert.Single(Directory.GetDirectories(agentsDirectory));
  }

  [DatabaseFact]
  public async Task PackagedAgentWithoutSettingsUsesEmptyObject()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    TestRoom setup = await CreateAsync(database);
    LibraryAgent agent = CreateAgent(database) with { SettingsPath = null };
    List<RoomMember> members = await setup.Room.PlaceTeamAsync(setup.Project.Id, CreateTeam(agent));
    string settings = TestLibrary.Paths(database.DataPath).SettingsFile(setup.Project.Id, members[0].Id);
    Assert.Equal("{}", await File.ReadAllTextAsync(settings));
  }

  private static async Task<TestRoom> CreateAsync(TestDatabase database)
  {
    ProjectStore projects = new(database.Database);
    await projects.InitializeAsync(CancellationToken.None);
    RoomStore room = new(database.Database, TestLibrary.Paths(database.DataPath));
    await room.InitializeAsync(CancellationToken.None);
    Project project = await projects.CreateAsync(new ProjectInput("Team room", null, null), CancellationToken.None);
    return new TestRoom(room, project);
  }

  private static LibraryAgent CreateAgent(TestDatabase database)
  {
    TestLibrary.Write(database.DataPath, "talker", "Talker", ["python3", "${Agent}/agent.py"], "{\"Voice\":\"plain\"}");
    return TestLibrary.Agent(database.DataPath, "talker");
  }

  private static LibraryTeam CreateTeam(LibraryAgent agent)
  {
    List<LibraryTeamMember> members = [new("writer", "Writer", agent), new("editor", "Editor", agent)];
    return new LibraryTeam("example.writing/1.0.0/teams/writing", "Writing", "Writing team", members, [["You", "writer"], ["writer", "editor"]], [new LibraryTeamChannel("Studio", ["You", "writer", "editor"])]);
  }

  // Channel members are listed in id order.
  private static List<string> Sorted(List<string> ids)
  {
    ids.Sort(StringComparer.Ordinal);
    return ids;
  }
}
