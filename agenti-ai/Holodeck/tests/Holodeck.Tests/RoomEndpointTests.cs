using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using Holodeck.Web.Projects;
using Holodeck.Web.Rooms;
using Xunit;

namespace Holodeck.Tests;

public sealed class RoomEndpointTests
{
  private const string DumpScript = "env | grep HOLODECK_ > env.txt\nexec sleep 60\n";
  private const int NoticeBufferBytes = 256;

  [DatabaseFact]
  public async Task AgentContractRequiresAKeyAndUsesNamesOnly()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    await using TestHost host = await TestHost.StartAsync(database);
    RoomFixture room = await RoomFixture.CreateAsync(host, database);
    HttpClient anonymous = host.Client;
    Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/agent/me")).StatusCode);
    Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/agent/next")).StatusCode);
    Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/agent/send", new SendInput(null, "B", "hi", null))).StatusCode);
    using HttpRequestMessage forged = new(HttpMethod.Get, "/api/agent/me");
    forged.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-key");
    Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.SendAsync(forged)).StatusCode);

    RoomMemberView a = await room.PlaceAndStartAsync("A");
    RoomMemberView b = await room.PlaceAndStartAsync("B");
    Channel pair = await room.ChannelAsync("Pair", [a.Id, b.Id]);
    await room.ChannelAsync("Mine", [room.You.Id, a.Id]);
    HttpClient asA = await room.ClientForAsync(a);
    HttpClient asB = await room.ClientForAsync(b);
    AgentIdentity me = (await asA.GetFromJsonAsync<AgentIdentity>("/api/agent/me"))!;
    Assert.Equal("A", me.Name);
    Assert.Equal(room.ProjectId, me.ProjectId);
    Assert.Equal(["B", "You"], me.Peers);
    Assert.Equal(2, me.Channels.Count);
    Assert.Equal(HttpStatusCode.Created, (await asA.PostAsJsonAsync("/api/agent/send", new SendInput(null, "B", "hi", null))).StatusCode);
    HttpResponseMessage next = await asB.GetAsync("/api/agent/next?wait=0");
    Assert.Equal(HttpStatusCode.OK, next.StatusCode);
    Delivery delivery = (await next.Content.ReadFromJsonAsync<Delivery>())!;
    Assert.Equal("A", delivery.From);
    Assert.Equal("hi", delivery.Note);
    Assert.Equal(pair.Id, delivery.ChannelId);
    Assert.Equal("Pair", delivery.Channel);
    Assert.Equal(HttpStatusCode.OK, (await host.Client.PutAsJsonAsync(room.Root + "/channels/" + pair.Id, new ChannelInput(null, true))).StatusCode);
    Assert.Equal(HttpStatusCode.Conflict, (await asA.PostAsJsonAsync("/api/agent/send", new SendInput(pair.Id, null, "while paused", null))).StatusCode);
    Assert.Equal(HttpStatusCode.OK, (await host.Client.PutAsJsonAsync(room.Root + "/channels/" + pair.Id, new ChannelInput("Renamed", false))).StatusCode);
    Assert.Equal(HttpStatusCode.Created, (await asA.PostAsJsonAsync("/api/agent/send", new SendInput(pair.Id, null, "by id", null))).StatusCode);
    Assert.Equal("by id", (await (await asB.GetAsync("/api/agent/next?wait=0")).Content.ReadFromJsonAsync<Delivery>())!.Note);
    Assert.Equal(HttpStatusCode.NoContent, (await asB.GetAsync("/api/agent/next?wait=0")).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest, (await asB.GetAsync("/api/agent/next?wait=61")).StatusCode);
    Assert.Equal(HttpStatusCode.NotFound, (await asA.PostAsJsonAsync("/api/agent/send", new SendInput(null, "Nobody", "hi", null))).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest, (await asA.PostAsJsonAsync("/api/agent/send", new SendInput(null, "B", "hi", ["../x"]))).StatusCode);
    await room.StopAsync(a);
    Assert.Equal(HttpStatusCode.Unauthorized, (await asA.GetAsync("/api/agent/me")).StatusCode);
  }

  [DatabaseFact]
  public async Task NextWaitsForAPacket()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    await using TestHost host = await TestHost.StartAsync(database);
    RoomFixture room = await RoomFixture.CreateAsync(host, database);
    RoomMemberView a = await room.PlaceAndStartAsync("A");
    RoomMemberView b = await room.PlaceAndStartAsync("B");
    await room.ChannelAsync("Pair", [a.Id, b.Id]);
    HttpClient asA = await room.ClientForAsync(a);
    HttpClient asB = await room.ClientForAsync(b);
    DateTime started = DateTime.UtcNow;
    Task<HttpResponseMessage> waiting = asB.GetAsync("/api/agent/next?wait=10");
    await Task.Delay(300);
    await asA.PostAsJsonAsync("/api/agent/send", new SendInput(null, "B", "wake up", null));
    HttpResponseMessage response = await waiting;
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.Equal("wake up", (await response.Content.ReadFromJsonAsync<Delivery>())!.Note);
    Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(3));
  }

  [DatabaseFact]
  public async Task YouTalksThroughTheRoomApiWithFiles()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    await using TestHost host = await TestHost.StartAsync(database);
    RoomFixture room = await RoomFixture.CreateAsync(host, database);
    RoomMemberView a = await room.PlaceAndStartAsync("A");
    RoomMemberView b = await room.PlaceAndStartAsync("B");
    Channel withYou = await room.ChannelAsync("Mine", [room.You.Id, a.Id]);
    Channel between = await room.ChannelAsync("Between", [a.Id, b.Id]);
    Channel everyone = await room.ChannelAsync("Everyone", [room.You.Id, a.Id, b.Id]);
    byte[] image = [137, 80, 78, 71, 1, 2, 3];
    using MultipartFormDataContent form = new();
    form.Add(new StringContent("look"), "note");
    form.Add(new ByteArrayContent(image), "files", "ref.png");
    HttpResponseMessage sent = await host.Client.PostAsync(room.Root + "/channels/" + withYou.Id + "/packets", form);
    Assert.Equal(HttpStatusCode.Created, sent.StatusCode);
    Packet packet = (await sent.Content.ReadFromJsonAsync<Packet>())!;
    List<Packet> history = (await host.Client.GetFromJsonAsync<List<Packet>>(room.Root + "/channels/" + withYou.Id + "/packets?after=0"))!;
    Assert.Equal(new PacketFile("ref.png", "image/png", image.Length), Assert.Single(Assert.Single(history).Files));
    HttpClient asA = await room.ClientForAsync(a);
    Delivery delivery = (await asA.GetFromJsonAsync<Delivery>("/api/agent/next?wait=0"))!;
    Assert.Equal([$"received/{withYou.Id}/You/ref.png"], delivery.Files);
    Assert.Equal(image, File.ReadAllBytes(Path.Combine(room.TableOf(a), "received", withYou.Id, "You", "ref.png")));
    Assert.Equal(image, await host.Client.GetByteArrayAsync(room.Root + "/packets/" + packet.Id + "/files/ref.png"));
    using MultipartFormDataContent intruding = new();
    intruding.Add(new StringContent("not mine"), "note");
    Assert.Equal(HttpStatusCode.Conflict, (await host.Client.PostAsync(room.Root + "/channels/" + between.Id + "/packets", intruding)).StatusCode);
    using MultipartFormDataContent toAll = new();
    toAll.Add(new StringContent("all of you"), "note");
    Assert.Equal(HttpStatusCode.Created, (await host.Client.PostAsync(room.Root + "/channels/" + everyone.Id + "/packets", toAll)).StatusCode);
    HttpClient asB = await room.ClientForAsync(b);
    Assert.Equal("all of you", (await asA.GetFromJsonAsync<Delivery>("/api/agent/next?wait=0"))!.Note);
    Assert.Equal("all of you", (await asB.GetFromJsonAsync<Delivery>("/api/agent/next?wait=0"))!.Note);
    Assert.Equal(HttpStatusCode.OK, (await host.Client.DeleteAsync(room.Root + "/channels/" + everyone.Id + "/members/" + b.Id)).StatusCode);
    Assert.Equal(2, (await room.ViewAsync()).Channels.Find(item => item.Id == everyone.Id)!.MemberIds.Count);
  }

  [DatabaseFact]
  public async Task RoomViewShowsMembersStatusAndConversations()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    await using TestHost host = await TestHost.StartAsync(database);
    RoomFixture room = await RoomFixture.CreateAsync(host, database);
    RoomView initial = await room.ViewAsync();
    RoomMemberView you = Assert.Single(initial.Members);
    Assert.Equal("you", you.Status);
    RoomMemberView a = await room.PlaceAndStartAsync("A");
    Assert.Equal("running", a.Status);
    await room.ChannelAsync("Mine", [you.Id, a.Id]);
    HttpClient asA = await room.ClientForAsync(a);
    await asA.PostAsJsonAsync("/api/agent/send", new SendInput(null, "You", "hello you", null));
    RoomView view = await room.ViewAsync();
    ChannelSummary channel = Assert.Single(view.Channels);
    Assert.Equal(1, channel.PacketCount);
    Assert.Equal("hello you", channel.LastNote);
    await room.StopAsync(a);
    Assert.Equal("stopped", (await room.ViewAsync()).Members[1].Status);
    Assert.Equal(HttpStatusCode.NoContent, (await host.Client.DeleteAsync(room.Root + "/agents/" + a.Id)).StatusCode);
    Assert.Equal("removed", (await room.ViewAsync()).Members[1].Status);
    LibraryCatalogView catalog = (await host.Client.GetFromJsonAsync<LibraryCatalogView>("/api/library/catalog"))!;
    Assert.Equal(TestLibrary.Reference("probe"), Assert.Single(catalog.Agents).Folder);
    Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync("/api/library")).StatusCode);
  }

  [DatabaseFact]
  public async Task OldStyleAgentReferencesAreUnresolved()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    await using TestHost host = await TestHost.StartAsync(database);
    RoomFixture room = await RoomFixture.CreateAsync(host, database);
    RoomMemberView placed = (await (await host.Client.PostAsJsonAsync(room.Root + "/agents", new PlaceInput(TestLibrary.Reference("probe")))).Content.ReadFromJsonAsync<RoomMemberView>())!;
    await using (Npgsql.NpgsqlCommand command = database.Database.CreateCommand("UPDATE room_members SET library = 'echo' WHERE id = $1"))
    {
      command.Parameters.AddWithValue(placed.Id);
      await command.ExecuteNonQueryAsync();
    }

    RoomMemberView old = (await room.ViewAsync()).Members.Find(member => member.Id == placed.Id)!;
    Assert.Equal("error", old.Status);
    Assert.NotNull(old.Error);
    Assert.Equal(HttpStatusCode.NotFound, (await host.Client.PostAsync(room.Root + "/agents/" + placed.Id + "/start", null)).StatusCode);
  }

  [DatabaseFact]
  public async Task SettingsLogAndTableAreReadable()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    await using TestHost host = await TestHost.StartAsync(database);
    RoomFixture room = await RoomFixture.CreateAsync(host, database);
    RoomMemberView a = await room.PlaceAndStartAsync("A");
    string agent = room.Root + "/agents/" + a.Id;
    Assert.Equal(HttpStatusCode.NoContent, (await host.Client.PutAsync(agent + "/settings", new StringContent("{\"mood\":\"calm\"}"))).StatusCode);
    Assert.Equal("{\"mood\":\"calm\"}", await host.Client.GetStringAsync(agent + "/settings"));
    Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.PutAsync(agent + "/settings", new StringContent(new string('a', RoomLimits.MaxSettingsBytes + 1)))).StatusCode);
    File.WriteAllText(Path.Combine(room.TableOf(a), "draft.md"), "draft");
    List<TableEntry> table = (await host.Client.GetFromJsonAsync<List<TableEntry>>(agent + "/table"))!;
    Assert.Contains(new TableEntry("draft.md", 5), table);
    Assert.Equal("draft", await host.Client.GetStringAsync(agent + "/table/file?path=draft.md"));
    Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.GetAsync(agent + "/table/file?path=../settings.json")).StatusCode);
    await File.AppendAllTextAsync(host.Get<RoomPaths>().LogFile(room.ProjectId, a.Id), "hello log\n");
    Assert.Contains("hello log", await host.Client.GetStringAsync(agent + "/log"));
    RoomMemberView renamed = (await (await host.Client.PutAsJsonAsync(agent, new RenameInput("Writer"))).Content.ReadFromJsonAsync<RoomMemberView>())!;
    Assert.Equal("Writer", renamed.Name);
  }

  [DatabaseFact]
  public async Task EventsSocketAnnouncesChanges()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    await using TestHost host = await TestHost.StartAsync(database);
    RoomFixture room = await RoomFixture.CreateAsync(host, database);
    Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.GetAsync("/api/projects/" + room.ProjectId + "/events")).StatusCode);
    UriBuilder address = new(new Uri(host.Client.BaseAddress!, "/api/projects/" + room.ProjectId + "/events")) { Scheme = "ws" };
    using ClientWebSocket socket = new();
    await socket.ConnectAsync(address.Uri, CancellationToken.None);
    Task<string> notice = ReceiveTextAsync(socket);
    await room.PlaceAndStartAsync("A");
    Assert.Same(notice, await Task.WhenAny(notice, Task.Delay(2000)));
    Assert.Equal("{\"kind\":\"room\"}", await notice);
    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
  }

  [DatabaseFact]
  public async Task NewProjectsStartWithYou()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    await using TestHost host = await TestHost.StartAsync(database);
    Project project = (await (await host.Client.PostAsJsonAsync("/api/projects", new ProjectInput("Fresh", null, null))).Content.ReadFromJsonAsync<Project>())!;
    RoomView view = (await host.Client.GetFromJsonAsync<RoomView>("/api/projects/" + project.Id + "/room"))!;
    Assert.True(Assert.Single(view.Members).IsYou);
    Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync("/api/projects/missing/room")).StatusCode);
  }

  private static async Task<string> ReceiveTextAsync(WebSocket socket)
  {
    byte[] buffer = new byte[NoticeBufferBytes];
    WebSocketReceiveResult received = await socket.ReceiveAsync(buffer, CancellationToken.None);
    return Encoding.UTF8.GetString(buffer, 0, received.Count);
  }

  private sealed class RoomFixture
  {
    private readonly TestHost host;
    private readonly TestDatabase database;

    private RoomFixture(TestHost host, TestDatabase database, string projectId, RoomMemberView you)
    {
      this.host = host;
      this.database = database;
      ProjectId = projectId;
      You = you;
      Root = "/api/projects/" + projectId + "/room";
    }

    public string ProjectId { get; }
    public string Root { get; }
    public RoomMemberView You { get; }

    public static async Task<RoomFixture> CreateAsync(TestHost host, TestDatabase database)
    {
      string directory = TestLibrary.Write(database.DataPath, "probe", "Probe", ["sh", "${Agent}/run.sh"]);
      File.WriteAllText(Path.Combine(directory, "run.sh"), DumpScript);
      HttpResponseMessage created = await host.Client.PostAsJsonAsync("/api/projects", new ProjectInput("Endpoint room", null, null));
      Project project = (await created.Content.ReadFromJsonAsync<Project>())!;
      (await host.Client.PostAsJsonAsync($"/api/projects/{project.Id}/libraries", new ProjectLibraryInput(TestLibrary.Id, TestLibrary.Version))).EnsureSuccessStatusCode();
      RoomView view = (await host.Client.GetFromJsonAsync<RoomView>("/api/projects/" + project.Id + "/room"))!;
      return new RoomFixture(host, database, project.Id, view.Members[0]);
    }

    public string TableOf(RoomMemberView member)
    {
      return TestLibrary.Paths(database.DataPath).Table(ProjectId, member.Id);
    }

    public async Task<RoomView> ViewAsync()
    {
      return (await host.Client.GetFromJsonAsync<RoomView>(Root))!;
    }

    public async Task<RoomMemberView> PlaceAndStartAsync(string name)
    {
      HttpResponseMessage placed = await host.Client.PostAsJsonAsync(Root + "/agents", new PlaceInput(TestLibrary.Reference("probe")));
      Assert.Equal(HttpStatusCode.Created, placed.StatusCode);
      RoomMemberView member = (await placed.Content.ReadFromJsonAsync<RoomMemberView>())!;
      HttpResponseMessage renamed = await host.Client.PutAsJsonAsync(Root + "/agents/" + member.Id, new RenameInput(name));
      renamed.EnsureSuccessStatusCode();
      HttpResponseMessage started = await host.Client.PostAsync(Root + "/agents/" + member.Id + "/start", null);
      started.EnsureSuccessStatusCode();
      return (await started.Content.ReadFromJsonAsync<RoomMemberView>())!;
    }

    public async Task StopAsync(RoomMemberView member)
    {
      HttpResponseMessage stopped = await host.Client.PostAsync(Root + "/agents/" + member.Id + "/stop", null);
      stopped.EnsureSuccessStatusCode();
    }

    public async Task<Channel> ChannelAsync(string name, List<string> memberIds)
    {
      HttpResponseMessage response = await host.Client.PostAsJsonAsync(Root + "/channels", new CreateChannelInput(name, memberIds));
      Assert.Equal(HttpStatusCode.Created, response.StatusCode);
      return (await response.Content.ReadFromJsonAsync<Channel>())!;
    }

    // Reads the key the agent program was started with, as the program itself would.
    public async Task<HttpClient> ClientForAsync(RoomMemberView member)
    {
      string env = Path.Combine(TableOf(member), "env.txt");
      Assert.True(await Eventually.TrueAsync(() => EnvironmentDump.Read(env, "HOLODECK_SETTINGS") is not null));
      HttpClient client = new() { BaseAddress = host.Client.BaseAddress };
      client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", EnvironmentDump.Read(env, "HOLODECK_KEY"));
      return client;
    }
  }
}
