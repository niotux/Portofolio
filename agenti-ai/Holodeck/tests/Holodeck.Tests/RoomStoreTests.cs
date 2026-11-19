using Holodeck.Web.Projects;
using Holodeck.Web.Rooms;
using Xunit;

namespace Holodeck.Tests;

public sealed class RoomStoreTests
{
  private static readonly CancellationToken Token = CancellationToken.None;

  [DatabaseFact]
  public async Task YouExistsOnceAndCannotBeRemovedOrRenamed()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    TestRoom setup = await CreateAsync(database);
    RoomStore room = setup.Room;
    Project project = setup.Project;
    RoomMember you = await room.EnsureYouAsync(project.Id);
    Assert.Equal(you, await room.EnsureYouAsync(project.Id));
    Assert.Equal("You", you.Name);
    Assert.True(you.IsYou);
    await Assert.ThrowsAsync<InvalidOperationException>(() => room.RemoveAsync(project.Id, you.Id));
    await Assert.ThrowsAsync<InvalidOperationException>(() => room.RenameAsync(project.Id, you.Id, "Me"));
  }

  [DatabaseFact]
  public async Task PlacingTwiceSuffixesNamesAndCopiesSettings()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    TestRoom setup = await CreateAsync(database);
    RoomStore room = setup.Room;
    Project project = setup.Project;
    LibraryAgent talker = PlaceableTalker(database, "{\"x\":1}");
    RoomMember first = await room.PlaceAsync(project.Id, talker);
    RoomMember second = await room.PlaceAsync(project.Id, talker);
    Assert.Equal("Talker", first.Name);
    Assert.Equal("Talker 2", second.Name);
    Assert.Equal(TestLibrary.Reference("talker"), second.Library);
    RoomPaths paths = TestLibrary.Paths(database.DataPath);
    Assert.Equal("{\"x\":1}", File.ReadAllText(paths.SettingsFile(project.Id, first.Id)));
    Assert.Equal("{\"x\":1}", File.ReadAllText(paths.SettingsFile(project.Id, second.Id)));
    Assert.True(Directory.Exists(paths.Table(project.Id, second.Id)));
    RoomMember editor = await room.RenameAsync(project.Id, second.Id, " Editor ");
    Assert.Equal("Editor", editor.Name);
    await Assert.ThrowsAsync<InvalidOperationException>(() => room.RenameAsync(project.Id, first.Id, "editor"));
    await Assert.ThrowsAsync<ArgumentException>(() => room.RenameAsync(project.Id, first.Id, "a/b"));
    await Assert.ThrowsAsync<ArgumentException>(() => room.RenameAsync(project.Id, first.Id, ".hidden"));
    await Assert.ThrowsAsync<ArgumentException>(() => room.RenameAsync(project.Id, first.Id, new string('a', RoomLimits.MaxNameLength + 1)));
  }

  [DatabaseFact]
  public async Task PlacingWithoutLibrarySettingsWritesAnEmptyObject()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    TestRoom setup = await CreateAsync(database);
    RoomStore room = setup.Room;
    Project project = setup.Project;
    RoomMember member = await room.PlaceAsync(project.Id, PlaceableTalker(database, null));
    Assert.Equal("{}", File.ReadAllText(TestLibrary.Paths(database.DataPath).SettingsFile(project.Id, member.Id)));
  }

  [DatabaseFact]
  public async Task ChannelsHaveUniqueNamesAndExplicitMembers()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    TestRoom setup = await CreateAsync(database);
    RoomStore room = setup.Room;
    Project project = setup.Project;
    ProjectStore projects = new(database.Database);
    Project other = await projects.CreateAsync(new ProjectInput("Other", null, null), Token);
    LibraryAgent talker = PlaceableTalker(database, null);
    RoomMember you = await room.EnsureYouAsync(project.Id);
    RoomMember a = await room.PlaceAsync(project.Id, talker);
    RoomMember b = await room.PlaceAsync(project.Id, talker);
    RoomMember foreign = await room.PlaceAsync(other.Id, talker);
    Channel group = await room.CreateChannelAsync(project.Id, "  Studio  ", [you.Id, a.Id, b.Id]);
    Assert.Equal("Studio", group.Name);
    Assert.False(group.IsLegacy);
    Assert.Equal(3, group.MemberIds.Count);
    Assert.Equal(group.Id, (await room.GetChannelAsync(project.Id, "studio")).Id);
    await Assert.ThrowsAsync<InvalidOperationException>(() => room.CreateChannelAsync(project.Id, "STUDIO", [a.Id]));
    await Assert.ThrowsAsync<KeyNotFoundException>(() => room.CreateChannelAsync(project.Id, "Abroad", [foreign.Id]));
    Channel renamed = await room.RenameChannelAsync(project.Id, group.Id, "Workshop");
    Assert.Equal("Workshop", renamed.Name);
    await Assert.ThrowsAsync<KeyNotFoundException>(() => room.GetChannelAsync(project.Id, "Studio"));
    await Assert.ThrowsAsync<KeyNotFoundException>(() => room.GetChannelAsync(other.Id, group.Id));
    Assert.Equal(2, (await room.RemoveChannelMemberAsync(project.Id, group.Id, b.Id)).MemberIds.Count);
    Assert.Equal(3, (await room.AddChannelMemberAsync(project.Id, group.Id, b.Id)).MemberIds.Count);
    Assert.True((await room.SetChannelPausedAsync(project.Id, group.Id, true)).IsPaused);
    Assert.Null(await room.FindPairChannelAsync(project.Id, a.Id, b.Id));
    Channel pair = await room.EnsurePairChannelAsync(project.Id, a.Id, b.Id);
    Assert.Equal($"{a.Name} & {b.Name}", pair.Name);
    Assert.Equal(pair.Id, (await room.EnsurePairChannelAsync(project.Id, b.Id, a.Id)).Id);
    await Assert.ThrowsAsync<ArgumentException>(() => room.EnsurePairChannelAsync(project.Id, a.Id, a.Id));
    ChannelSummary summary = Assert.Single(await room.ListChannelsAsync(project.Id), item => item.Id == pair.Id);
    Assert.Equal(0, summary.PacketCount);
    Assert.Null(summary.LastNote);
    await room.DeleteChannelAsync(project.Id, pair.Id);
    Assert.Single(await room.ListChannelsAsync(project.Id));
  }

  [DatabaseFact]
  public async Task RemovedMembersStayListedAndFreeTheirName()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    TestRoom setup = await CreateAsync(database);
    RoomStore room = setup.Room;
    Project project = setup.Project;
    RoomMember you = await room.EnsureYouAsync(project.Id);
    LibraryAgent talker = PlaceableTalker(database, null);
    RoomMember first = await room.PlaceAsync(project.Id, talker);
    Channel channel = await room.EnsurePairChannelAsync(project.Id, you.Id, first.Id);
    await room.RemoveAsync(project.Id, first.Id);
    List<RoomMember> members = await room.ListMembersAsync(project.Id);
    Assert.Equal(2, members.Count);
    Assert.True(members[0].IsYou);
    Assert.True(members[1].IsRemoved);
    Assert.Equal([you.Id], (await room.GetChannelAsync(project.Id, channel.Id)).MemberIds);
    RoomMember replacement = await room.PlaceAsync(project.Id, talker);
    Assert.Equal("Talker", replacement.Name);
    await Assert.ThrowsAsync<InvalidOperationException>(() => room.AddChannelMemberAsync(project.Id, channel.Id, first.Id));
    Assert.True(Directory.Exists(TestLibrary.Paths(database.DataPath).Table(project.Id, first.Id)));
  }

  [DatabaseFact]
  public async Task SessionsIdentifyMembers()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    TestRoom setup = await CreateAsync(database);
    RoomStore room = setup.Room;
    Project project = setup.Project;
    RoomMember a = await room.PlaceAsync(project.Id, PlaceableTalker(database, null));
    await room.BeginSessionAsync(a.Id, "h1");
    Assert.Equal(a, await room.FindBySessionAsync("h1"));
    await room.EndSessionAsync(a.Id);
    Assert.Null(await room.FindBySessionAsync("h1"));
    Assert.Empty(await room.ListShouldRunAsync());
    await room.SetShouldRunAsync(a.Id, true);
    Assert.Equal(a, Assert.Single(await room.ListShouldRunAsync()));
  }

  private static async Task<TestRoom> CreateAsync(TestDatabase database)
  {
    ProjectStore projects = new(database.Database);
    await projects.InitializeAsync(Token);
    RoomStore room = new(database.Database, TestLibrary.Paths(database.DataPath));
    await room.InitializeAsync(Token);
    Project project = await projects.CreateAsync(new ProjectInput("Room", null, null), Token);
    return new TestRoom(room, project);
  }

  private static LibraryAgent PlaceableTalker(TestDatabase database, string? settings)
  {
    TestLibrary.Write(database.DataPath, "talker", "Talker", ["python3", "${Agent}/agent.py"], settings);
    return TestLibrary.Agent(database.DataPath, "talker");
  }
}
