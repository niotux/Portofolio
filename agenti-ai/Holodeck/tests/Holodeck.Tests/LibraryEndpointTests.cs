using System.Net;
using System.Net.Http.Json;
using Holodeck.Web.Projects;
using Holodeck.Web.Rooms;
using Xunit;

namespace Holodeck.Tests;

public sealed class LibraryEndpointTests
{
  [DatabaseFact]
  public async Task CatalogAndPlacementUseLocalVersionedReferences()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    string library = Path.Combine(database.DataPath, "libs", "sample");
    string agent = Path.Combine(library, "agents", "echo");
    string team = Path.Combine(library, "teams", "pair");
    Directory.CreateDirectory(agent);
    Directory.CreateDirectory(team);
    File.WriteAllText(Path.Combine(library, "Library.json"), """
      {"FormatVersion":1,"Id":"sample","Version":"1.0.0","Name":"Sample","Dependencies":[],"Runtime":{"Image":"holodeck-library:1","Processes":[]}}
      """);
    File.WriteAllText(Path.Combine(agent, "Agent.json"), """
      {"Id":"echo","Name":"Echo","Launch":{"Command":"sh","Args":["${Agent}/run.sh"]}}
      """);
    File.WriteAllText(Path.Combine(agent, "run.sh"), "exec sleep 60\n");
    File.WriteAllText(Path.Combine(team, "Team.json"), """
      {"Id":"pair","Name":"Pair","Members":[{"Id":"one","Name":"First","Agent":"sample/agents/echo"},{"Id":"two","Name":"Second","Agent":"sample/agents/echo"}],"Wires":[["You","one"],["one","two"]],"Channels":[{"Name":"Everyone","Members":["You","one","two"]}]}
      """);
    await using TestHost host = await TestHost.StartAsync(database);
    LibraryCatalogView catalog = (await host.Client.GetFromJsonAsync<LibraryCatalogView>("/api/library/catalog"))!;
    Assert.Empty(catalog.Diagnostics);
    Assert.Equal(new LibrarySummary("sample", "1.0.0", "Sample", ""), Assert.Single(catalog.Libraries));
    Assert.Equal("sample/1.0.0/agents/echo", Assert.Single(catalog.Agents).Folder);
    Assert.Equal("sample/1.0.0/teams/pair", Assert.Single(catalog.Teams).Folder);
    Project project = (await (await host.Client.PostAsJsonAsync("/api/projects", new ProjectInput("Library room", null, null))).Content.ReadFromJsonAsync<Project>())!;
    string root = $"/api/projects/{project.Id}/room";
    HttpResponseMessage attached = await host.Client.PostAsJsonAsync($"/api/projects/{project.Id}/libraries", new LibraryReference("sample", "1.0.0"));
    Assert.Equal(HttpStatusCode.OK, attached.StatusCode);
    HttpResponseMessage placed = await host.Client.PostAsJsonAsync(root + "/teams", new PlaceTeamInput(catalog.Teams[0].Folder));
    Assert.Equal(HttpStatusCode.Created, placed.StatusCode);
    List<RoomMemberView> members = (await placed.Content.ReadFromJsonAsync<List<RoomMemberView>>())!;
    Assert.Equal(2, members.Count);
    foreach (RoomMemberView member in members)
    {
      Assert.Equal("stopped", member.Status);
      Assert.Equal(catalog.Agents[0].Folder, member.Library);
    }

    RoomView view = (await host.Client.GetFromJsonAsync<RoomView>(root))!;
    Assert.Equal(3, view.Members.Count);
    Assert.Equal(3, view.Channels.Count);
    bool hasGroup = false;
    foreach (ChannelSummary channel in view.Channels)
    {
      hasGroup |= channel.Name == "Everyone" && channel.MemberIds.Count == 3;
    }

    Assert.True(hasGroup);
    HttpResponseMessage started = await host.Client.PostAsync(root + "/agents/" + members[0].Id + "/start", null);
    Assert.Equal(HttpStatusCode.OK, started.StatusCode);
    Assert.Equal("running", (await started.Content.ReadFromJsonAsync<RoomMemberView>())!.Status);
    Assert.Equal(HttpStatusCode.NotFound, (await host.Client.PostAsJsonAsync(root + "/teams", new PlaceTeamInput("sample/9.0.0/teams/pair"))).StatusCode);
    Assert.Equal(3, (await host.Client.GetFromJsonAsync<RoomView>(root))!.Members.Count);
  }
}
