using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Holodeck.Web.Activity;
using Holodeck.Web.Rooms;
using Xunit;

namespace Holodeck.Tests;

public sealed class ActivityTests
{
  [DatabaseFact]
  public async Task AgentProseStreamsSeparatelyFromMessagesAndSurvivesReadback()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    await using TestHost host = await TestHost.StartAsync(database);
    TestRoomSetup setup = await TestRoomSetup.CreateAsync(database);
    Channel channel = await setup.Room.CreateChannelAsync(setup.Project.Id, "Conversation", [setup.You.Id, setup.A.Id]);
    Packet packet = await setup.Packets.SendAsync(setup.You, channel.Id, "A question", new List<OutgoingFile>(), CancellationToken.None);
    string key = AgentKeys.Create();
    await setup.Room.BeginSessionAsync(setup.A.Id, AgentKeys.Hash(key));
    using HttpClient agent = new() { BaseAddress = host.Client.BaseAddress };
    agent.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
    ActivityUpdate update = new("run-one", channel.Id, packet.Id, 0, "feeling", "Feeling", "I am", false, false, "working");
    Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.PostAsJsonAsync("/api/agent/activity", update)).StatusCode);
    Assert.Equal(HttpStatusCode.NoContent, (await agent.PostAsJsonAsync("/api/agent/activity", update)).StatusCode);
    update = update with { Text = "I am curious, but I have not reached a conclusion.", IsComplete = true };
    Assert.Equal(HttpStatusCode.NoContent, (await agent.PostAsJsonAsync("/api/agent/activity", update)).StatusCode);
    update = update with { Ordinal = 1, Phase = "reply", Label = "Writing", Text = "An answer", IsTyping = true, IsComplete = false };
    Assert.Equal(HttpStatusCode.NoContent, (await agent.PostAsJsonAsync("/api/agent/activity", update)).StatusCode);
    string route = $"/api/projects/{setup.Project.Id}/room/channels/{channel.Id}/activity";
    List<ActivityRun> live = (await host.Client.GetFromJsonAsync<List<ActivityRun>>(route))!;
    Assert.True(Assert.Single(live).IsTyping);
    Assert.Equal(2, live[0].Steps.Count);
    Assert.Contains("curious", live[0].Steps[0].Text);
    Assert.Equal(1, await database.CountAsync("SELECT count(*) FROM room_packets"));
    update = update with { State = "completed", IsTyping = false, IsComplete = true };
    Assert.Equal(HttpStatusCode.NoContent, (await agent.PostAsJsonAsync("/api/agent/activity", update)).StatusCode);
    List<ActivityRun> completed = await new ActivityStore(database.Database).ReadAsync(setup.Project.Id, channel.Id);
    Assert.False(completed[0].Active);
    Assert.Equal("completed", completed[0].State);
    Assert.Equal(2, completed[0].Steps.Count);
    Assert.Equal(HttpStatusCode.Conflict, (await agent.PostAsJsonAsync("/api/agent/activity", update with { State = "working" })).StatusCode);
    Assert.DoesNotContain(AgentKeys.Hash(key), await host.Client.GetStringAsync(route));
  }

  [DatabaseFact]
  public async Task ActivityCannotSpoofAnotherAgentOrChannelAndStopsWithItsSession()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    await using TestHost host = await TestHost.StartAsync(database);
    TestRoomSetup setup = await TestRoomSetup.CreateAsync(database);
    Channel channel = await setup.Room.CreateChannelAsync(setup.Project.Id, "Shared", [setup.You.Id, setup.A.Id, setup.B.Id]);
    Channel privateChannel = await setup.Room.CreateChannelAsync(setup.Project.Id, "Other", [setup.You.Id, setup.B.Id]);
    Packet packet = await setup.Packets.SendAsync(setup.You, channel.Id, "Question", new List<OutgoingFile>(), CancellationToken.None);
    string first = AgentKeys.Hash(AgentKeys.Create());
    string second = AgentKeys.Hash(AgentKeys.Create());
    await setup.Room.BeginSessionAsync(setup.A.Id, first);
    await setup.Room.BeginSessionAsync(setup.B.Id, second);
    ActivityStore store = host.Get<ActivityStore>();
    AgentCaller a = new(setup.A, first);
    AgentCaller b = new(setup.B, second);
    ActivityUpdate update = new("unique-run", channel.Id, packet.Id, 0, "reason", "Reasoning", "I am considering this.", false, false, "working");
    await store.WriteAsync(a, update);
    await Assert.ThrowsAsync<InvalidOperationException>(() => store.WriteAsync(b, update));
    await Assert.ThrowsAsync<UnauthorizedAccessException>(() => store.WriteAsync(a, update with { Id = "private", ChannelId = privateChannel.Id }));
    await setup.Room.EndSessionAsync(setup.A.Id);
    ActivityRun stopped = Assert.Single(await store.ReadAsync(setup.Project.Id, channel.Id));
    Assert.False(stopped.Active);
    Assert.Equal("interrupted", stopped.State);
    await Assert.ThrowsAsync<UnauthorizedAccessException>(() => store.WriteAsync(a, update));
  }

  [DatabaseFact]
  public async Task MissingHeartbeatsExpireAndInvalidOrUnrelatedSourcesAreRefused()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    await using TestHost host = await TestHost.StartAsync(database);
    TestRoomSetup setup = await TestRoomSetup.CreateAsync(database);
    Channel channel = await setup.Room.CreateChannelAsync(setup.Project.Id, "One", [setup.You.Id, setup.A.Id]);
    Channel other = await setup.Room.CreateChannelAsync(setup.Project.Id, "Two", [setup.You.Id, setup.A.Id]);
    Packet packet = await setup.Packets.SendAsync(setup.You, other.Id, "Different source", new List<OutgoingFile>(), CancellationToken.None);
    string hash = AgentKeys.Hash(AgentKeys.Create());
    await setup.Room.BeginSessionAsync(setup.A.Id, hash);
    ActivityStore store = host.Get<ActivityStore>();
    AgentCaller caller = new(setup.A, hash);
    ActivityUpdate update = new("heartbeat", channel.Id, null, 0, "work", "Thinking", "Some prose", false, false, "working");
    await store.WriteAsync(caller, update);
    await database.ExecuteAsync("UPDATE agent_activity SET updated_at=now()-interval '2 minutes'");
    Assert.False(Assert.Single(await store.ReadAsync(setup.Project.Id, channel.Id)).Active);
    await Assert.ThrowsAsync<ArgumentException>(() => store.WriteAsync(caller, update with { Id = "wrong-source", PacketId = packet.Id }));
    await Assert.ThrowsAsync<ArgumentException>(() => store.WriteAsync(caller, update with { Text = new string('x', ActivityLimits.MaxText + 1) }));
  }
}
