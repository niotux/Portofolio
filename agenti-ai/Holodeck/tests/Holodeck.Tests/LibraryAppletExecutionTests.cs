using System.Net;
using System.Net.Http.Json;
using Holodeck.Web.Projects;
using Holodeck.Web.Computers;
using Xunit;

namespace Holodeck.Tests;

public sealed class LibraryAppletExecutionTests
{
  [DatabaseFact]
  public async Task WorkspaceReadsDoNotStartProjectExecution()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    await using TestHost host = await TestHost.StartAsync(database);
    Project project = await CreateProjectAsync(host);

    using HttpResponseMessage workspace = await host.Client.GetAsync($"/api/projects/{project.Id}/workspace");
    Assert.Equal(HttpStatusCode.OK, workspace.StatusCode);
    Assert.False(await host.Get<ProjectExecutionStore>().IsRunningAsync(project.Id, CancellationToken.None));
    Assert.Empty(await host.Get<ProjectExecutionStore>().ListRunningAsync(CancellationToken.None));
  }

  [DatabaseFact]
  public async Task RepeatedActivationAndStopPersistExplicitIntentWithoutAgents()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    await using TestHost host = await TestHost.StartAsync(database);
    Project project = await CreateProjectAsync(host);
    string root = $"/api/projects/{project.Id}";
    for (int attempt = 0; attempt < 2; attempt++)
    {
      using HttpResponseMessage started = await host.Client.PostAsync(root + "/activate", null);
      Assert.Equal(HttpStatusCode.NoContent, started.StatusCode);
    }

    Assert.True(await host.Get<ProjectExecutionStore>().IsRunningAsync(project.Id, CancellationToken.None));
    Assert.Equal(project.Id, Assert.Single(await host.Get<ProjectExecutionStore>().ListRunningAsync(CancellationToken.None)));
    for (int attempt = 0; attempt < 2; attempt++)
    {
      using HttpResponseMessage stopped = await host.Client.PostAsync(root + "/stop", null);
      Assert.Equal(HttpStatusCode.NoContent, stopped.StatusCode);
    }

    Assert.False(await host.Get<ProjectExecutionStore>().IsRunningAsync(project.Id, CancellationToken.None));
    Assert.Empty(await host.Get<ProjectExecutionStore>().ListRunningAsync(CancellationToken.None));
  }

  [DatabaseFact]
  public async Task HolodeckRestartPreservesProjectIntentAndExplicitStopSurvivesAnotherRestart()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    string projectId;
    await using (TestHost first = await TestHost.StartAsync(database))
    {
      Project project = await CreateProjectAsync(first);
      projectId = project.Id;
      using HttpResponseMessage started = await first.Client.PostAsync($"/api/projects/{projectId}/activate", null);
      Assert.Equal(HttpStatusCode.NoContent, started.StatusCode);
    }

    await using (TestHost second = await TestHost.StartAsync(database))
    {
      Assert.True(await second.Get<ProjectExecutionStore>().IsRunningAsync(projectId, CancellationToken.None));
      using HttpResponseMessage stopped = await second.Client.PostAsync($"/api/projects/{projectId}/stop", null);
      Assert.Equal(HttpStatusCode.NoContent, stopped.StatusCode);
    }

    await using TestHost third = await TestHost.StartAsync(database);
    Assert.False(await third.Get<ProjectExecutionStore>().IsRunningAsync(projectId, CancellationToken.None));
    Assert.Empty(await third.Get<ProjectExecutionStore>().ListRunningAsync(CancellationToken.None));
  }

  [DatabaseFact]
  public async Task BrokenLibraryDoesNotPreventOpeningTheProject()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    using LibraryAppletFixture fixture = new(database.DataPath);
    fixture.Write(runtime: new LibraryEnvironmentDefinition("untrusted:1", []));
    await using TestHost host = await TestHost.StartAsync(database);
    Project project = await CreateProjectAsync(host);
    using HttpResponseMessage attached = await host.Client.PostAsJsonAsync($"/api/projects/{project.Id}/libraries", new ProjectLibraryInput("utilities", "1.0.0"));
    attached.EnsureSuccessStatusCode();

    using HttpResponseMessage activation = await host.Client.PostAsync($"/api/projects/{project.Id}/activate", null);
    Assert.Equal(HttpStatusCode.OK, activation.StatusCode);
    ProjectActivationResult result = (await activation.Content.ReadFromJsonAsync<ProjectActivationResult>())!;
    Assert.Equal("utilities", Assert.Single(result.Failures).LibraryId);
    Assert.True(await host.Get<ProjectExecutionStore>().IsRunningAsync(project.Id, CancellationToken.None));
    using HttpResponseMessage workspace = await host.Client.GetAsync($"/api/projects/{project.Id}/workspace");
    Assert.Equal(HttpStatusCode.OK, workspace.StatusCode);
  }

  [DatabaseFact]
  public async Task ActivationCannotCreateAnEnvironmentAfterProjectDeletion()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    await using TestHost host = await TestHost.StartAsync(database);
    Project project = await CreateProjectAsync(host);
    TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    ProjectApplications applications = host.Get<ProjectApplications>();
    Task deletion = applications.DeleteAsync(project.Id, async () =>
    {
      entered.SetResult();
      await release.Task;
      await host.Get<ProjectStore>().DeleteAsync(project.Id, CancellationToken.None);
    }, CancellationToken.None);
    await entered.Task;
    Task activation = applications.ActivateAsync(project.Id, CancellationToken.None);
    Assert.False(activation.IsCompleted);
    release.SetResult();
    await deletion;
    await Assert.ThrowsAsync<KeyNotFoundException>(() => activation);
  }

  private static async Task<Project> CreateProjectAsync(TestHost host)
  {
    using HttpResponseMessage response = await host.Client.PostAsJsonAsync("/api/projects", new ProjectInput("Execution", null, null));
    response.EnsureSuccessStatusCode();
    return (await response.Content.ReadFromJsonAsync<Project>())!;
  }
}
