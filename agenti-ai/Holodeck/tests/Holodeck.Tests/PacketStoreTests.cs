using Holodeck.Web.Rooms;
using Xunit;

namespace Holodeck.Tests;

public sealed class PacketStoreTests
{
  private static readonly CancellationToken Token = CancellationToken.None;
  private static readonly List<OutgoingFile> NoFiles = new();

  [DatabaseFact]
  public async Task FansOutToEveryOtherMemberInOrderAcrossChannels()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    TestRoomSetup setup = await TestRoomSetup.CreateAsync(database);
    Channel group = await setup.Room.CreateChannelAsync(setup.Project.Id, "Group", [setup.You.Id, setup.A.Id, setup.B.Id]);
    Channel pair = await setup.Room.CreateChannelAsync(setup.Project.Id, "Pair", [setup.A.Id, setup.B.Id]);
    await setup.Packets.SendAsync(setup.You, group.Id, "one", NoFiles, Token);
    await setup.Packets.SendAsync(setup.B, "pair", "two", NoFiles, Token);
    Delivery first = (await setup.Packets.NextAsync(setup.A, "s1", Token))!;
    Assert.Equal([group.Id, "Group", "You", "one"], [first.ChannelId, first.Channel, first.From, first.Note]);
    Delivery second = (await setup.Packets.NextAsync(setup.A, "s1", Token))!;
    Assert.Equal("B", second.From);
    Assert.Equal(pair.Id, second.ChannelId);
    Assert.Null(await setup.Packets.NextAsync(setup.A, "s1", Token));
    Assert.Equal("one", (await setup.Packets.NextAsync(setup.B, "s2", Token))!.Note);
    Assert.Null(await setup.Packets.NextAsync(setup.B, "s2", Token));
  }

  [DatabaseFact]
  public async Task RedeliversTheInFlightPacketToANewSession()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    TestRoomSetup setup = await TestRoomSetup.CreateAsync(database);
    Channel channel = await setup.Room.EnsurePairChannelAsync(setup.Project.Id, setup.You.Id, setup.A.Id);
    await setup.Packets.SendAsync(setup.You, channel.Id, "one", NoFiles, Token);
    await setup.Packets.SendAsync(setup.You, channel.Id, "two", NoFiles, Token);
    Assert.Equal("one", (await setup.Packets.NextAsync(setup.A, "s1", Token))!.Note);
    Assert.Equal("one", (await setup.Packets.NextAsync(setup.A, "s2", Token))!.Note);
    Assert.Equal("two", (await setup.Packets.NextAsync(setup.A, "s2", Token))!.Note);
    Assert.Equal("two", (await setup.Packets.NextAsync(setup.A, "s3", Token))!.Note);
    Assert.Null(await setup.Packets.NextAsync(setup.A, "s3", Token));
  }

  [DatabaseFact]
  public async Task CopiesFilesPerRecipientSoLaterEditsDoNotLeak()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    TestRoomSetup setup = await TestRoomSetup.CreateAsync(database);
    Channel channel = await setup.Room.CreateChannelAsync(setup.Project.Id, "Desk", [setup.You.Id, setup.A.Id, setup.B.Id]);
    string tableA = setup.TableOf(setup.A);
    string tableB = setup.TableOf(setup.B);
    File.WriteAllText(Path.Combine(tableA, "draft.md"), "v1");
    Packet sent = await setup.Packets.SendAsync(setup.A, channel.Id, "", [TablePaths.Resolve(tableA, "draft.md")], Token);
    File.WriteAllText(Path.Combine(tableA, "draft.md"), "v2");
    string expected = $"received/{channel.Id}/A/draft.md";
    Delivery delivery = (await setup.Packets.NextAsync(setup.B, "s1", Token))!;
    Assert.Equal([expected], delivery.Files);
    Assert.Equal("v1", File.ReadAllText(Path.Combine(tableB, "received", channel.Id, "A", "draft.md")));
    Assert.Equal([expected], (await setup.Packets.NextAsync(setup.B, "s2", Token))!.Files);
    Packet stored = Assert.Single(await setup.Packets.HistoryAsync(setup.Project.Id, channel.Id, 0, Token));
    Assert.Equal(sent.Id, stored.Id);
    Assert.Equal(new PacketFile("draft.md", "text/markdown", 2), Assert.Single(stored.Files));
    Assert.Equal("v1", File.ReadAllText(setup.Packets.FilePath(setup.Project.Id, sent.Id, "draft.md")));
    Assert.Throws<KeyNotFoundException>(() => setup.Packets.FilePath(setup.Project.Id, sent.Id, "other.md"));
  }

  [DatabaseFact]
  public async Task NamesDuplicateFilesWithinOnePacket()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    TestRoomSetup setup = await TestRoomSetup.CreateAsync(database);
    Channel channel = await setup.Room.EnsurePairChannelAsync(setup.Project.Id, setup.A.Id, setup.B.Id);
    string tableA = setup.TableOf(setup.A);
    Directory.CreateDirectory(Path.Combine(tableA, "sub"));
    File.WriteAllText(Path.Combine(tableA, "notes.txt"), "top");
    File.WriteAllText(Path.Combine(tableA, "sub", "notes.txt"), "sub");
    Packet sent = await setup.Packets.SendAsync(setup.A, channel.Id, "two notes", [TablePaths.Resolve(tableA, "notes.txt"), TablePaths.Resolve(tableA, "sub/notes.txt")], Token);
    Assert.Equal("notes.txt", sent.Files[0].Name);
    Assert.Equal("notes (2).txt", sent.Files[1].Name);
    Assert.Equal("sub", File.ReadAllText(setup.Packets.FilePath(setup.Project.Id, sent.Id, "notes (2).txt")));
  }

  [DatabaseFact]
  public async Task RejectsBadSends()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    TestRoomSetup setup = await TestRoomSetup.CreateAsync(database);
    Channel channel = await setup.Room.EnsurePairChannelAsync(setup.Project.Id, setup.A.Id, setup.B.Id);
    await Assert.ThrowsAsync<KeyNotFoundException>(() => setup.Packets.SendByNameAsync(setup.A, "You", "no channel", NoFiles, Token));
    await Assert.ThrowsAsync<KeyNotFoundException>(() => setup.Packets.SendByNameAsync(setup.A, "Nobody", "missing", NoFiles, Token));
    await Assert.ThrowsAsync<KeyNotFoundException>(() => setup.Packets.SendAsync(setup.A, "missing", "no such channel", NoFiles, Token));
    await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Packets.SendAsync(setup.You, channel.Id, "not a member", NoFiles, Token));
    await Assert.ThrowsAsync<ArgumentException>(() => setup.Packets.SendAsync(setup.A, channel.Id, "", NoFiles, Token));
    await Assert.ThrowsAsync<ArgumentException>(() => setup.Packets.SendAsync(setup.A, channel.Id, new string('a', RoomLimits.MaxNoteCharacters + 1), NoFiles, Token));
    string tableA = setup.TableOf(setup.A);
    File.WriteAllText(Path.Combine(tableA, "f.txt"), "x");
    List<OutgoingFile> tooMany = new();
    for (int index = 0; index <= RoomLimits.MaxFilesPerPacket; index++)
    {
      tooMany.Add(TablePaths.Resolve(tableA, "f.txt"));
    }

    await Assert.ThrowsAsync<ArgumentException>(() => setup.Packets.SendAsync(setup.A, channel.Id, "many", tooMany, Token));
    Assert.Equal("legacy form", (await setup.Packets.SendByNameAsync(setup.A, "b", "legacy form", NoFiles, Token)).Note);
    await setup.Room.RemoveAsync(setup.Project.Id, setup.B.Id);
    await Assert.ThrowsAsync<KeyNotFoundException>(() => setup.Packets.SendByNameAsync(setup.A, "B", "gone", NoFiles, Token));
  }

  [DatabaseFact]
  public async Task PauseStopsAgentsButNotYou()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    TestRoomSetup setup = await TestRoomSetup.CreateAsync(database);
    Channel channel = await setup.Room.CreateChannelAsync(setup.Project.Id, "Loop", [setup.You.Id, setup.A.Id, setup.B.Id]);
    await setup.Packets.SendAsync(setup.A, channel.Id, "queued before pause", NoFiles, Token);
    await setup.Room.SetChannelPausedAsync(setup.Project.Id, channel.Id, true);
    await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Packets.SendAsync(setup.B, channel.Id, "answer", NoFiles, Token));
    await setup.Packets.SendAsync(setup.You, channel.Id, "stop, both of you", NoFiles, Token);
    Assert.Equal("queued before pause", (await setup.Packets.NextAsync(setup.B, "s1", Token))!.Note);
    Assert.Equal("stop, both of you", (await setup.Packets.NextAsync(setup.B, "s1", Token))!.Note);
    await setup.Room.SetChannelPausedAsync(setup.Project.Id, channel.Id, false);
    await setup.Packets.SendAsync(setup.B, channel.Id, "resumed", NoFiles, Token);
  }

  [DatabaseFact]
  public async Task RenamedChannelsStayReachableById()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    TestRoomSetup setup = await TestRoomSetup.CreateAsync(database);
    Channel channel = await setup.Room.CreateChannelAsync(setup.Project.Id, "Before", [setup.A.Id, setup.B.Id]);
    await setup.Room.RenameChannelAsync(setup.Project.Id, channel.Id, "After");
    await setup.Packets.SendAsync(setup.A, channel.Id, "by id", NoFiles, Token);
    await setup.Packets.SendAsync(setup.A, "after", "by new name", NoFiles, Token);
    await Assert.ThrowsAsync<KeyNotFoundException>(() => setup.Packets.SendAsync(setup.A, "Before", "by old name", NoFiles, Token));
    Delivery delivery = (await setup.Packets.NextAsync(setup.B, "s1", Token))!;
    Assert.Equal("After", delivery.Channel);
  }

  [DatabaseFact]
  public async Task RemovedMembersKeepQueuedPacketsOnly()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    TestRoomSetup setup = await TestRoomSetup.CreateAsync(database);
    Channel channel = await setup.Room.CreateChannelAsync(setup.Project.Id, "Desk", [setup.You.Id, setup.A.Id, setup.B.Id]);
    await setup.Packets.SendAsync(setup.You, channel.Id, "before", NoFiles, Token);
    await setup.Room.RemoveChannelMemberAsync(setup.Project.Id, channel.Id, setup.B.Id);
    await setup.Packets.SendAsync(setup.You, channel.Id, "after", NoFiles, Token);
    Assert.Equal("before", (await setup.Packets.NextAsync(setup.B, "s1", Token))!.Note);
    Assert.Null(await setup.Packets.NextAsync(setup.B, "s1", Token));
    await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Packets.SendAsync(setup.B, channel.Id, "let me in", NoFiles, Token));
  }

  [DatabaseFact]
  public async Task DeletingAChannelWithQueuedPacketsDoesNotStallDelivery()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    TestRoomSetup setup = await TestRoomSetup.CreateAsync(database);
    Channel old = await setup.Room.CreateChannelAsync(setup.Project.Id, "Old", [setup.You.Id, setup.A.Id]);
    await setup.Packets.SendAsync(setup.You, old.Id, "unread", NoFiles, Token);
    await setup.Room.DeleteChannelAsync(setup.Project.Id, old.Id);
    Channel fresh = await setup.Room.CreateChannelAsync(setup.Project.Id, "Fresh", [setup.You.Id, setup.A.Id]);
    await setup.Packets.SendAsync(setup.You, fresh.Id, "hi", NoFiles, Token);
    Assert.Equal("hi", (await setup.Packets.NextAsync(setup.A, "s1", Token))!.Note);
    Assert.Null(await setup.Packets.NextAsync(setup.A, "s1", Token));
  }

  [DatabaseFact]
  public async Task QueuedPacketsSurviveLeavingAndRestart()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    TestRoomSetup setup = await TestRoomSetup.CreateAsync(database);
    Channel channel = await setup.Room.EnsurePairChannelAsync(setup.Project.Id, setup.A.Id, setup.B.Id);
    await setup.Packets.SendAsync(setup.A, channel.Id, "queued", NoFiles, Token);
    await setup.Room.RemoveChannelMemberAsync(setup.Project.Id, channel.Id, setup.B.Id);
    await database.RestartAsync();
    PacketStore restarted = new(database.Database, new RoomStore(database.Database, setup.Paths), setup.Paths, new RoomSignals());
    Assert.Equal("queued", (await restarted.NextAsync(setup.B, "s1", Token))!.Note);
  }

  // Sends in one channel must commit in id order, or a reader paging by "after the last id" skips one.
  [DatabaseFact]
  public async Task SendsInOneChannelCommitInOrder()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    TestRoomSetup setup = await TestRoomSetup.CreateAsync(database);
    Channel channel = await setup.Room.EnsurePairChannelAsync(setup.Project.Id, setup.A.Id, setup.B.Id);
    await using Npgsql.NpgsqlConnection holder = await database.Database.OpenConnectionAsync();
    await using Npgsql.NpgsqlTransaction transaction = await holder.BeginTransactionAsync();
    // A send that holds the channel lock but has not committed yet, as while it copies a large file.
    await using (Npgsql.NpgsqlCommand open = new("SELECT 1 FROM room_channels WHERE id = $1 FOR UPDATE", holder, transaction))
    {
      open.Parameters.AddWithValue(channel.Id);
      await open.ExecuteNonQueryAsync();
    }

    Task<Packet> send = setup.Packets.SendAsync(setup.B, channel.Id, "waits for the channel", NoFiles, Token);
    Assert.NotSame(send, await Task.WhenAny(send, Task.Delay(500)));
    await transaction.CommitAsync();
    Assert.Equal("waits for the channel", (await send).Note);
  }

  [Fact]
  public void TablePathsRejectEscapes()
  {
    string root = Path.Combine(Path.GetTempPath(), "holodeck-table-" + Guid.NewGuid().ToString("N"));
    string table = Path.Combine(root, "table");
    try
    {
      Directory.CreateDirectory(Path.Combine(table, "sub"));
      File.WriteAllText(Path.Combine(table, "sub", "ok.txt"), "ok");
      File.WriteAllText(Path.Combine(root, "outside.txt"), "secret");
      File.CreateSymbolicLink(Path.Combine(table, "link"), Path.Combine(root, "outside.txt"));
      Directory.CreateSymbolicLink(Path.Combine(table, "linkdir"), root);
      Assert.Throws<ArgumentException>(() => TablePaths.Resolve(table, "../outside.txt"));
      Assert.Throws<ArgumentException>(() => TablePaths.Resolve(table, Path.Combine(root, "outside.txt")));
      Assert.Throws<ArgumentException>(() => TablePaths.Resolve(table, "sub/../../outside.txt"));
      Assert.Throws<ArgumentException>(() => TablePaths.Resolve(table, "link"));
      Assert.Throws<ArgumentException>(() => TablePaths.Resolve(table, "linkdir/outside.txt"));
      Assert.Throws<ArgumentException>(() => TablePaths.Resolve(table, "sub"));
      Assert.Throws<ArgumentException>(() => TablePaths.Resolve(table, "missing.txt"));
      Assert.Throws<ArgumentException>(() => TablePaths.Resolve(table, ""));
      OutgoingFile file = TablePaths.Resolve(table, "sub/ok.txt");
      Assert.Equal("ok.txt", file.Name);
      Assert.Equal(Path.Combine(table, "sub", "ok.txt"), file.SourcePath);
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }

  [DatabaseFact]
  public async Task HistoryPagesAfterAnId()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    TestRoomSetup setup = await TestRoomSetup.CreateAsync(database);
    Channel channel = await setup.Room.EnsurePairChannelAsync(setup.Project.Id, setup.You.Id, setup.A.Id);
    await setup.Packets.SendAsync(setup.You, channel.Id, "first", NoFiles, Token);
    Packet second = await setup.Packets.SendAsync(setup.A, channel.Id, "second", NoFiles, Token);
    await setup.Packets.SendAsync(setup.You, channel.Id, "third", NoFiles, Token);
    List<Packet> all = await setup.Packets.HistoryAsync(setup.Project.Id, channel.Id, 0, Token);
    Assert.Equal(3, all.Count);
    Assert.Equal("first", all[0].Note);
    Assert.Equal(setup.A.Id, all[1].SenderId);
    Assert.Equal("third", Assert.Single(await setup.Packets.HistoryAsync(setup.Project.Id, channel.Id, second.Id, Token)).Note);
    ChannelSummary summary = Assert.Single(await setup.Room.ListChannelsAsync(setup.Project.Id));
    Assert.Equal(3, summary.PacketCount);
    Assert.Equal("third", summary.LastNote);
    Assert.Equal(setup.You.Id, summary.LastSenderId);
  }

  [Fact]
  public async Task SignalsWakeWaitersAndTimeOutQuietly()
  {
    RoomSignals signals = new();
    Task member = signals.WaitForMemberAsync("m", TimeSpan.FromSeconds(5), Token);
    Task project = signals.WaitForProjectAsync("p", Token);
    signals.Pulse("p", "m");
    Assert.Same(member, await Task.WhenAny(member, Task.Delay(1000)));
    Assert.Same(project, await Task.WhenAny(project, Task.Delay(1000)));
    await signals.WaitForMemberAsync("m", TimeSpan.FromMilliseconds(50), Token);
    Task other = signals.WaitForProjectAsync("p", Token);
    signals.Pulse("p", "someone-else");
    Assert.Same(other, await Task.WhenAny(other, Task.Delay(1000)));
  }
}
