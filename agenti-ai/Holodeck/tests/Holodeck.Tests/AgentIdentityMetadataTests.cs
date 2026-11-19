using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Holodeck.Web.Rooms;
using Xunit;

namespace Holodeck.Tests;

public sealed class AgentIdentityMetadataTests
{
  private static readonly CancellationToken Token = CancellationToken.None;

  [DatabaseFact]
  public async Task IdentityAndParticipantsKeepTheirIdsAcrossRenames()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    await using TestHost host = await TestHost.StartAsync(database);
    TestRoomSetup setup = await TestRoomSetup.CreateAsync(database);
    Channel channel = await setup.Room.CreateChannelAsync(setup.Project.Id, "Desk", [setup.You.Id, setup.A.Id, setup.B.Id]);
    using HttpClient client = await ClientForAsync(host, setup, setup.A, "identity-session");

    AgentIdentity initial = (await client.GetFromJsonAsync<AgentIdentity>("/api/agent/me"))!;
    Assert.Equal(setup.A.Id, initial.MemberId);
    Assert.Equal("A", initial.Name);
    Assert.Equal(["B", "You"], initial.Peers);
    AgentChannel initialChannel = Assert.Single(initial.Channels);
    Assert.Equal(channel.Id, initialChannel.ChannelId);
    Assert.Equal(["You", "B"], initialChannel.Members);
    Assert.Contains(new AgentParticipant(setup.You.Id, "You", true), initialChannel.Participants);
    Assert.Contains(new AgentParticipant(setup.A.Id, "A", false), initialChannel.Participants);
    Assert.Contains(new AgentParticipant(setup.B.Id, "B", false), initialChannel.Participants);
    Assert.Equal(3, initialChannel.Participants.Count);

