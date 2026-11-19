using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Holodeck.Web.Rooms;
using Xunit;

namespace Holodeck.Tests;

public sealed class SendIdempotencyTests
{
  private const string SendPath = "/api/agent/send";

  [DatabaseFact]
  public async Task RetryAfterCommitReturnsTheOriginalPacketWithoutSendingAgain()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    await using TestHost host = await TestHost.StartAsync(database);
    TestRoomSetup setup = await TestRoomSetup.CreateAsync(database);
    Channel channel = await setup.Room.CreateChannelAsync(setup.Project.Id, "Desk", [setup.A.Id, setup.B.Id]);
    File.WriteAllText(Path.Combine(setup.TableOf(setup.B), "draft.md"), "first draft");
    using HttpClient sender = await ClientForAsync(host, setup, setup.B, "sender-session");
    AgentIdentity identity = (await sender.GetFromJsonAsync<AgentIdentity>("/api/agent/me"))!;
    Assert.NotNull(identity.Capabilities);
    Assert.Contains(AgentCapabilities.IdempotentSend, identity.Capabilities);
    Assert.Contains(AgentCapabilities.ManagementJobs, identity.Capabilities);

    SendInput input = new(channel.Id, null, "see the draft", ["draft.md"], "outbox-1");

    // The first response is treated as lost; the retry must report the same committed packet.
    HttpResponseMessage first = await sender.PostAsJsonAsync(SendPath, input);
    Assert.Equal(HttpStatusCode.Created, first.StatusCode);
    Packet original = (await first.Content.ReadFromJsonAsync<Packet>())!;
    HttpResponseMessage retry = await sender.PostAsJsonAsync(SendPath, input);
    Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
    Packet replayed = (await retry.Content.ReadFromJsonAsync<Packet>())!;

    Assert.Equal(original.Id, replayed.Id);
    Assert.Equal(original.SentAt, replayed.SentAt);
    Assert.Equal(original.Note, replayed.Note);
    Assert.Equal(original.Files, replayed.Files);
    Assert.Equal(1, await database.CountAsync("SELECT count(*) FROM room_packets"));
    Assert.Equal(1, await database.CountAsync("SELECT count(*) FROM room_deliveries"));
    Assert.Single(Directory.GetFiles(setup.Paths.PacketDirectory(setup.Project.Id, original.Id)));

