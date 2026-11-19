using Holodeck.Web.Management;
using Holodeck.Web.Rooms;
using Xunit;

namespace Holodeck.Tests;

public sealed class ManagementStoreTests
{
  [DatabaseFact]
  public async Task ConcurrentEnqueuesAndClaimsDoNotDuplicateWork()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    await using ManagementFixture fixture = await ManagementFixture.StartAsync(database);
    ManagementEnqueue input = await fixture.InputAsync();
    List<Task<ManagementEnqueued>> sends = new();
    for (int index = 0; index < 8; index++)
    {
      sends.Add(fixture.Store.EnqueueAsync(fixture.Setup.B, input));
    }

    ManagementEnqueued[] enqueued = await Task.WhenAll(sends);
    int fresh = 0;
    foreach (ManagementEnqueued result in enqueued)
    {
      Assert.Equal(enqueued[0].Job.Id, result.Job.Id);
      if (!result.IsReplay)
      {
        fresh++;
      }
    }

    Assert.Equal(1, fresh);
    Task<ManagementClaim?> first = fixture.Store.ClaimAsync(fixture.Caller, new ManagementClaimInput("same"));
    Task<ManagementClaim?> second = fixture.Store.ClaimAsync(fixture.Caller, new ManagementClaimInput("same"));
    ManagementClaim?[] claims = await Task.WhenAll(first, second);
    Assert.NotNull(claims[0]);
    Assert.NotNull(claims[1]);
    Assert.Equal(claims[0]!.AttemptToken, claims[1]!.AttemptToken);
    Assert.Null(await fixture.Store.ClaimAsync(fixture.Caller, new ManagementClaimInput("different")));
    Assert.Equal(1, await database.CountAsync("SELECT count(*) FROM management_claims"));
    await fixture.EnqueueAsync("second");
    // Empty polls store nothing; retrying that Id can claim later work because the empty poll made no side effect.
    Assert.NotNull(await fixture.Store.ClaimAsync(fixture.Caller, new ManagementClaimInput("different")));
    Assert.Null(await fixture.Store.ClaimAsync(fixture.Caller, new ManagementClaimInput("next")));
    Assert.Equal(2, await database.CountAsync("SELECT count(*) FROM management_claims"));
  }

  [DatabaseFact]
  public async Task ExpiredAndPreviousSessionAttemptsCannotReportOrBeReplayed()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    await using ManagementFixture fixture = await ManagementFixture.StartAsync(database);
    ManagementJob job = await fixture.EnqueueAsync();
    ManagementClaim first = await fixture.ClaimAsync();
    await database.ExecuteAsync("UPDATE management_jobs SET lease_expires_at = clock_timestamp() - interval '1 second'");
    await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Store.ProgressAsync(fixture.Caller, job.Id, new ManagementProgress(first.AttemptToken, 20, "late")));
    await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Store.CompleteAsync(fixture.Caller, job.Id, new ManagementCompletion(first.AttemptToken, ManagementFixture.Value("late"))));
    await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Store.ClaimAsync(fixture.Caller, new ManagementClaimInput("claim")));
    ManagementClaim second = await fixture.ClaimAsync("reclaim");
    Assert.NotEqual(first.AttemptToken, second.AttemptToken);
    await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Store.FailAsync(fixture.Caller, job.Id, new ManagementFailure(first.AttemptToken, "stale")));

    string newHash = AgentKeys.Hash(AgentKeys.Create());
    await fixture.Setup.Room.BeginSessionAsync(fixture.Setup.B.Id, newHash);
    await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Store.ProgressAsync(fixture.Caller, job.Id, new ManagementProgress(second.AttemptToken, null, null)));
    AgentCaller newCaller = new(fixture.Setup.B, newHash);
    ManagementClaim third = (await fixture.Store.ClaimAsync(newCaller, new ManagementClaimInput("new-session")))!;
    Assert.NotEqual(second.AttemptToken, third.AttemptToken);
    await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Store.CompleteAsync(newCaller, job.Id, new ManagementCompletion(second.AttemptToken, ManagementFixture.Value("wrong session"))));
    ManagementJob failed = await fixture.Store.FailAsync(newCaller, job.Id, new ManagementFailure(third.AttemptToken, "bounded failure"));
    Assert.Equal(ManagementStatus.Failed, failed.Status);
    Assert.Equal(failed.Id, (await fixture.Store.FailAsync(newCaller, job.Id, new ManagementFailure(third.AttemptToken, "bounded failure"))).Id);
    await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Store.FailAsync(newCaller, job.Id, new ManagementFailure(third.AttemptToken, "changed failure")));
  }

  [DatabaseFact]
  public async Task CompletionAndCancellationHaveOneTerminalWinner()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    await using ManagementFixture fixture = await ManagementFixture.StartAsync(database);
    ManagementJob job = await fixture.EnqueueAsync();
    ManagementClaim claim = await fixture.ClaimAsync();
    Task<ManagementJob> complete = fixture.Store.CompleteAsync(fixture.Caller, job.Id, new ManagementCompletion(claim.AttemptToken, ManagementFixture.Value("done")));
    Task<ManagementJob> cancel = fixture.Store.CancelAsync(fixture.Setup.B, job.Id);
    try
    {
      await complete;
    }
    catch (InvalidOperationException)
    {
      Assert.Equal(ManagementStatus.Canceled, (await cancel).Status);
    }

    await cancel;
    ManagementJob final = Assert.Single(await fixture.Store.ListAsync(fixture.Setup.B));
    Assert.True(final.Status == ManagementStatus.Succeeded || final.Status == ManagementStatus.Canceled);
    Assert.Equal(final.Status, (await fixture.Store.CancelAsync(fixture.Setup.B, job.Id)).Status);
    if (final.Status == ManagementStatus.Succeeded)
    {
      Assert.Equal("done", final.Result!.Value.GetProperty("text").GetString());
    }
    else
    {
      Assert.Null(final.Result);
      await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Store.CompleteAsync(fixture.Caller, job.Id, new ManagementCompletion(claim.AttemptToken, ManagementFixture.Value("late"))));
    }
  }

  [DatabaseFact]
  public async Task SourceEditsAndReloadKeepOriginalDefinitionsAndSettings()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    await using ManagementFixture fixture = await ManagementFixture.StartAsync(database);
    string settingsPath = fixture.Setup.Paths.SettingsFile(fixture.Setup.Project.Id, fixture.Setup.B.Id);
    File.WriteAllText(settingsPath, "{\"version\":1}");
    ManagementEnqueue input = await fixture.InputAsync();
    ManagementJob job = (await fixture.Store.EnqueueAsync(fixture.Setup.B, input)).Job;
    File.WriteAllText(settingsPath, "{\"version\":2}");
    File.WriteAllText(Path.Combine(fixture.Directory, "result.schema.json"), """{"type":"object","properties":{"number":{"type":"number"}},"required":["number"],"additionalProperties":false}""");
    ManagementDefinition changed = await fixture.Store.ActionsAsync(fixture.Setup.B);
    Assert.NotEqual(job.DefinitionRevision, changed.DefinitionRevision);
    Assert.True((await fixture.Store.EnqueueAsync(fixture.Setup.B, input)).IsReplay);
    ManagementClaim claim = await fixture.ClaimAsync();
    Assert.Equal("{\"version\":1}", claim.SettingsContent);
    ManagementJob result = await fixture.Store.CompleteAsync(fixture.Caller, job.Id, new ManagementCompletion(claim.AttemptToken, ManagementFixture.Value("old schema works")));
    Assert.Equal(ManagementStatus.Succeeded, result.Status);
    await using TestHost reloaded = await TestHost.StartAsync(database);
    ManagementJob recovered = Assert.Single(await reloaded.Get<ManagementStore>().ListAsync(fixture.Setup.B));
    Assert.Equal(result.SettingsRevision, recovered.SettingsRevision);
    Assert.Equal(result.Action.InputSchema.Type, recovered.Action.InputSchema.Type);
    Assert.Equal(result.Result!.Value.GetRawText(), recovered.Result!.Value.GetRawText());
  }

  [DatabaseFact]
  public async Task RemovedMembersCannotClaimAndCancellationRemainsInspectable()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    await using ManagementFixture fixture = await ManagementFixture.StartAsync(database);
    ManagementJob job = await fixture.EnqueueAsync();
    await fixture.Setup.Room.RemoveAsync(fixture.Setup.Project.Id, fixture.Setup.B.Id);
    await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Store.ClaimAsync(fixture.Caller, new ManagementClaimInput("removed")));
    Assert.Equal(ManagementStatus.Canceled, (await fixture.Store.CancelAsync(fixture.Setup.B, job.Id)).Status);
    Assert.Equal(ManagementStatus.Canceled, Assert.Single(await fixture.Store.ListAsync(fixture.Setup.B)).Status);
  }
}
