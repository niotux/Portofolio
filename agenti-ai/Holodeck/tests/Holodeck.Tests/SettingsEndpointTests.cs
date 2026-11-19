using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Holodeck.Web.Projects;
using Holodeck.Web.Rooms;
using Holodeck.Web.Settings;
using Xunit;

namespace Holodeck.Tests;

public sealed class SettingsEndpointTests
{
  // Initial values from the fixture library, including a value the form does not describe.
  private const string InitialSettings = """
    {
      "personality": "plain",
      "model": { "name": "first-model" },
      "notes": { "large": 123456789012345678901234567890, "text": "café", "list": [1, 2.50, "x"] }
    }
    """;

  [DatabaseFact]
  public async Task FormChangesSaveOnlyTheirFieldsAndKeepUnknownValuesExactly()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    await using Fixture fixture = await Fixture.StartAsync(database);
    RoomMemberView agent = await fixture.PlaceAsync();

    SettingsView view = await fixture.ReadAsync(agent);
    Assert.Equal(AgentSettingsStore.FormMode, view.Mode);
    Assert.Equal(TestLibrary.Reference("formed"), view.Definition);
    Assert.NotNull(view.Schema);
    Assert.NotNull(view.SchemaRevision);
    Assert.False(view.NeedsRepair);
    Assert.Empty(view.Errors);

