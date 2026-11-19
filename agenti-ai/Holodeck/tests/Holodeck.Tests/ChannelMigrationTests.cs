using Holodeck.Web.Rooms;
using Npgsql;
using Xunit;

namespace Holodeck.Tests;

public sealed class ChannelMigrationTests
{
  private static readonly CancellationToken Token = CancellationToken.None;

  // A room from before channels: You-A wired with an in-flight packet that carried a file, A-B unplugged.
  [DatabaseFact]
  public async Task WiresBecomeLegacyChannelsWithQueuesAndPathsIntact()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    TestRoomSetup setup = await TestRoomSetup.CreateAsync(database);
    string projectId = setup.Project.Id;
    bool youFirst = string.CompareOrdinal(setup.You.Id, setup.A.Id) < 0;
    bool aFirst = string.CompareOrdinal(setup.A.Id, setup.B.Id) < 0;
    await ExecuteAsync(database, "INSERT INTO room_wires(id, project_id, first_id, second_id, connected, created_at) VALUES ('w1', $1, $2, $3, true, now()), ('w2', $1, $4, $5, false, now())",
      projectId, youFirst ? setup.You.Id : setup.A.Id, youFirst ? setup.A.Id : setup.You.Id, aFirst ? setup.A.Id : setup.B.Id, aFirst ? setup.B.Id : setup.A.Id);
    await ExecuteAsync(database, "INSERT INTO room_packets(id, project_id, wire_id, sender_id, recipient_id, recipient_sequence, note, sent_at) VALUES (1, $1, 'w1', $2, $3, 1, 'with a file', now()), (2, $1, 'w1', $2, $3, 2, 'second', now())",
      projectId, setup.You.Id, setup.A.Id);
    await ExecuteAsync(database, "SELECT setval(pg_get_serial_sequence('room_packets', 'id'), 2)");
    await ExecuteAsync(database, "INSERT INTO room_packet_files(packet_id, ordinal, name, media_type, size, delivered_path) VALUES (1, 0, 'x.txt', 'text/plain', 1, 'received/You/x.txt')");
    await ExecuteAsync(database, "UPDATE room_members SET next_sequence = 2, done_sequence = 0, in_flight_sequence = 1, in_flight_session = 'old' WHERE id = $1", setup.A.Id);
    string received = Path.Combine(setup.TableOf(setup.A), "received", "You");
    Directory.CreateDirectory(received);
    File.WriteAllText(Path.Combine(received, "x.txt"), "x");
    // A removed member also called "A", and B renamed "A 2": numbered names must not collide with real ones.
    await ExecuteAsync(database, "INSERT INTO room_members(id, project_id, name, is_you, removed, created_at) VALUES ('gone', $1, 'A', false, true, now())", projectId);
    await ExecuteAsync(database, "UPDATE room_members SET name = 'A 2' WHERE id = $1", setup.B.Id);
    await ExecuteAsync(database, "INSERT INTO room_wires(id, project_id, first_id, second_id, connected, created_at) VALUES ('w3', $1, $2, $3, false, now() + interval '1 second'), ('w4', $1, $4, $5, true, now() + interval '2 seconds')",
      projectId, string.CompareOrdinal(setup.You.Id, "gone") < 0 ? setup.You.Id : "gone", string.CompareOrdinal(setup.You.Id, "gone") < 0 ? "gone" : setup.You.Id,
      string.CompareOrdinal(setup.You.Id, setup.B.Id) < 0 ? setup.You.Id : setup.B.Id, string.CompareOrdinal(setup.You.Id, setup.B.Id) < 0 ? setup.B.Id : setup.You.Id);
    await ExecuteAsync(database, "DELETE FROM room_migrations");

    await setup.Room.InitializeAsync(Token);

    Channel withYou = await setup.Room.GetChannelAsync(projectId, "w1");
    Assert.True(withYou.IsLegacy);
    Assert.Equal("A", withYou.Name);
    Assert.Equal(2, withYou.MemberIds.Count);
    Channel unplugged = await setup.Room.GetChannelAsync(projectId, "w2");
    Assert.Equal("A & A 2", unplugged.Name);
    List<string> names = new();
    foreach (ChannelSummary migrated in await setup.Room.ListChannelsAsync(projectId))
    {
      Assert.DoesNotContain(migrated.Name, names);
      names.Add(migrated.Name);
    }

    Assert.Equal(4, names.Count);
    Assert.Empty(unplugged.MemberIds);
    Delivery redelivered = (await setup.Packets.NextAsync(setup.A, "new", Token))!;
    Assert.Equal(["w1", "A", "You", "with a file"], [redelivered.ChannelId, redelivered.Channel, redelivered.From, redelivered.Note]);
    Assert.Equal(["received/You/x.txt"], redelivered.Files);
    Assert.Equal("second", (await setup.Packets.NextAsync(setup.A, "new", Token))!.Note);
    Assert.Equal(2, (await setup.Packets.HistoryAsync(projectId, "w1", 0, Token)).Count);
    Assert.Equal("w1", (await setup.Packets.SendByNameAsync(setup.A, "You", "legacy reply", [], Token)).ChannelId);
    await setup.Room.InitializeAsync(Token);
    Assert.Equal(4, (await setup.Room.ListChannelsAsync(projectId)).Count);
  }

  private static async Task ExecuteAsync(TestDatabase database, string sql, params string[] values)
  {
    await using NpgsqlCommand command = database.Database.CreateCommand(sql);
    foreach (string value in values)
    {
      command.Parameters.AddWithValue(value);
    }

    await command.ExecuteNonQueryAsync();
  }
}
