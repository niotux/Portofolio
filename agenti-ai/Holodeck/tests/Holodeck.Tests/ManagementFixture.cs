using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Holodeck.Web.Management;
using Holodeck.Web.Projects;
using Holodeck.Web.Rooms;

namespace Holodeck.Tests;

internal sealed class ManagementFixture(TestHost host, TestRoomSetup setup, string directory, HttpClient agent, AgentCaller caller) : IAsyncDisposable
{
  public const string Schema = """{"type":"object","properties":{"text":{"type":"string","minLength":1,"maxLength":64}},"required":["text"],"additionalProperties":false}""";
  public TestHost Host { get; } = host;
  public TestRoomSetup Setup { get; } = setup;
  public string Directory { get; } = directory;
  public HttpClient Agent { get; } = agent;
  public AgentCaller Caller { get; } = caller;
  public ManagementStore Store => Host.Get<ManagementStore>();
  public string Root => $"/api/projects/{Setup.Project.Id}/room/agents/{Setup.B.Id}";

  public static async Task<ManagementFixture> StartAsync(TestDatabase database)
  {
    TestHost host = await TestHost.StartAsync(database);
    TestRoomSetup setup = await TestRoomSetup.CreateAsync(database);
    string directory = Path.Combine(database.DataPath, "libs", TestLibrary.Id, "agents", "agent");
    PackageAgentManifest manifest = JsonSerializer.Deserialize<PackageAgentManifest>(File.ReadAllText(Path.Combine(directory, "Agent.json")))!;
    File.WriteAllText(Path.Combine(directory, "Agent.json"), JsonSerializer.Serialize(manifest with { Actions = "actions.json" }));
    WriteDefinition(directory);
    await host.Get<ProjectLibraryStore>().AddAsync(setup.Project.Id, TestLibrary.Id, TestLibrary.Version);
    string key = AgentKeys.Create();
    string hash = AgentKeys.Hash(key);
    await setup.Room.BeginSessionAsync(setup.B.Id, hash);
    HttpClient agent = new() { BaseAddress = host.Client.BaseAddress };
    agent.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
    return new ManagementFixture(host, setup, directory, agent, new AgentCaller(setup.B, hash));
  }

  public static void WriteDefinition(string directory)
  {
    File.WriteAllText(Path.Combine(directory, "input.schema.json"), Schema);
    File.WriteAllText(Path.Combine(directory, "result.schema.json"), Schema);
    List<ManagementActionManifest> actions =
    [
      new("prepare", "Prepare", "Prepare a value.", "input.schema.json", "result.schema.json", ["apply"]),
      new("apply", "Apply", "Apply an edited value.", "input.schema.json", "result.schema.json", null)
    ];
    File.WriteAllText(Path.Combine(directory, "actions.json"), JsonSerializer.Serialize(new ManagementManifest(1, actions)));
  }

  public static JsonElement Value(string text) => JsonSerializer.SerializeToElement(new ManagementTestValue(text));

  public async Task<ManagementEnqueue> InputAsync(string requestId = "request")
  {
    ManagementDefinition definition = await Store.ActionsAsync(Setup.B);
    return new ManagementEnqueue("prepare", requestId, definition.DefinitionRevision, Value("first"));
  }

  public async Task<ManagementJob> EnqueueAsync(string requestId = "request")
  {
    ManagementEnqueued result = await Store.EnqueueAsync(Setup.B, await InputAsync(requestId));
    return result.Job;
  }

  public async Task<ManagementClaim> ClaimAsync(string claimId = "claim")
  {
    return (await Store.ClaimAsync(Caller, new ManagementClaimInput(claimId)))!;
  }

  public async ValueTask DisposeAsync()
  {
    Agent.Dispose();
    await Host.DisposeAsync();
  }
}