    SettingsView saved = await fixture.SaveAsync(agent, view, [Set("/personality", "\"curious\""), Set("/model/key", "\"secret\""), Set("/rounds", "4"), Remove("/tone")]);
    Assert.NotEqual(view.Revision, saved.Revision);
    JsonElement stored = JsonDocument.Parse(fixture.SettingsText(agent)).RootElement;
    Assert.Equal("curious", stored.GetProperty("personality").GetString());
    Assert.Equal("first-model", stored.GetProperty("model").GetProperty("name").GetString());
    Assert.Equal("secret", stored.GetProperty("model").GetProperty("key").GetString());
    Assert.Equal(4, stored.GetProperty("rounds").GetInt32());
    Assert.Equal("123456789012345678901234567890", stored.GetProperty("notes").GetProperty("large").GetRawText());
    Assert.Equal("""[1,2.50,"x"]""", Compact(stored.GetProperty("notes").GetProperty("list")));
    Assert.Contains("café", fixture.SettingsText(agent));
    Assert.Equal(saved.Content, fixture.SettingsText(agent));
  }

  [DatabaseFact]
  public async Task MissingSchemaDefaultsAreNotFilledIn()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    await using Fixture fixture = await Fixture.StartAsync(database);
    RoomMemberView agent = await fixture.PlaceAsync();
    SettingsView view = await fixture.ReadAsync(agent);

    await fixture.SaveAsync(agent, view, [Set("/personality", "\"other\"")]);
    JsonElement stored = JsonDocument.Parse(fixture.SettingsText(agent)).RootElement;
    Assert.False(stored.TryGetProperty("temperature", out _));
    Assert.False(stored.TryGetProperty("thinking", out _));
  }

  [DatabaseFact]
  public async Task InvalidValuesReturnFieldErrorsAndLeaveTheFile()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    await using Fixture fixture = await Fixture.StartAsync(database);
    RoomMemberView agent = await fixture.PlaceAsync();
    SettingsView view = await fixture.ReadAsync(agent);
    string before = fixture.SettingsText(agent);

    HttpResponseMessage response = await fixture.PutAsync(agent, Write(view, [Set("/temperature", "9"), Set("/model/name", "\"\""), Remove("/personality"), Set("/tone", "\"loud\"")]));
    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    SettingsRejection rejection = (await response.Content.ReadFromJsonAsync<SettingsRejection>())!;
    List<string> paths = new();
    foreach (SettingsFieldError error in rejection.FieldErrors)
    {
      paths.Add(error.Path);
    }

    paths.Sort(StringComparer.Ordinal);
    Assert.Equal(["/model/name", "/personality", "/temperature", "/tone"], paths);
    Assert.Equal(before, fixture.SettingsText(agent));

    // Fields outside the form and whole groups cannot be set through changes.
    Assert.Equal(HttpStatusCode.BadRequest, (await fixture.PutAsync(agent, Write(view, [Set("/notes", "1")]))).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest, (await fixture.PutAsync(agent, Write(view, [Set("/model", "{}")]))).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest, (await fixture.PutAsync(agent, Write(view, [Set("/model/extra", "1")]))).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest, (await fixture.PutAsync(agent, Write(view, [new SettingsChange("/personality", null, false)]))).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest, (await fixture.PutAsync(agent, new SettingsWrite(view.Revision, view.SchemaRevision, "{}", [Set("/personality", "\"x\"")]))).StatusCode);
    Assert.Equal(before, fixture.SettingsText(agent));
  }

  [DatabaseFact]
  public async Task AStaleWriteConflictsAndKeepsTheNewerContent()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    await using Fixture fixture = await Fixture.StartAsync(database);
    RoomMemberView agent = await fixture.PlaceAsync();
    SettingsView first = await fixture.ReadAsync(agent);
    SettingsView second = await fixture.ReadAsync(agent);

    SettingsView saved = await fixture.SaveAsync(agent, first, [Set("/personality", "\"first editor\"")]);
    HttpResponseMessage stale = await fixture.PutAsync(agent, Write(second, [Set("/personality", "\"second editor\"")]));
    Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
    SettingsConflict conflict = (await stale.Content.ReadFromJsonAsync<SettingsConflict>())!;
    Assert.Equal(saved.Revision, conflict.Current.Revision);
    Assert.Contains("first editor", fixture.SettingsText(agent));

    HttpResponseMessage wrongSchema = await fixture.PutAsync(agent, new SettingsWrite(saved.Revision, "0000", null, [Set("/personality", "\"x\"")]));
    Assert.Equal(HttpStatusCode.Conflict, wrongSchema.StatusCode);
    Assert.Contains("first editor", fixture.SettingsText(agent));
  }

  [DatabaseFact]
  public async Task ConcurrentWritesFromOneRevisionSaveExactlyOnce()
  {
    const int Editors = 6;
    await using TestDatabase database = await TestDatabase.CreateAsync();
    await using Fixture fixture = await Fixture.StartAsync(database);
    RoomMemberView agent = await fixture.PlaceAsync();
    SettingsView view = await fixture.ReadAsync(agent);

    List<Task<HttpResponseMessage>> writes = new();
    for (int index = 0; index < Editors; index++)
    {
      writes.Add(fixture.PutAsync(agent, Write(view, [Set("/personality", $"\"editor {index}\"")])));
    }

    int saved = 0;
    foreach (HttpResponseMessage response in await Task.WhenAll(writes))
    {
      if (response.StatusCode == HttpStatusCode.OK)
      {
        saved++;
      }
      else
      {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
      }
    }

    Assert.Equal(1, saved);
  }

  [DatabaseFact]
  public async Task ASchemaEditedInPlaceIsVisibleAndRejectsWritesFromTheOldForm()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    await using Fixture fixture = await Fixture.StartAsync(database);
    RoomMemberView agent = await fixture.PlaceAsync();
    SettingsView view = await fixture.ReadAsync(agent);
    string schemaFile = Path.Combine(fixture.AgentDirectory, "settings.schema.json");
    File.WriteAllText(schemaFile, SettingsSchemaTests.Schema.Replace("\"maxLength\": 400", "\"maxLength\": 300"));

    SettingsView changed = await fixture.ReadAsync(agent);
    Assert.NotEqual(view.SchemaRevision, changed.SchemaRevision);
    Assert.Equal(view.Revision, changed.Revision);
    Assert.Equal(HttpStatusCode.Conflict, (await fixture.PutAsync(agent, Write(view, [Set("/personality", "\"x\"")]))).StatusCode);
    await fixture.SaveAsync(agent, changed, [Set("/personality", "\"x\"")]);

    // A schema that leaves the profile makes the definition unavailable: no partial validator, no fabricated form.
    File.WriteAllText(schemaFile, SettingsSchemaTests.Schema.Replace("\"maxLength\": 400", "\"pattern\": \"a\""));
    SettingsView unavailable = await fixture.ReadAsync(agent);
    Assert.Equal(AgentSettingsStore.RawMode, unavailable.Mode);
    Assert.Null(unavailable.Schema);
    Assert.NotNull(unavailable.Notice);
    Assert.Contains("\"x\"", unavailable.Content);
  }

  [DatabaseFact]
  public async Task TwoInstancesKeepTheirOwnValues()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    await using Fixture fixture = await Fixture.StartAsync(database);
    RoomMemberView first = await fixture.PlaceAsync();
    RoomMemberView second = await fixture.PlaceAsync();

    await fixture.SaveAsync(first, await fixture.ReadAsync(first), [Set("/personality", "\"patient\""), Set("/model/name", "\"model-one\"")]);
    await fixture.SaveAsync(second, await fixture.ReadAsync(second), [Set("/personality", "\"blunt\""), Set("/model/name", "\"model-two\"")]);
    (await fixture.Host.Client.PostAsync($"{fixture.Root}/agents/{first.Id}/start", null)).EnsureSuccessStatusCode();
    (await fixture.Host.Client.PostAsync($"{fixture.Root}/agents/{first.Id}/stop", null)).EnsureSuccessStatusCode();

    JsonElement one = JsonDocument.Parse((await fixture.ReadAsync(first)).Content).RootElement;
    JsonElement two = JsonDocument.Parse((await fixture.ReadAsync(second)).Content).RootElement;
    Assert.Equal("patient", one.GetProperty("personality").GetString());
    Assert.Equal("model-one", one.GetProperty("model").GetProperty("name").GetString());
    Assert.Equal("blunt", two.GetProperty("personality").GetString());
    Assert.Equal("model-two", two.GetProperty("model").GetProperty("name").GetString());
  }

  [DatabaseFact]
  public async Task TheTextEndpointValidatesSchemaBackedSettings()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    await using Fixture fixture = await Fixture.StartAsync(database);
    RoomMemberView agent = await fixture.PlaceAsync();
    string before = fixture.SettingsText(agent);

    HttpResponseMessage refused = await fixture.PutTextAsync(agent, """{ "personality": 5, "model": { "name": "m" } }""");
    Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    Assert.Equal("/personality", Assert.Single((await refused.Content.ReadFromJsonAsync<SettingsRejection>())!.FieldErrors).Path);
    Assert.Equal(HttpStatusCode.BadRequest, (await fixture.PutTextAsync(agent, "not json")).StatusCode);
    Assert.Equal(before, fixture.SettingsText(agent));
    Assert.Equal(HttpStatusCode.NoContent, (await fixture.PutTextAsync(agent, """{ "personality": "ok", "model": { "name": "m" } }""")).StatusCode);
  }

  [DatabaseFact]
  public async Task BrokenStoredContentOffersRepairInsteadOfAForm()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    await using Fixture fixture = await Fixture.StartAsync(database);
    RoomMemberView agent = await fixture.PlaceAsync();
    File.WriteAllText(fixture.SettingsPath(agent), "{ \"personality\": ");

    SettingsView broken = await fixture.ReadAsync(agent);
    Assert.Equal(AgentSettingsStore.FormMode, broken.Mode);
    Assert.True(broken.NeedsRepair);
    Assert.Equal("{ \"personality\": ", broken.Content);
    Assert.Equal(HttpStatusCode.BadRequest, (await fixture.PutAsync(agent, Write(broken, [Set("/personality", "\"x\"")]))).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest, (await fixture.PutAsync(agent, new SettingsWrite(broken.Revision, broken.SchemaRevision, "{ \"personality\": 1 }", null))).StatusCode);

    SettingsView repaired = await fixture.SaveContentAsync(agent, broken, """{ "personality": "fixed", "model": { "name": "m" } }""");
    Assert.False(repaired.NeedsRepair);
    Assert.Empty(repaired.Errors);
  }

  [DatabaseFact]
  public async Task SchemaLessSettingsStayOpaqueText()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    await using Fixture fixture = await Fixture.StartAsync(database);
    RoomMemberView agent = await fixture.PlaceAsync("plain");

    SettingsView view = await fixture.ReadAsync(agent);
    Assert.Equal(AgentSettingsStore.RawMode, view.Mode);
    Assert.Null(view.Schema);
    Assert.Null(view.Notice);
    Assert.Equal("{\"keep\": true}", view.Content);
    Assert.Equal(HttpStatusCode.NoContent, (await fixture.PutTextAsync(agent, "anything at all")).StatusCode);
    Assert.Equal("anything at all", await fixture.Host.Client.GetStringAsync($"{fixture.Root}/agents/{agent.Id}/settings"));

    SettingsView text = await fixture.ReadAsync(agent);
    SettingsView saved = await fixture.SaveContentAsync(agent, text, "still opaque");
    Assert.Equal(HttpStatusCode.Conflict, (await fixture.PutAsync(agent, new SettingsWrite(text.Revision, null, "stale", null))).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest, (await fixture.PutAsync(agent, Write(saved, [Set("/personality", "\"x\"")]))).StatusCode);
    Assert.Equal("still opaque", fixture.SettingsText(agent));
  }

  [DatabaseFact]
  public async Task AnUnresolvedDefinitionKeepsItsStoredSettingsAsText()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    await using Fixture fixture = await Fixture.StartAsync(database);
    RoomMemberView agent = await fixture.PlaceAsync();
    (await fixture.Host.Client.DeleteAsync($"/api/projects/{fixture.Project.Id}/libraries/{TestLibrary.Id}/{TestLibrary.Version}")).EnsureSuccessStatusCode();

    SettingsView view = await fixture.ReadAsync(agent);
    Assert.Equal(AgentSettingsStore.RawMode, view.Mode);
    Assert.Null(view.Schema);
    Assert.Null(view.SchemaRevision);
    Assert.NotNull(view.Notice);
    Assert.Equal(fixture.SettingsText(agent), view.Content);
    SettingsView saved = await fixture.SaveContentAsync(agent, view, view.Content + " ");
    Assert.Equal(view.Content + " ", saved.Content);
  }

  private static SettingsChange Set(string path, string json)
  {
    return new SettingsChange(path, JsonDocument.Parse(json).RootElement.Clone(), false);
  }

  private static SettingsChange Remove(string path)
  {
    return new SettingsChange(path, null, true);
  }

  private static SettingsWrite Write(SettingsView view, List<SettingsChange> changes)
  {
    return new SettingsWrite(view.Revision, view.SchemaRevision, null, changes);
  }

  private static string Compact(JsonElement element)
  {
    using MemoryStream stream = new();
    using (Utf8JsonWriter writer = new(stream))
    {
      element.WriteTo(writer);
    }

    return Encoding.UTF8.GetString(stream.ToArray());
  }

  // A host with a project that references a test library holding a schema-backed agent and a schema-less one.
  private sealed class Fixture : IAsyncDisposable
  {
    private Fixture(TestHost host, Project project, string agentDirectory)
    {
      Host = host;
      Project = project;
      AgentDirectory = agentDirectory;
    }

    public TestHost Host { get; }
    public Project Project { get; }
    public string AgentDirectory { get; }
    public string Root => $"/api/projects/{Project.Id}/room";

    public static async Task<Fixture> StartAsync(TestDatabase database)
    {
      string directory = TestLibrary.Write(database.DataPath, "formed", "Formed", ["sh", "-c", "sleep 60"], InitialSettings, null, SettingsSchemaTests.Schema);
      TestLibrary.Write(database.DataPath, "plain", "Plain", ["sh", "-c", "sleep 60"], "{\"keep\": true}");
      TestHost host = await TestHost.StartAsync(database);
      Project project = (await (await host.Client.PostAsJsonAsync("/api/projects", new ProjectInput("Settings", null, null))).Content.ReadFromJsonAsync<Project>())!;
      (await host.Client.PostAsJsonAsync($"/api/projects/{project.Id}/libraries", new ProjectLibraryInput(TestLibrary.Id, TestLibrary.Version))).EnsureSuccessStatusCode();
      return new Fixture(host, project, directory);
    }

    public async Task<RoomMemberView> PlaceAsync(string agentId = "formed")
    {
      HttpResponseMessage response = await Host.Client.PostAsJsonAsync(Root + "/agents", new PlaceInput(TestLibrary.Reference(agentId)));
      response.EnsureSuccessStatusCode();
      return (await response.Content.ReadFromJsonAsync<RoomMemberView>())!;
    }

    public async Task<SettingsView> ReadAsync(RoomMemberView agent)
    {
      return (await Host.Client.GetFromJsonAsync<SettingsView>(FormPath(agent)))!;
    }

    public Task<HttpResponseMessage> PutAsync(RoomMemberView agent, SettingsWrite write)
    {
      return Host.Client.PutAsJsonAsync(FormPath(agent), write);
    }

    public Task<HttpResponseMessage> PutTextAsync(RoomMemberView agent, string text)
    {
      return Host.Client.PutAsync($"{Root}/agents/{agent.Id}/settings", new StringContent(text, Encoding.UTF8, "text/plain"));
    }

    public async Task<SettingsView> SaveAsync(RoomMemberView agent, SettingsView view, List<SettingsChange> changes)
    {
      return await ExpectSavedAsync(await PutAsync(agent, Write(view, changes)));
    }

    public async Task<SettingsView> SaveContentAsync(RoomMemberView agent, SettingsView view, string content)
    {
      return await ExpectSavedAsync(await PutAsync(agent, new SettingsWrite(view.Revision, view.SchemaRevision, content, null)));
    }

    public string SettingsPath(RoomMemberView agent)
    {
      return Host.Get<RoomPaths>().SettingsFile(Project.Id, agent.Id);
    }

    public string SettingsText(RoomMemberView agent)
    {
      return File.ReadAllText(SettingsPath(agent));
    }

    public async ValueTask DisposeAsync()
    {
      await Host.DisposeAsync();
    }

    private string FormPath(RoomMemberView agent)
    {
      return $"{Root}/agents/{agent.Id}/settings/form";
    }

    private static async Task<SettingsView> ExpectSavedAsync(HttpResponseMessage response)
    {
      Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
      return (await response.Content.ReadFromJsonAsync<SettingsView>())!;
    }
  }
}