    await setup.Room.RenameAsync(setup.Project.Id, setup.A.Id, "Collaborator");
    await setup.Room.RenameAsync(setup.Project.Id, setup.B.Id, "Investigator");
    AgentIdentity renamed = (await client.GetFromJsonAsync<AgentIdentity>("/api/agent/me"))!;
    Assert.Equal(initial.MemberId, renamed.MemberId);
    Assert.Equal("Collaborator", renamed.Name);
    Assert.Equal(["Investigator", "You"], renamed.Peers);
    AgentChannel renamedChannel = Assert.Single(renamed.Channels);
    Assert.Equal(initialChannel.ChannelId, renamedChannel.ChannelId);
    Assert.Equal(["You", "Investigator"], renamedChannel.Members);
    Assert.Contains(new AgentParticipant(setup.A.Id, "Collaborator", false), renamedChannel.Participants);
    Assert.Contains(new AgentParticipant(setup.B.Id, "Investigator", false), renamedChannel.Participants);
  }

  [DatabaseFact]
  public async Task IdenticalMessagesHaveDistinctTransportIdsAndOriginalTimestamps()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    await using TestHost host = await TestHost.StartAsync(database);
    TestRoomSetup setup = await TestRoomSetup.CreateAsync(database);
    Channel channel = await setup.Room.CreateChannelAsync(setup.Project.Id, "Desk", [setup.A.Id, setup.B.Id]);
    using HttpClient sender = await ClientForAsync(host, setup, setup.B, "sender-session");
    using HttpClient receiver = await ClientForAsync(host, setup, setup.A, "receiver-session");
    SendInput message = new(channel.Id, null, "same words", null);
    HttpResponseMessage firstResponse = await sender.PostAsJsonAsync("/api/agent/send", message);
    firstResponse.EnsureSuccessStatusCode();
    Packet firstPacket = (await firstResponse.Content.ReadFromJsonAsync<Packet>())!;
    HttpResponseMessage secondResponse = await sender.PostAsJsonAsync("/api/agent/send", message);
    secondResponse.EnsureSuccessStatusCode();
    Packet secondPacket = (await secondResponse.Content.ReadFromJsonAsync<Packet>())!;

    Delivery first = (await receiver.GetFromJsonAsync<Delivery>("/api/agent/next?wait=0"))!;
    Delivery second = (await receiver.GetFromJsonAsync<Delivery>("/api/agent/next?wait=0"))!;
    Assert.NotEqual(first.PacketId, second.PacketId);
    Assert.Equal(firstPacket.Id, first.PacketId);
    Assert.Equal(secondPacket.Id, second.PacketId);
    Assert.Equal(firstPacket.SentAt, first.SentAt);
    Assert.Equal(secondPacket.SentAt, second.SentAt);
    Assert.Equal(setup.B.Id, first.SenderId);
    Assert.Equal(setup.B.Id, second.SenderId);
    Assert.Equal(first.Note, second.Note);
    Assert.Equal("B", first.From);
    Assert.Equal(channel.Id, first.ChannelId);
    Assert.Equal(HttpStatusCode.NoContent, (await receiver.GetAsync("/api/agent/next?wait=0")).StatusCode);
  }

  [DatabaseFact]
  public async Task RestartRedeliversTheSamePacketAndFileAfterSenderRename()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    await using TestHost host = await TestHost.StartAsync(database);
    TestRoomSetup setup = await TestRoomSetup.CreateAsync(database);
    Channel channel = await setup.Room.CreateChannelAsync(setup.Project.Id, "Desk", [setup.A.Id, setup.B.Id]);
    string source = Path.Combine(setup.TableOf(setup.B), "notes.txt");
    File.WriteAllText(source, "Remember this source.");
    Packet sent = await setup.Packets.SendAsync(setup.B, channel.Id, "a source", [TablePaths.Resolve(setup.TableOf(setup.B), "notes.txt")], Token);
    using HttpClient before = await ClientForAsync(host, setup, setup.A, "before-restart");
    Delivery original = (await before.GetFromJsonAsync<Delivery>("/api/agent/next?wait=0"))!;
    await setup.Room.RenameAsync(setup.Project.Id, setup.B.Id, "Investigator");

    using HttpClient after = await ClientForAsync(host, setup, setup.A, "after-restart");
    Assert.Equal(HttpStatusCode.Unauthorized, (await before.GetAsync("/api/agent/next?wait=0")).StatusCode);
    Delivery repeated = (await after.GetFromJsonAsync<Delivery>("/api/agent/next?wait=0"))!;
    Assert.Equal(sent.Id, repeated.PacketId);
    Assert.Equal(original.PacketId, repeated.PacketId);
    Assert.Equal(original.SenderId, repeated.SenderId);
    Assert.Equal(original.SentAt, repeated.SentAt);
    Assert.Equal("B", original.From);
    Assert.Equal("Investigator", repeated.From);
    Assert.Equal(original.Files, repeated.Files);
    Assert.Equal("Remember this source.", File.ReadAllText(Path.Combine(setup.TableOf(setup.A), Assert.Single(repeated.Files))));
    Assert.Equal(HttpStatusCode.NoContent, (await after.GetAsync("/api/agent/next?wait=0")).StatusCode);
  }

  [DatabaseFact]
  public async Task StoppedAgentReusingItsContainerKeyReceivesItsUnfinishedPacketAgain()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    await using TestHost host = await TestHost.StartAsync(database);
    TestRoomSetup setup = await TestRoomSetup.CreateAsync(database);
    Channel channel = await setup.Room.CreateChannelAsync(setup.Project.Id, "Desk", [setup.A.Id, setup.B.Id]);
    Packet sent = await setup.Packets.SendAsync(setup.B, channel.Id, "unfinished work", [], Token);
    using HttpClient before = await ClientForAsync(host, setup, setup.A, "container-key");
    Delivery original = (await before.GetFromJsonAsync<Delivery>("/api/agent/next?wait=0"))!;
    await setup.Room.EndSessionAsync(setup.A.Id);
    Assert.Equal(HttpStatusCode.Unauthorized, (await before.GetAsync("/api/agent/next?wait=0")).StatusCode);
    using HttpClient after = await ClientForAsync(host, setup, setup.A, "container-key");
    Delivery repeated = (await after.GetFromJsonAsync<Delivery>("/api/agent/next?wait=0"))!;
    Assert.Equal(sent.Id, repeated.PacketId);
    Assert.Equal(original.PacketId, repeated.PacketId);
    Assert.Equal("unfinished work", repeated.Note);
    Assert.Equal(HttpStatusCode.NoContent, (await after.GetAsync("/api/agent/next?wait=0")).StatusCode);
  }

  [DatabaseFact]
  public async Task SameProcessKeepsItsDeliveryPositionAndNewProcessReplaysUnfinishedWork()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    await using TestHost host = await TestHost.StartAsync(database);
    TestRoomSetup setup = await TestRoomSetup.CreateAsync(database);
    Channel channel = await setup.Room.CreateChannelAsync(setup.Project.Id, "Desk", [setup.A.Id, setup.B.Id]);
    Packet first = await setup.Packets.SendAsync(setup.B, channel.Id, "first work", [], Token);
    Packet second = await setup.Packets.SendAsync(setup.B, channel.Id, "second work", [], Token);
    using HttpClient client = await ClientForAsync(host, setup, setup.A, "retained-container-key");
    Delivery original = (await client.GetFromJsonAsync<Delivery>("/api/agent/next?wait=0"))!;
    Assert.Equal(first.Id, original.PacketId);

    const string FirstStart = "2026-11-08T10:00:00Z";
    await setup.Room.ObserveComputerStartAsync(setup.A.Id, FirstStart);
    await setup.Room.ObserveComputerStartAsync(setup.A.Id, "");
    await setup.Room.ObserveComputerStartAsync(setup.A.Id, FirstStart);
    Delivery next = (await client.GetFromJsonAsync<Delivery>("/api/agent/next?wait=0"))!;
    Assert.Equal(second.Id, next.PacketId);

    await setup.Room.ObserveComputerStartAsync(setup.A.Id, "2026-11-08T11:00:00Z");
    Delivery repeated = (await client.GetFromJsonAsync<Delivery>("/api/agent/next?wait=0"))!;
    Assert.Equal(second.Id, repeated.PacketId);
    Assert.Equal(next.PacketId, repeated.PacketId);
    Assert.Equal(HttpStatusCode.NoContent, (await client.GetAsync("/api/agent/next?wait=0")).StatusCode);
  }

  private static async Task<HttpClient> ClientForAsync(TestHost host, TestRoomSetup setup, RoomMember member, string key)
  {
    await setup.Room.BeginSessionAsync(member.Id, AgentKeys.Hash(key));
    HttpClient client = new() { BaseAddress = host.Client.BaseAddress };
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
    return client;
  }
}