    using HttpClient receiver = await ClientForAsync(host, setup, setup.A, "receiver-session");
    Delivery delivery = (await receiver.GetFromJsonAsync<Delivery>("/api/agent/next?wait=0"))!;
    Assert.Equal(original.Id, delivery.PacketId);
    Assert.Equal(HttpStatusCode.NoContent, (await receiver.GetAsync("/api/agent/next?wait=0")).StatusCode);
  }

  [DatabaseFact]
  public async Task KeySurvivesANewBearerSession()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    await using TestHost host = await TestHost.StartAsync(database);
    TestRoomSetup setup = await TestRoomSetup.CreateAsync(database);
    Channel channel = await setup.Room.CreateChannelAsync(setup.Project.Id, "Desk", [setup.A.Id, setup.B.Id]);
    SendInput input = new(channel.Id, null, "before restart", null, "outbox-restart");
    using HttpClient before = await ClientForAsync(host, setup, setup.B, "first-start");
    Packet original = await SendAsync(before, input, HttpStatusCode.Created);

    using HttpClient after = await ClientForAsync(host, setup, setup.B, "second-start");
    Assert.Equal(HttpStatusCode.Unauthorized, (await before.PostAsJsonAsync(SendPath, input)).StatusCode);
    Packet replayed = await SendAsync(after, input, HttpStatusCode.OK);
    Assert.Equal(original.Id, replayed.Id);
    Assert.Equal(1, await database.CountAsync("SELECT count(*) FROM room_packets"));
  }

  [DatabaseFact]
  public async Task ReusingAKeyForDifferentContentIsAConflict()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    await using TestHost host = await TestHost.StartAsync(database);
    TestRoomSetup setup = await TestRoomSetup.CreateAsync(database);
    Channel desk = await setup.Room.CreateChannelAsync(setup.Project.Id, "Desk", [setup.A.Id, setup.B.Id]);
    Channel other = await setup.Room.CreateChannelAsync(setup.Project.Id, "Other", [setup.A.Id, setup.B.Id]);
    string table = setup.TableOf(setup.B);
    File.WriteAllText(Path.Combine(table, "one.txt"), "one");
    File.WriteAllText(Path.Combine(table, "two.txt"), "two");
    using HttpClient sender = await ClientForAsync(host, setup, setup.B, "sender-session");
    SendInput input = new(desk.Id, null, "two files", ["one.txt", "two.txt"], "outbox-conflict");
    Packet original = await SendAsync(sender, input, HttpStatusCode.Created);

    Assert.Equal(HttpStatusCode.Conflict, (await sender.PostAsJsonAsync(SendPath, input with { Note = "other words" })).StatusCode);
    Assert.Equal(HttpStatusCode.Conflict, (await sender.PostAsJsonAsync(SendPath, input with { Channel = other.Id })).StatusCode);
    Assert.Equal(HttpStatusCode.Conflict, (await sender.PostAsJsonAsync(SendPath, input with { Files = ["two.txt", "one.txt"] })).StatusCode);
    Assert.Equal(HttpStatusCode.Conflict, (await sender.PostAsJsonAsync(SendPath, input with { Files = ["one.txt"] })).StatusCode);
    File.WriteAllText(Path.Combine(table, "one.txt"), "changed");
    Assert.Equal(HttpStatusCode.Conflict, (await sender.PostAsJsonAsync(SendPath, input)).StatusCode);

    // The channel name resolves to the same channel, and restored bytes match what was actually stored.
    File.WriteAllText(Path.Combine(table, "one.txt"), "one");
    Packet replayed = await SendAsync(sender, input with { Channel = "Desk" }, HttpStatusCode.OK);
    Assert.Equal(original.Id, replayed.Id);
    Assert.Equal(1, await database.CountAsync("SELECT count(*) FROM room_packets"));
    Assert.Equal(2, await database.CountAsync("SELECT count(*) FROM room_packet_files"));
  }

  [DatabaseFact]
  public async Task KeysAreScopedToTheirSender()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    await using TestHost host = await TestHost.StartAsync(database);
    TestRoomSetup setup = await TestRoomSetup.CreateAsync(database);
    Channel channel = await setup.Room.CreateChannelAsync(setup.Project.Id, "Desk", [setup.A.Id, setup.B.Id]);
    using HttpClient a = await ClientForAsync(host, setup, setup.A, "a-session");
    using HttpClient b = await ClientForAsync(host, setup, setup.B, "b-session");
    SendInput input = new(channel.Id, null, "same words", null, "shared-key");

    Packet fromA = await SendAsync(a, input, HttpStatusCode.Created);
    Packet fromB = await SendAsync(b, input, HttpStatusCode.Created);
    Packet again = await SendAsync(b, input with { IdempotencyKey = "another-key" }, HttpStatusCode.Created);
    Assert.NotEqual(fromA.Id, fromB.Id);
    Assert.NotEqual(fromB.Id, again.Id);
    Assert.Equal(3, await database.CountAsync("SELECT count(*) FROM room_packets"));
  }

  [DatabaseFact]
  public async Task ConcurrentRetriesCommitOnePacket()
  {
    const int Attempts = 8;
    await using TestDatabase database = await TestDatabase.CreateAsync();
    await using TestHost host = await TestHost.StartAsync(database);
    TestRoomSetup setup = await TestRoomSetup.CreateAsync(database);
    Channel channel = await setup.Room.CreateChannelAsync(setup.Project.Id, "Desk", [setup.You.Id, setup.A.Id, setup.B.Id]);
    File.WriteAllText(Path.Combine(setup.TableOf(setup.B), "report.txt"), new string('x', 200000));
    using HttpClient sender = await ClientForAsync(host, setup, setup.B, "sender-session");
    SendInput input = new(channel.Id, null, "the report", ["report.txt"], "outbox-concurrent");

    List<Task<HttpResponseMessage>> attempts = new();
    for (int index = 0; index < Attempts; index++)
    {
      attempts.Add(sender.PostAsJsonAsync(SendPath, input));
    }

    HttpResponseMessage[] responses = await Task.WhenAll(attempts);
    int created = 0;
    HashSet<long> ids = new();
    foreach (HttpResponseMessage response in responses)
    {
      Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
      if (response.StatusCode == HttpStatusCode.Created)
      {
        created++;
      }

      ids.Add((await response.Content.ReadFromJsonAsync<Packet>())!.Id);
    }

    Assert.Equal(1, created);
    long packetId = Assert.Single(ids);
    Assert.Equal(1, await database.CountAsync("SELECT count(*) FROM room_packets"));
    Assert.Equal(2, await database.CountAsync("SELECT count(*) FROM room_deliveries"));
    Assert.Equal(1, await database.CountAsync("SELECT count(*) FROM room_packet_files"));
    Assert.Single(Directory.GetFiles(setup.Paths.PacketDirectory(setup.Project.Id, packetId)));
  }

  [DatabaseFact]
  public async Task ReplaysNeverAuthorizeNewSends()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    await using TestHost host = await TestHost.StartAsync(database);
    TestRoomSetup setup = await TestRoomSetup.CreateAsync(database);
    Channel channel = await setup.Room.CreateChannelAsync(setup.Project.Id, "Desk", [setup.A.Id, setup.B.Id]);
    using HttpClient sender = await ClientForAsync(host, setup, setup.B, "sender-session");
    SendInput input = new(channel.Id, null, "while paused", null, "outbox-paused");

    // A refused first attempt stores nothing, so the same key can still commit once allowed.
    await setup.Room.SetChannelPausedAsync(setup.Project.Id, channel.Id, true);
    Assert.Equal(HttpStatusCode.Conflict, (await sender.PostAsJsonAsync(SendPath, input)).StatusCode);
    Assert.Equal(0, await database.CountAsync("SELECT count(*) FROM room_packets"));
    await setup.Room.SetChannelPausedAsync(setup.Project.Id, channel.Id, false);
    Packet original = await SendAsync(sender, input, HttpStatusCode.Created);

    // A committed send may be reported again, but a new key needs current permission.
    await setup.Room.SetChannelPausedAsync(setup.Project.Id, channel.Id, true);
    Assert.Equal(original.Id, (await SendAsync(sender, input, HttpStatusCode.OK)).Id);
    Assert.Equal(HttpStatusCode.Conflict, (await sender.PostAsJsonAsync(SendPath, input with { IdempotencyKey = "outbox-new" })).StatusCode);
    await setup.Room.SetChannelPausedAsync(setup.Project.Id, channel.Id, false);
    await setup.Room.RemoveChannelMemberAsync(setup.Project.Id, channel.Id, setup.B.Id);
    Assert.Equal(original.Id, (await SendAsync(sender, input, HttpStatusCode.OK)).Id);
    Assert.Equal(HttpStatusCode.Conflict, (await sender.PostAsJsonAsync(SendPath, input with { IdempotencyKey = "outbox-other" })).StatusCode);
    Assert.Equal(1, await database.CountAsync("SELECT count(*) FROM room_packets"));
    Assert.Equal(1, await database.CountAsync("SELECT count(*) FROM room_deliveries"));
  }

  [DatabaseFact]
  public async Task MalformedKeysAreRejectedWithoutSending()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    await using TestHost host = await TestHost.StartAsync(database);
    TestRoomSetup setup = await TestRoomSetup.CreateAsync(database);
    Channel channel = await setup.Room.CreateChannelAsync(setup.Project.Id, "Desk", [setup.A.Id, setup.B.Id]);
    using HttpClient sender = await ClientForAsync(host, setup, setup.B, "sender-session");
    SendInput input = new(channel.Id, null, "hello", null);

    Assert.Equal(HttpStatusCode.BadRequest, (await sender.PostAsJsonAsync(SendPath, input with { IdempotencyKey = "" })).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest, (await sender.PostAsJsonAsync(SendPath, input with { IdempotencyKey = "has space" })).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest, (await sender.PostAsJsonAsync(SendPath, input with { IdempotencyKey = new string('k', RoomLimits.MaxIdempotencyKeyLength + 1) })).StatusCode);
    Assert.Equal(0, await database.CountAsync("SELECT count(*) FROM room_packets"));
    await SendAsync(sender, input with { IdempotencyKey = new string('k', RoomLimits.MaxIdempotencyKeyLength) }, HttpStatusCode.Created);
  }

  private static async Task<Packet> SendAsync(HttpClient client, SendInput input, HttpStatusCode expected)
  {
    HttpResponseMessage response = await client.PostAsJsonAsync(SendPath, input);
    Assert.True(response.StatusCode == expected, await response.Content.ReadAsStringAsync());
    return (await response.Content.ReadFromJsonAsync<Packet>())!;
  }

  private static async Task<HttpClient> ClientForAsync(TestHost host, TestRoomSetup setup, RoomMember member, string key)
  {
    await setup.Room.BeginSessionAsync(member.Id, AgentKeys.Hash(key));
    HttpClient client = new() { BaseAddress = host.Client.BaseAddress };
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
    return client;
  }
}
