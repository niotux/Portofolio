using Holodeck.Web.Rooms;
using Npgsql;
using Xunit;

namespace Holodeck.Tests;

// Transaction boundaries, lock ordering, and table containment for sends.
public sealed class SendSafetyTests
{
  private const string Sentinel = "outside sentinel";
  private static readonly CancellationToken Token = CancellationToken.None;

  [DatabaseFact]
  public async Task AFailedCommitKeepsItsStagedFilesAndTheKeyStaysUsable()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    TestRoomSetup setup = await TestRoomSetup.CreateAsync(database);
    Channel channel = await setup.Room.CreateChannelAsync(setup.Project.Id, "Desk", [setup.A.Id, setup.B.Id]);
    File.WriteAllText(Path.Combine(setup.TableOf(setup.B), "draft.md"), "draft");
    // PostgreSQL rejects this transaction only at COMMIT, after the files were staged.
    await database.ExecuteAsync("CREATE FUNCTION refuse_files() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'injected'; END $$; CREATE CONSTRAINT TRIGGER refuse_at_commit AFTER INSERT ON room_packet_files DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION refuse_files();");

    await Assert.ThrowsAsync<PostgresException>(() => SendDraftAsync(setup, channel));
    Assert.Equal(0, await database.CountAsync("SELECT count(*) FROM room_packets"));
    string staged = Assert.Single(Directory.GetDirectories(PacketsRoot(setup)));
    Assert.Equal("draft", File.ReadAllText(Path.Combine(staged, "draft.md")));

