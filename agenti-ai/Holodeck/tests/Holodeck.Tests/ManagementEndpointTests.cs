using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Holodeck.Web.Management;
using Holodeck.Web.Projects;
using Holodeck.Web.Rooms;
using Xunit;

namespace Holodeck.Tests;

public sealed class ManagementEndpointTests
{
  [DatabaseFact]
  public async Task ValidatedJobsArePrivateDurableAndReplaySafe()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    await using ManagementFixture fixture = await ManagementFixture.StartAsync(database);
    File.WriteAllText(fixture.Setup.Paths.SettingsFile(fixture.Setup.Project.Id, fixture.Setup.B.Id), "{\"privateSetting\":\"snapshot-marker\"}");
    ManagementEnqueue input = await fixture.InputAsync();
    HttpResponseMessage response = await fixture.Host.Client.PostAsJsonAsync(fixture.Root + "/jobs", input);
    Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    ManagementJob job = (await response.Content.ReadFromJsonAsync<ManagementJob>())!;
    Assert.Equal(ManagementStatus.Queued, job.Status);
    Assert.Equal("Prepare", job.Action.Title);
    Assert.Equal(HttpStatusCode.OK, (await fixture.Host.Client.PostAsJsonAsync(fixture.Root + "/jobs", input)).StatusCode);
    Assert.Equal(HttpStatusCode.Conflict, (await fixture.Host.Client.PostAsJsonAsync(fixture.Root + "/jobs", input with { Input = ManagementFixture.Value("changed") })).StatusCode);
    ManagementClaim claim = (await (await fixture.Agent.PostAsJsonAsync("/api/agent/jobs/claim", new ManagementClaimInput("claim"))).Content.ReadFromJsonAsync<ManagementClaim>())!;
    Assert.Equal(job.Id, claim.Job.Id);
    Assert.Contains("snapshot-marker", claim.SettingsContent);
    Assert.Equal(ManagementStatus.Running, claim.Job.Status);
    HttpResponseMessage progress = await fixture.Agent.PostAsJsonAsync($"/api/agent/jobs/{job.Id}/progress", new ManagementProgress(claim.AttemptToken, 40, "Working"));
    Assert.Equal(HttpStatusCode.OK, progress.StatusCode);
    ManagementCompletion completion = new(claim.AttemptToken, ManagementFixture.Value("finished"));
    Assert.Equal(HttpStatusCode.OK, (await fixture.Agent.PostAsJsonAsync($"/api/agent/jobs/{job.Id}/complete", completion)).StatusCode);
    Assert.Equal(HttpStatusCode.OK, (await fixture.Agent.PostAsJsonAsync($"/api/agent/jobs/{job.Id}/complete", completion)).StatusCode);
    Assert.Equal(HttpStatusCode.Conflict, (await fixture.Agent.PostAsJsonAsync($"/api/agent/jobs/{job.Id}/complete", completion with { Result = ManagementFixture.Value("different") })).StatusCode);
    string publicJobs = await fixture.Host.Client.GetStringAsync(fixture.Root + "/jobs");
    Assert.DoesNotContain("snapshot-marker", publicJobs);
    Assert.DoesNotContain(claim.AttemptToken, publicJobs);
    Assert.DoesNotContain(fixture.Caller.SessionHash, publicJobs);
    List<ManagementJob> listed = JsonSerializer.Deserialize<List<ManagementJob>>(publicJobs, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    Assert.Equal(ManagementStatus.Succeeded, Assert.Single(listed).Status);
    Assert.Equal(0, await database.CountAsync("SELECT count(*) FROM room_packets"));
    Assert.Equal(1, await database.CountAsync("SELECT count(*) FROM management_jobs"));
  }

  [DatabaseFact]
  public async Task AnonymousCrossAgentStoppedAndUnresolvedRequestsAreRejected()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    await using ManagementFixture fixture = await ManagementFixture.StartAsync(database);
    ManagementJob job = await fixture.EnqueueAsync();
    ManagementClaim claim = await fixture.ClaimAsync();
    Assert.Equal(HttpStatusCode.Unauthorized, (await fixture.Host.Client.PostAsJsonAsync("/api/agent/jobs/claim", new ManagementClaimInput("anonymous"))).StatusCode);
    string key = AgentKeys.Create();
    await fixture.Setup.Room.BeginSessionAsync(fixture.Setup.A.Id, AgentKeys.Hash(key));
    using HttpClient other = new() { BaseAddress = fixture.Host.Client.BaseAddress };
    other.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
    Assert.Equal(HttpStatusCode.NoContent, (await other.PostAsJsonAsync("/api/agent/jobs/claim", new ManagementClaimInput("other"))).StatusCode);
    Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync($"/api/agent/jobs/{job.Id}/complete", new ManagementCompletion(claim.AttemptToken, ManagementFixture.Value("stolen")))).StatusCode);
    await fixture.Setup.Room.EndSessionAsync(fixture.Setup.B.Id);
    Assert.Equal(HttpStatusCode.Unauthorized, (await fixture.Agent.PostAsJsonAsync($"/api/agent/jobs/{job.Id}/progress", new ManagementProgress(claim.AttemptToken, 50, null))).StatusCode);
    Assert.Equal(HttpStatusCode.Conflict, (await fixture.Host.Client.PostAsJsonAsync(fixture.Root + "/jobs", await fixture.InputAsync("stopped"))).StatusCode);
    await fixture.Host.Get<ProjectLibraryStore>().RemoveAsync(fixture.Setup.Project.Id, TestLibrary.Id, TestLibrary.Version);
    Assert.Equal(HttpStatusCode.Conflict, (await fixture.Host.Client.GetAsync(fixture.Root + "/actions")).StatusCode);
    Assert.Single((await fixture.Host.Client.GetFromJsonAsync<List<ManagementJob>>(fixture.Root + "/jobs"))!);
  }

  [DatabaseFact]
  public async Task InvalidInputsAndResultsReturnFieldPathsWithoutChangingTheJob()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    await using ManagementFixture fixture = await ManagementFixture.StartAsync(database);
    ManagementEnqueue input = await fixture.InputAsync();
    HttpResponseMessage rejected = await fixture.Host.Client.PostAsJsonAsync(fixture.Root + "/jobs", input with { Input = ManagementFixture.Value("") });
    Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
    Assert.Equal("/text", Assert.Single((await rejected.Content.ReadFromJsonAsync<ManagementValidationError>())!.FieldErrors).Path);
    Assert.Equal(0, await database.CountAsync("SELECT count(*) FROM management_jobs"));
    ManagementJob job = await fixture.EnqueueAsync();
    ManagementClaim claim = await fixture.ClaimAsync();
    Assert.Equal(HttpStatusCode.BadRequest, (await fixture.Agent.PostAsJsonAsync($"/api/agent/jobs/{job.Id}/complete", new ManagementCompletion(claim.AttemptToken, ManagementFixture.Value("")))).StatusCode);
    Assert.Equal(ManagementStatus.Running, Assert.Single(await fixture.Store.ListAsync(fixture.Setup.B)).Status);
    Assert.Equal(HttpStatusCode.BadRequest, (await fixture.Agent.PostAsJsonAsync($"/api/agent/jobs/{job.Id}/progress", new ManagementProgress(claim.AttemptToken, 101, null))).StatusCode);
    Assert.Equal(HttpStatusCode.Conflict, (await fixture.Host.Client.PostAsJsonAsync(fixture.Root + "/jobs", input with { RequestId = "stale", DefinitionRevision = "old-definition" })).StatusCode);
  }
}
