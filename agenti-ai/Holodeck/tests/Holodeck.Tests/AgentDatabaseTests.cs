using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Holodeck.Web.AgentData;
using Holodeck.Web.Rooms;
using Npgsql;
using Xunit;

namespace Holodeck.Tests;

public sealed class AgentDatabaseTests
{
  [DatabaseFact]
  public async Task AgentKeepsVectorsInItsOwnSchemaAndCannotReachAnythingElse()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    await using TestHost host = await TestHost.StartAsync(database);
    TestRoomSetup setup = await TestRoomSetup.CreateAsync(database);
    AgentDatabases databases = host.Get<AgentDatabases>();
    try
    {
      using HttpClient a = await SessionAsync(host, setup, setup.A);
      using HttpClient b = await SessionAsync(host, setup, setup.B);
      Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.PostAsJsonAsync("/api/agent/database", Request("SELECT 1"))).StatusCode);

      SqlRequest create = new([Statement("CREATE TABLE notes(id bigserial PRIMARY KEY, text text NOT NULL, at timestamptz NOT NULL, embedding vector(2) NOT NULL)"), Statement("INSERT INTO notes(text, at, embedding) VALUES($1, $2, $3::vector), ($4, $2, $5::vector)", "near", "2026-11-05T10:00:00Z", new[] { 1.0, 0.0 }, "far", new[] { 0.0, 1.0 })]);
      Assert.Equal(HttpStatusCode.OK, (await a.PostAsJsonAsync("/api/agent/database", create)).StatusCode);
      JsonElement nearest = await ResultAsync(a, Request("SELECT text, embedding FROM notes ORDER BY embedding <=> $1::vector LIMIT 1", new[] { 0.9, 0.1 }));
      Assert.Equal("near", nearest.GetProperty("rows")[0][0].GetString());
      Assert.Equal(1.0, nearest.GetProperty("rows")[0][1][0].GetDouble());

      SqlRequest halfBroken = new([Statement("INSERT INTO notes(text, at, embedding) VALUES('lost', now(), '[1,1]')"), Statement("SELECT * FROM missing_table")]);
      HttpResponseMessage broken = await a.PostAsJsonAsync("/api/agent/database", halfBroken);
      Assert.Equal(HttpStatusCode.BadRequest, broken.StatusCode);
      Assert.Contains("Statement 2", await broken.Content.ReadAsStringAsync());
      JsonElement count = await ResultAsync(a, Request("SELECT count(*) FROM notes"));
      Assert.Equal(2, count.GetProperty("rows")[0][0].GetInt64());

      string holodeckSchema = new NpgsqlConnectionStringBuilder(database.ScopedConnectionString).SearchPath!;
      Assert.Equal(HttpStatusCode.BadRequest, (await a.PostAsJsonAsync("/api/agent/database", Request($"SELECT * FROM {holodeckSchema}.room_members"))).StatusCode);
      Assert.Equal(HttpStatusCode.BadRequest, (await a.PostAsJsonAsync("/api/agent/database", Request("SELECT * FROM agent_databases"))).StatusCode);
      Assert.Equal(HttpStatusCode.BadRequest, (await b.PostAsJsonAsync("/api/agent/database", Request($"SELECT * FROM agent_{setup.A.Id}.notes"))).StatusCode);
      Assert.Equal(HttpStatusCode.BadRequest, (await b.PostAsJsonAsync("/api/agent/database", Request("SELECT * FROM notes"))).StatusCode);

      string room = $"/api/projects/{setup.Project.Id}/room/agents/{setup.A.Id}/database";
      JsonElement looked = JsonDocument.Parse(await (await host.Client.PostAsJsonAsync(room, Request("SELECT text FROM notes ORDER BY id"))).Content.ReadAsStringAsync()).RootElement;
      Assert.Equal(2, looked.GetProperty("results")[0].GetProperty("rows").GetArrayLength());
      Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.PostAsJsonAsync(room, Request("DELETE FROM notes"))).StatusCode);

      Assert.Equal(HttpStatusCode.NoContent, (await host.Client.DeleteAsync(room)).StatusCode);
      Assert.Equal(HttpStatusCode.BadRequest, (await a.PostAsJsonAsync("/api/agent/database", Request("SELECT * FROM notes"))).StatusCode);
      Assert.Equal(HttpStatusCode.OK, (await a.PostAsJsonAsync("/api/agent/database", Request("CREATE TABLE fresh(id int)"))).StatusCode);
    }
    finally
    {
      await databases.DropProjectAsync(setup.Project.Id);
    }

    Assert.Equal(0, await database.CountAsync($"SELECT count(*) FROM pg_roles WHERE rolname IN ('agent_{setup.A.Id}', 'agent_{setup.B.Id}')"));
  }

  private static async Task<HttpClient> SessionAsync(TestHost host, TestRoomSetup setup, RoomMember member)
  {
    string key = AgentKeys.Create();
    await setup.Room.BeginSessionAsync(member.Id, AgentKeys.Hash(key));
    HttpClient client = new() { BaseAddress = host.Client.BaseAddress };
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
    return client;
  }

  private static async Task<JsonElement> ResultAsync(HttpClient client, SqlRequest request)
  {
    HttpResponseMessage response = await client.PostAsJsonAsync("/api/agent/database", request);
    string body = await response.Content.ReadAsStringAsync();
    Assert.True(response.IsSuccessStatusCode, body);
    return JsonDocument.Parse(body).RootElement.GetProperty("results")[0];
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