    await database.ExecuteAsync("DROP TRIGGER refuse_at_commit ON room_packet_files");
    SendResult result = await SendDraftAsync(setup, channel);
    Assert.False(result.IsReplay);
    Assert.NotEqual(staged, setup.Paths.PacketDirectory(setup.Project.Id, result.Packet.Id));
    Assert.Equal("draft", File.ReadAllText(Path.Combine(setup.Paths.PacketDirectory(setup.Project.Id, result.Packet.Id), "draft.md")));
  }

  [DatabaseFact]
  public async Task AFailureBeforeCommitRemovesItsStagedFiles()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    TestRoomSetup setup = await TestRoomSetup.CreateAsync(database);
    Channel channel = await setup.Room.CreateChannelAsync(setup.Project.Id, "Desk", [setup.A.Id, setup.B.Id]);
    File.WriteAllText(Path.Combine(setup.TableOf(setup.B), "draft.md"), "draft");
    await database.ExecuteAsync("CREATE FUNCTION refuse_files() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'injected'; END $$; CREATE TRIGGER refuse_now AFTER INSERT ON room_packet_files FOR EACH ROW EXECUTE FUNCTION refuse_files();");

    await Assert.ThrowsAsync<PostgresException>(() => SendDraftAsync(setup, channel));
    Assert.Equal(0, await database.CountAsync("SELECT count(*) FROM room_packets"));
    Assert.Empty(Directory.GetDirectories(PacketsRoot(setup)));
  }

  [DatabaseFact]
  public async Task ASendWaitingOnTheChannelSeesAMembershipRemovalCommittedMeanwhile()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    TestRoomSetup setup = await TestRoomSetup.CreateAsync(database);
    Channel channel = await setup.Room.CreateChannelAsync(setup.Project.Id, "Desk", [setup.A.Id, setup.B.Id]);
    await using NpgsqlConnection holder = await database.Database.OpenConnectionAsync();
    await using NpgsqlTransaction transaction = await LockChannelAsync(holder, channel.Id);

    Task<Packet> send = setup.Packets.SendAsync(setup.B, channel.Id, "after removal", [], Token);
    Assert.True(await IsBlockedByAsync(database, holder));
    await using (NpgsqlCommand remove = new("DELETE FROM room_channel_members WHERE channel_id = $1 AND member_id = $2", holder, transaction))
    {
      remove.Parameters.AddWithValue(channel.Id);
      remove.Parameters.AddWithValue(setup.B.Id);
      await remove.ExecuteNonQueryAsync();
    }

    await transaction.CommitAsync();
    await Assert.ThrowsAsync<InvalidOperationException>(() => send);
    Assert.Equal(0, await database.CountAsync("SELECT count(*) FROM room_packets"));
  }

  [DatabaseFact]
  public async Task ASendWaitingOnTheChannelSeesAPauseCommittedMeanwhile()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    TestRoomSetup setup = await TestRoomSetup.CreateAsync(database);
    Channel channel = await setup.Room.CreateChannelAsync(setup.Project.Id, "Desk", [setup.A.Id, setup.B.Id]);
    await using NpgsqlConnection holder = await database.Database.OpenConnectionAsync();
    await using NpgsqlTransaction transaction = await LockChannelAsync(holder, channel.Id);

    Task<Packet> send = setup.Packets.SendAsync(setup.B, channel.Id, "after pause", [], Token);
    Assert.True(await IsBlockedByAsync(database, holder));
    await using (NpgsqlCommand pause = new("UPDATE room_channels SET paused = true WHERE id = $1", holder, transaction))
    {
      pause.Parameters.AddWithValue(channel.Id);
      await pause.ExecuteNonQueryAsync();
    }

    await transaction.CommitAsync();
    await Assert.ThrowsAsync<InvalidOperationException>(() => send);
    Assert.Equal(0, await database.CountAsync("SELECT count(*) FROM room_packets"));
  }

  [DatabaseFact]
  public async Task MembershipChangesWaitForASendHoldingTheChannel()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    TestRoomSetup setup = await TestRoomSetup.CreateAsync(database);
    Channel desk = await setup.Room.CreateChannelAsync(setup.Project.Id, "Desk", [setup.A.Id, setup.B.Id]);
    Channel other = await setup.Room.CreateChannelAsync(setup.Project.Id, "Other", [setup.You.Id, setup.A.Id]);

    await using (NpgsqlConnection holder = await database.Database.OpenConnectionAsync())
    {
      await using NpgsqlTransaction transaction = await LockChannelAsync(holder, desk.Id);
      Task<Channel> removeMember = setup.Room.RemoveChannelMemberAsync(setup.Project.Id, desk.Id, setup.B.Id);
      Assert.True(await IsBlockedByAsync(database, holder));
      await transaction.CommitAsync();
      Assert.DoesNotContain(setup.B.Id, (await removeMember).MemberIds);
    }

    await using (NpgsqlConnection holder = await database.Database.OpenConnectionAsync())
    {
      await using NpgsqlTransaction transaction = await LockChannelAsync(holder, other.Id);
      Task addMember = setup.Room.AddChannelMemberAsync(setup.Project.Id, other.Id, setup.B.Id);
      Assert.True(await IsBlockedByAsync(database, holder));
      await transaction.CommitAsync();
      await addMember;
    }

    await using (NpgsqlConnection holder = await database.Database.OpenConnectionAsync())
    {
      await using NpgsqlTransaction transaction = await LockChannelAsync(holder, other.Id);
      Task removeAgent = setup.Room.RemoveAsync(setup.Project.Id, setup.A.Id);
      Assert.True(await IsBlockedByAsync(database, holder));
      await transaction.CommitAsync();
      await removeAgent;
    }

    Assert.Equal(Sorted([setup.You.Id, setup.B.Id]), Sorted((await setup.Room.GetChannelAsync(setup.Project.Id, other.Id)).MemberIds));
  }

  [DatabaseFact]
  public async Task AddingAMemberWhileRemovingItCannotLeaveRemovedMembershipBehind()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    TestRoomSetup setup = await TestRoomSetup.CreateAsync(database);
    Channel channel = await setup.Room.CreateChannelAsync(setup.Project.Id, "Desk", [setup.You.Id, setup.A.Id]);
    await using NpgsqlConnection holder = await database.Database.OpenConnectionAsync();
    await using NpgsqlTransaction transaction = await LockChannelAsync(holder, channel.Id);

    Task<Channel> add = setup.Room.AddChannelMemberAsync(setup.Project.Id, channel.Id, setup.B.Id);
    Assert.True(await IsBlockedByAsync(database, holder));
    Task remove = setup.Room.RemoveAsync(setup.Project.Id, setup.B.Id);
    // Either removal finishes (the former bug) or it waits behind the membership addition. Wait for that boundary.
    Assert.True(await CompletedOrQueuedBehindAsync(database, holder, remove));

    await transaction.CommitAsync();
    await add;
    await remove;
    Assert.True((await setup.Room.GetMemberAsync(setup.Project.Id, setup.B.Id)).IsRemoved);
    Assert.DoesNotContain(setup.B.Id, (await setup.Room.GetChannelAsync(setup.Project.Id, channel.Id)).MemberIds);
  }

  [DatabaseFact]
  public async Task CreatingAChannelWhileRemovingAMemberCannotReintroduceIt()
  {
    await CheckConversationCreationDuringRemovalAsync(false);
  }

  [DatabaseFact]
  public async Task CreatingAPairWhileRemovingAMemberCannotReintroduceIt()
  {
    await CheckConversationCreationDuringRemovalAsync(true);
  }

  private static async Task CheckConversationCreationDuringRemovalAsync(bool pair)
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    TestRoomSetup setup = await TestRoomSetup.CreateAsync(database);
    await using NpgsqlConnection holder = await database.Database.OpenConnectionAsync();
    await using NpgsqlTransaction transaction = await holder.BeginTransactionAsync();
    await using (NpgsqlCommand lockMember = new("SELECT id FROM room_members WHERE id = $1 FOR NO KEY UPDATE", holder, transaction))
    {
      lockMember.Parameters.AddWithValue(setup.B.Id);
      await lockMember.ExecuteScalarAsync();
    }

    // Removal has finished discovering and deleting old memberships when it blocks updating the member row.
    Task remove = setup.Room.RemoveAsync(setup.Project.Id, setup.B.Id);
    Assert.True(await IsBlockedByAsync(database, holder));
    Task<Channel> create = pair
      ? setup.Room.EnsurePairChannelAsync(setup.Project.Id, setup.You.Id, setup.B.Id)
      : setup.Room.CreateChannelAsync(setup.Project.Id, "New conversation", [setup.You.Id, setup.B.Id]);
    Assert.True(await CompletedOrQueuedBehindAsync(database, holder, create));

    await transaction.CommitAsync();
    await remove;
    await Assert.ThrowsAsync<InvalidOperationException>(() => create);
    Assert.Empty(await setup.Room.ListChannelsAsync(setup.Project.Id));
  }

  [DatabaseFact]
  public async Task AFileReplacedByALinkAfterItWasCheckedIsNeverRead()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    TestRoomSetup setup = await TestRoomSetup.CreateAsync(database);
    Channel channel = await setup.Room.CreateChannelAsync(setup.Project.Id, "Desk", [setup.A.Id, setup.B.Id]);
    string table = setup.TableOf(setup.B);
    string outside = WriteSentinel(database);
    File.WriteAllText(Path.Combine(table, "notes.txt"), "notes");
    OutgoingFile checkedFile = TablePaths.Resolve(table, "notes.txt");
    File.Delete(Path.Combine(table, "notes.txt"));
    File.CreateSymbolicLink(Path.Combine(table, "notes.txt"), Path.Combine(outside, "notes.txt"));

    await Assert.ThrowsAsync<ArgumentException>(() => setup.Packets.SendAsync(setup.B, channel.Id, "swapped", [checkedFile], Token));
    Assert.Equal(0, await database.CountAsync("SELECT count(*) FROM room_packets"));
    Assert.Empty(Directory.GetDirectories(PacketsRoot(setup)));
  }

  [DatabaseFact]
  public async Task AFolderReplacedByALinkAfterItWasCheckedIsNeverRead()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    TestRoomSetup setup = await TestRoomSetup.CreateAsync(database);
    Channel channel = await setup.Room.CreateChannelAsync(setup.Project.Id, "Desk", [setup.A.Id, setup.B.Id]);
    string table = setup.TableOf(setup.B);
    string outside = WriteSentinel(database);
    Directory.CreateDirectory(Path.Combine(table, "drafts"));
    File.WriteAllText(Path.Combine(table, "drafts", "notes.txt"), "notes");
    OutgoingFile checkedFile = TablePaths.Resolve(table, "drafts/notes.txt");
    Directory.Move(Path.Combine(table, "drafts"), Path.Combine(table, "drafts-moved"));
    Directory.CreateSymbolicLink(Path.Combine(table, "drafts"), outside);

    await Assert.ThrowsAsync<ArgumentException>(() => setup.Packets.SendAsync(setup.B, channel.Id, "swapped", [checkedFile], Token));
    Assert.Equal(0, await database.CountAsync("SELECT count(*) FROM room_packets"));
    Assert.Empty(Directory.GetDirectories(PacketsRoot(setup)));
  }

  [DatabaseFact]
  public async Task AKeyedRetryNeverReadsALinkedReplacement()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    TestRoomSetup setup = await TestRoomSetup.CreateAsync(database);
    Channel channel = await setup.Room.CreateChannelAsync(setup.Project.Id, "Desk", [setup.A.Id, setup.B.Id]);
    string table = setup.TableOf(setup.B);
    string outside = WriteSentinel(database);
    // The original carries the sentinel's exact bytes, so reading the link would yield a matching replay.
    File.WriteAllText(Path.Combine(table, "notes.txt"), Sentinel);
    SendResult original = await setup.Packets.SendAsync(setup.B, channel.Id, "notes", [TablePaths.Resolve(table, "notes.txt")], "retry-key", Token);
    OutgoingFile checkedFile = TablePaths.Resolve(table, "notes.txt");
    File.Delete(Path.Combine(table, "notes.txt"));
    File.CreateSymbolicLink(Path.Combine(table, "notes.txt"), Path.Combine(outside, "notes.txt"));

    await Assert.ThrowsAsync<ArgumentException>(() => setup.Packets.SendAsync(setup.B, channel.Id, "notes", [checkedFile], "retry-key", Token));
    Assert.False(original.IsReplay);
    Assert.Equal(1, await database.CountAsync("SELECT count(*) FROM room_packets"));
  }

  [DatabaseFact]
  public async Task AKeyedRetryNeverReadsALinkedFolderReplacement()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    TestRoomSetup setup = await TestRoomSetup.CreateAsync(database);
    Channel channel = await setup.Room.CreateChannelAsync(setup.Project.Id, "Desk", [setup.A.Id, setup.B.Id]);
    string table = setup.TableOf(setup.B);
    string outside = WriteSentinel(database);
    Directory.CreateDirectory(Path.Combine(table, "drafts"));
    File.WriteAllText(Path.Combine(table, "drafts", "notes.txt"), Sentinel);
    await setup.Packets.SendAsync(setup.B, channel.Id, "notes", [TablePaths.Resolve(table, "drafts/notes.txt")], "retry-key", Token);
    OutgoingFile checkedFile = TablePaths.Resolve(table, "drafts/notes.txt");
    Directory.Move(Path.Combine(table, "drafts"), Path.Combine(table, "drafts-moved"));
    Directory.CreateSymbolicLink(Path.Combine(table, "drafts"), outside);

    await Assert.ThrowsAsync<ArgumentException>(() => setup.Packets.SendAsync(setup.B, channel.Id, "notes", [checkedFile], "retry-key", Token));
    Assert.Equal(1, await database.CountAsync("SELECT count(*) FROM room_packets"));
  }

  private static Task<SendResult> SendDraftAsync(TestRoomSetup setup, Channel channel)
  {
    OutgoingFile draft = TablePaths.Resolve(setup.TableOf(setup.B), "draft.md");
    return setup.Packets.SendAsync(setup.B, channel.Id, "the draft", [draft], "staged-key", Token);
  }

  private static string PacketsRoot(TestRoomSetup setup)
  {
    string root = Path.GetDirectoryName(setup.Paths.PacketDirectory(setup.Project.Id, 1))!;
    Directory.CreateDirectory(root);
    return root;
  }

  // A harmless file outside every table, standing in for anything an agent must not reach.
  private static string WriteSentinel(TestDatabase database)
  {
    string outside = Path.Combine(database.DataPath, "outside");
    Directory.CreateDirectory(outside);
    File.WriteAllText(Path.Combine(outside, "notes.txt"), Sentinel);
    return outside;
  }

  private static async Task<NpgsqlTransaction> LockChannelAsync(NpgsqlConnection holder, string channelId)
  {
    NpgsqlTransaction transaction = await holder.BeginTransactionAsync();
    await using NpgsqlCommand command = new("SELECT id FROM room_channels WHERE id = $1 FOR UPDATE", holder, transaction);
    command.Parameters.AddWithValue(channelId);
    await command.ExecuteScalarAsync();
    return transaction;
  }

  // The operation either finished or queued behind the transaction already waiting for holder. Following that
  // exact blocking chain avoids mistaking another test's unrelated database work for this operation's boundary.
  private static async Task<bool> CompletedOrQueuedBehindAsync(TestDatabase database, NpgsqlConnection holder, Task operation)
  {
    DateTime until = DateTime.UtcNow.AddSeconds(10);
    while (DateTime.UtcNow < until)
    {
      if (operation.IsCompleted)
      {
        return true;
      }

      await using NpgsqlCommand command = database.Database.CreateCommand("SELECT count(*) FROM pg_stat_activity waiter JOIN pg_stat_activity blocked ON blocked.pid = ANY(pg_blocking_pids(waiter.pid)) WHERE $1 = ANY(pg_blocking_pids(blocked.pid))");
      command.Parameters.AddWithValue(holder.ProcessID);
      if ((long)(await command.ExecuteScalarAsync())! > 0)
      {
        return true;
      }

      await Task.Delay(20);
    }

    return false;
  }

  // True once another session is waiting on a lock the holder owns.
  private static async Task<bool> IsBlockedByAsync(TestDatabase database, NpgsqlConnection holder)
  {
    int holderPid = holder.ProcessID;
    DateTime until = DateTime.UtcNow.AddSeconds(10);
    while (DateTime.UtcNow < until)
    {
      await using NpgsqlCommand command = database.Database.CreateCommand("SELECT count(*) FROM pg_stat_activity WHERE $1 = ANY(pg_blocking_pids(pid))");
      command.Parameters.AddWithValue(holderPid);
      if ((long)(await command.ExecuteScalarAsync())! > 0)
      {
        return true;
      }

      await Task.Delay(20);
    }

    return false;
  }

  private static List<string> Sorted(List<string> ids)
  {
    List<string> sorted = new(ids);
    sorted.Sort(StringComparer.Ordinal);
    return sorted;
  }
}
