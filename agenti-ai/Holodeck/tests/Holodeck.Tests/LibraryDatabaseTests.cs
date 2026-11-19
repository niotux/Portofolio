using Holodeck.Web.Computers;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Holodeck.Web.AgentData;
using Holodeck.Web.Configuration;
using Holodeck.Web.Projects;
using Holodeck.Web.Rooms;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace Holodeck.Tests;

public sealed class LibraryDatabaseTests
{
  [DatabaseFact]
  public async Task SharedLibraryDataCrossesProjectsButNotPrivateOrHostSchemasAndSurvivesDeletion()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    await using TestHost host = await TestHost.StartAsync(database);
    TestRoomSetup first = await TestRoomSetup.CreateAsync(database);
    TestRoomSetup second = await TestRoomSetup.CreateAsync(database);
    AgentDatabases databases = host.Get<AgentDatabases>();
    string route = "/api/agent/library-database/test";
    try
    {
      using HttpClient a = await SessionAsync(host, first.Room, first.A);
      using HttpClient b = await SessionAsync(host, first.Room, first.B);
      using HttpClient otherProject = await SessionAsync(host, second.Room, second.A);
      JsonElement identity = await a.GetFromJsonAsync<JsonElement>("/api/agent/me");
      Assert.Contains(AgentCapabilities.LibraryDatabase, identity.GetProperty("capabilities").Deserialize<List<string>>()!);
      await ResultAsync(a, route, Request("CREATE TABLE notes(text text NOT NULL); INSERT INTO notes VALUES('shared')"));
      Assert.Equal("shared", (await ResultAsync(b, route, Request("SELECT text FROM notes"))).GetProperty("rows")[0][0].GetString());
      Assert.Equal("shared", (await ResultAsync(otherProject, route, Request("SELECT text FROM notes"))).GetProperty("rows")[0][0].GetString());
      Assert.Equal("shared", (await ResultAsync(host.Client, "/api/libraries/test/1.0.0/database", Request("SELECT text FROM notes"))).GetProperty("rows")[0][0].GetString());
      Assert.Equal(HttpStatusCode.NotFound, (await host.Client.PostAsJsonAsync("/api/libraries/test/9.9.9/database", Request("SELECT 1"))).StatusCode);

      await ResultAsync(a, "/api/agent/database", Request("CREATE TABLE private_notes(text text); INSERT INTO private_notes VALUES('private')"));
      Assert.Equal(HttpStatusCode.BadRequest, (await a.PostAsJsonAsync(route, Request($"SELECT * FROM agent_{first.A.Id}.private_notes"))).StatusCode);
      string libraryRole = (await ResultAsync(a, route, Request("SELECT current_user"))).GetProperty("rows")[0][0].GetString()!;
      Assert.Equal(HttpStatusCode.BadRequest, (await a.PostAsJsonAsync("/api/agent/database", Request($"SELECT * FROM {libraryRole}.notes"))).StatusCode);
      string hostSchema = new NpgsqlConnectionStringBuilder(database.ScopedConnectionString).SearchPath!;
      Assert.Equal(HttpStatusCode.BadRequest, (await a.PostAsJsonAsync(route, Request($"SELECT * FROM {hostSchema}.room_members"))).StatusCode);
      Assert.Equal(HttpStatusCode.BadRequest, (await a.PostAsJsonAsync(route, Request("SELECT * FROM library_databases"))).StatusCode);

      SqlRequest broken = new([Statement("INSERT INTO notes VALUES('must roll back')"), Statement("SELECT * FROM missing_table")]);
      Assert.Equal(HttpStatusCode.BadRequest, (await a.PostAsJsonAsync(route, broken)).StatusCode);
      Assert.Equal(1, (await ResultAsync(a, route, Request("SELECT count(*) FROM notes"))).GetProperty("rows")[0][0].GetInt64());

      await databases.DropProjectAsync(first.Project.Id);
      await new ProjectStore(database.Database).DeleteAsync(first.Project.Id, CancellationToken.None);
      Assert.Equal("shared", (await ResultAsync(otherProject, route, Request("SELECT text FROM notes"))).GetProperty("rows")[0][0].GetString());
      await using AgentDatabases restarted = NewDatabases(database);
      SqlResponse retained = await restarted.RunLibraryAsync("test/1.0.0", Request("SELECT text FROM notes"), false, CancellationToken.None);
      Assert.Equal("shared", retained.Results[0].Rows[0][0]);
    }
    finally
    {
      await databases.DropProjectAsync(first.Project.Id);
      await databases.DropProjectAsync(second.Project.Id);
      await databases.DropLibraryAsync("test/1.0.0");
    }
  }

  [DatabaseFact]
  public async Task AccessUsesOnlyOwnPackageOrExactDirectDependencyAndRejectsInvalidSessions()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    await using TestHost host = await TestHost.StartAsync(database);
    TestRoomSetup setup = await TestRoomSetup.CreateAsync(database);
    WritePackage(database.DataPath, "leaf", "1.0.0", []);
    WritePackage(database.DataPath, "shared", "1.0.0", [new LibraryDependency("leaf", "1.0.0")]);
    WritePackage(database.DataPath, "shared", "2.0.0", []);
    WritePackage(database.DataPath, "unrelated", "1.0.0", []);
    SetTestDependencies(database.DataPath, [new LibraryDependency("shared", "1.0.0")]);
    RoomMember v2 = await setup.Room.PlaceAsync(setup.Project.Id, host.Get<AgentLibrary>().Get("shared/2.0.0/agents/agent"));
    AgentDatabases databases = host.Get<AgentDatabases>();
    try
    {
      using HttpClient a = await SessionAsync(host, setup.Room, setup.A);
      using HttpClient newer = await SessionAsync(host, setup.Room, v2);
      string route = "/api/agent/library-database/shared";
      Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.PostAsJsonAsync(route, Request("SELECT 1"))).StatusCode);
      Assert.Equal(HttpStatusCode.Unauthorized, (await a.PostAsJsonAsync("/api/agent/library-database/unrelated", Request("SELECT 1"))).StatusCode);
      Assert.Equal(HttpStatusCode.Unauthorized, (await a.PostAsJsonAsync("/api/agent/library-database/leaf", Request("SELECT 1"))).StatusCode);
      await ResultAsync(a, route, Request("CREATE TABLE releases(text text); INSERT INTO releases VALUES('v1')"));
      Assert.Equal(HttpStatusCode.BadRequest, (await newer.PostAsJsonAsync(route, Request("SELECT * FROM releases"))).StatusCode);
      await ResultAsync(newer, route, Request("CREATE TABLE releases(text text); INSERT INTO releases VALUES('v2')"));
      Assert.Equal("v1", (await ResultAsync(a, route, Request("SELECT text FROM releases"))).GetProperty("rows")[0][0].GetString());
      Assert.Equal("v2", (await ResultAsync(newer, route, Request("SELECT text FROM releases"))).GetProperty("rows")[0][0].GetString());
      string newerRole = (await ResultAsync(newer, route, Request("SELECT current_user"))).GetProperty("rows")[0][0].GetString()!;
      Assert.Equal(HttpStatusCode.BadRequest, (await a.PostAsJsonAsync(route, Request($"SELECT * FROM {newerRole}.releases"))).StatusCode);

      await setup.Room.EndSessionAsync(setup.A.Id);
      Assert.Equal(HttpStatusCode.Unauthorized, (await a.PostAsJsonAsync(route, Request("SELECT 1"))).StatusCode);
      using HttpClient unresolved = await SessionAsync(host, setup.Room, setup.B);
      File.Delete(Path.Combine(database.DataPath, "libs", "test", "agents", "agent", "Agent.json"));
      Assert.Equal(HttpStatusCode.NotFound, (await unresolved.PostAsJsonAsync(route, Request("SELECT 1"))).StatusCode);
    }
    finally
    {
      await databases.DropProjectAsync(setup.Project.Id);
      await databases.DropLibraryAsync("shared/1.0.0");
      await databases.DropLibraryAsync("shared/2.0.0");
    }
  }

  [DatabaseFact]
  public async Task ConcurrentProvisioningAcrossHostsReusesOneLibraryLogin()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    await using TestHost host = await TestHost.StartAsync(database);
    AgentDatabases first = host.Get<AgentDatabases>();
    AgentDatabases second = NewDatabases(database);
    try
    {
      Task<SqlResponse> a = first.RunLibraryAsync("concurrent/1.0.0", Request("SELECT current_user"), false, CancellationToken.None);
      Task<SqlResponse> b = second.RunLibraryAsync("concurrent/1.0.0", Request("SELECT current_user"), false, CancellationToken.None);
      SqlResponse[] results = await Task.WhenAll(a, b);
      Assert.Equal(results[0].Results[0].Rows[0][0], results[1].Results[0].Rows[0][0]);
      Assert.Equal(1, await database.CountAsync("SELECT count(*) FROM library_databases WHERE library_key = 'concurrent/1.0.0'"));
    }
    finally
    {
      await second.DisposeAsync();
      await first.DropLibraryAsync("concurrent/1.0.0");
    }
  }

  private static AgentDatabases NewDatabases(TestDatabase database)
  {
    return new AgentDatabases(database.Database, Options.Create(new DatabaseOptions { ConnectionString = database.ScopedConnectionString }));
  }

  private static void WritePackage(string dataPath, string id, string version, List<LibraryDependency> dependencies)
  {
    string library = Path.Combine(dataPath, "libs", id + "-" + version);
    string agent = Path.Combine(library, "agents", "agent");
    Directory.CreateDirectory(agent);
    Directory.CreateDirectory(Path.Combine(library, "teams"));
    File.WriteAllText(Path.Combine(library, "Library.json"), JsonSerializer.Serialize(new LibraryManifest(1, id, version, id, null, dependencies, Runtime: new LibraryEnvironmentDefinition(TestLibrary.RuntimeImage, new()))));
    File.WriteAllText(Path.Combine(agent, "Agent.json"), JsonSerializer.Serialize(new PackageAgentManifest("agent", id, "Test agent", new AgentLaunch("sh", []), null)));
  }

  private static void SetTestDependencies(string dataPath, List<LibraryDependency> dependencies)
  {
    string path = Path.Combine(dataPath, "libs", "test", "Library.json");
    File.WriteAllText(path, JsonSerializer.Serialize(new LibraryManifest(1, TestLibrary.Id, TestLibrary.Version, "Test library", null, dependencies, Runtime: new LibraryEnvironmentDefinition(TestLibrary.RuntimeImage, new()))));
  }

  private static async Task<HttpClient> SessionAsync(TestHost host, RoomStore room, RoomMember member)
  {
    string key = AgentKeys.Create();
    await room.BeginSessionAsync(member.Id, AgentKeys.Hash(key));
    HttpClient client = new() { BaseAddress = host.Client.BaseAddress };
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
    return client;
  }

  private static async Task<JsonElement> ResultAsync(HttpClient client, string route, SqlRequest request)
  {
    using HttpResponseMessage response = await client.PostAsJsonAsync(route, request);
    string text = await response.Content.ReadAsStringAsync();
    Assert.True(response.IsSuccessStatusCode, text);
    using JsonDocument document = JsonDocument.Parse(text);
    return document.RootElement.GetProperty("results")[0].Clone();
  }

  private static SqlRequest Request(string sql, params object[] parameters)
  {
    return new SqlRequest([Statement(sql, parameters)]);
  }

  private static SqlStatement Statement(string sql, params object[] parameters)
  {
    List<JsonElement> values = new();
    foreach (object parameter in parameters)
    {
      values.Add(JsonSerializer.SerializeToElement(parameter));
    }

    return new SqlStatement(sql, values);
  }
}
