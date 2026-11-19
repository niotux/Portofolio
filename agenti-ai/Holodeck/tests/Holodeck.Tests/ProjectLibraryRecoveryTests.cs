using Holodeck.Web.Computers;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Holodeck.Web.Projects;
using Holodeck.Web.Rooms;
using Xunit;

namespace Holodeck.Tests;

public sealed class ProjectLibraryRecoveryTests
{
  [DatabaseFact]
  public async Task RefusedReplacementAndLibraryRepairPreserveInstanceDataAndHistory()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    WriteLibrary(database.DataPath, "original");
    WriteLibrary(database.DataPath, "replacement");
    await using TestHost host = await TestHost.StartAsync(database);
    Project project = (await (await host.Client.PostAsJsonAsync("/api/projects", new ProjectInput("Recovery", null, null))).Content.ReadFromJsonAsync<Project>())!;
    string libraries = $"/api/projects/{project.Id}/libraries";
    string root = $"/api/projects/{project.Id}/room";
    PlaceInput original = new("original/1.0.0/agents/probe");
    Assert.Equal(HttpStatusCode.Conflict, (await host.Client.PostAsJsonAsync(root + "/agents", original)).StatusCode);
    (await host.Client.PostAsJsonAsync(libraries, new ProjectLibraryInput("original", "1.0.0"))).EnsureSuccessStatusCode();
    RoomMemberView first = (await (await host.Client.PostAsJsonAsync(root + "/agents", original)).Content.ReadFromJsonAsync<RoomMemberView>())!;
    RoomMemberView second = (await (await host.Client.PostAsJsonAsync(root + "/agents", original)).Content.ReadFromJsonAsync<RoomMemberView>())!;
    RoomStore room = host.Get<RoomStore>();
    RoomMember you = await room.EnsureYouAsync(project.Id);
    Channel wire = await room.EnsurePairChannelAsync(project.Id, you.Id, first.Id);
    Packet packet = await host.Get<PacketStore>().SendAsync(you, wire.Id, "Retain this history", [], CancellationToken.None);
    RoomPaths paths = host.Get<RoomPaths>();
    File.WriteAllText(paths.SettingsFile(project.Id, first.Id), "{\"keep\":true}");
    string file = Path.Combine(paths.Table(project.Id, first.Id), "draft.txt");
    File.WriteAllText(file, "Retain these files");
    (await host.Client.PostAsync(root + "/agents/" + first.Id + "/start", null)).EnsureSuccessStatusCode();
    (await host.Client.DeleteAsync(libraries + "/original/1.0.0")).EnsureSuccessStatusCode();
    RoomView ghosts = (await host.Client.GetFromJsonAsync<RoomView>(root))!;
    foreach (RoomMemberView member in ghosts.Members)
    {
      if (!member.IsYou)
      {
        Assert.Equal("error", member.Status);
        Assert.NotNull(member.Error);
      }
    }

    string definition = root + "/agents/" + first.Id + "/definition";
    ReplaceDefinitionInput replacement = new("replacement/1.0.0/agents/probe");
    Assert.Equal(HttpStatusCode.Conflict, (await host.Client.PostAsJsonAsync(definition, replacement)).StatusCode);
    (await host.Client.PostAsJsonAsync(libraries, new ProjectLibraryInput("replacement", "1.0.0"))).EnsureSuccessStatusCode();
    Assert.Equal(HttpStatusCode.Conflict, (await host.Client.PostAsJsonAsync(definition, replacement)).StatusCode);
    Assert.Equal(original.Library, (await room.GetMemberAsync(project.Id, first.Id)).Library);
    (await host.Client.PostAsJsonAsync(libraries, new ProjectLibraryInput("original", "1.0.0"))).EnsureSuccessStatusCode();
    RoomView recovered = (await host.Client.GetFromJsonAsync<RoomView>(root))!;
    RoomMemberView? repaired = null;
    foreach (RoomMemberView member in recovered.Members)
    {
      if (member.Id == first.Id)
      {
        repaired = member;
      }
    }

    Assert.NotNull(repaired);
    Assert.Equal(first.Id, repaired.Id);
    Assert.Equal(first.Name, repaired.Name);
    Assert.Equal(original.Library, repaired.Library);
    Assert.Equal("stopped", repaired.Status);
    Assert.Null(repaired.Error);
    Assert.Equal("{\"keep\":true}", File.ReadAllText(paths.SettingsFile(project.Id, first.Id)));
    Assert.Equal("Retain these files", File.ReadAllText(file));
    Assert.Equal(wire.Id, Assert.Single(await room.ListChannelsAsync(project.Id)).Id);
    Assert.Equal(packet.Id, Assert.Single(await host.Get<PacketStore>().HistoryAsync(project.Id, wire.Id, 0, CancellationToken.None)).Id);

    RoomMemberView orphan = (await (await host.Client.PostAsJsonAsync(root + "/agents", new PlaceInput(replacement.Library!))).Content.ReadFromJsonAsync<RoomMemberView>())!;
    (await host.Client.DeleteAsync(libraries + "/replacement/1.0.0")).EnsureSuccessStatusCode();
    Assert.Equal(HttpStatusCode.NoContent, (await host.Client.DeleteAsync(root + "/unresolved")).StatusCode);
    Assert.False((await room.GetMemberAsync(project.Id, first.Id)).IsRemoved);
    Assert.False((await room.GetMemberAsync(project.Id, second.Id)).IsRemoved);
    Assert.True((await room.GetMemberAsync(project.Id, orphan.Id)).IsRemoved);
    Assert.Equal("Retain these files", File.ReadAllText(file));
    (await host.Client.PostAsync(root + "/agents/" + first.Id + "/start", null)).EnsureSuccessStatusCode();
  }

  private static void WriteLibrary(string dataPath, string id)
  {
    string root = Path.Combine(dataPath, "libs", id);
    string agent = Path.Combine(root, "agents", "probe");
    Directory.CreateDirectory(agent);
    Directory.CreateDirectory(Path.Combine(root, "teams"));
    File.WriteAllText(Path.Combine(root, "Library.json"), JsonSerializer.Serialize(new LibraryManifest(1, id, "1.0.0", id, "", null, Runtime: new LibraryEnvironmentDefinition(TestLibrary.RuntimeImage, new()))));
    File.WriteAllText(Path.Combine(agent, "Agent.json"), JsonSerializer.Serialize(new PackageAgentManifest("probe", "Probe", "", new AgentLaunch("sh", ["${Agent}/run.sh"]), null)));
    File.WriteAllText(Path.Combine(agent, "run.sh"), "exec sleep 60\n");
  }
}
