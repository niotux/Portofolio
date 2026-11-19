using Holodeck.Web.Computers;
using Holodeck.Web.Projects;
using Holodeck.Web.Rooms;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Holodeck.Tests;

public sealed class AgentLifecycleTests
{
  private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);
  private static readonly TimeSpan PendingCheck = TimeSpan.FromMilliseconds(250);

  [DatabaseFact]
  public async Task StopWaitsForPendingStartAndStopsTheCreatedAgent()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    TestRoomSetup setup = await TestRoomSetup.CreateAsync(database);
    GatedAgentRuntime runtime = new();
    AgentSupervisor supervisor = await CreateSupervisorAsync(database, setup, runtime);
    Task starting = supervisor.StartAgentAsync(setup.A, CancellationToken.None);
    try
    {
      AgentStart requested = await runtime.Requested.WaitAsync(TestTimeout);
      Task stopping = supervisor.StopAgentAsync(setup.A, CancellationToken.None);
      Assert.NotSame(stopping, await Task.WhenAny(stopping, Task.Delay(PendingCheck)));
      Assert.Equal(0, runtime.StopCalls);

      runtime.ReleaseStart();
      await Task.WhenAll(starting, stopping).WaitAsync(TestTimeout);
      Assert.Equal(1, runtime.StopCalls);
      Assert.Equal(AgentStatus.Stopped, supervisor.Status(setup.A.Id));
      Assert.Null(await setup.Room.FindBySessionAsync(AgentKeys.Hash(requested.Key)));
      Assert.Empty(await setup.Room.ListShouldRunAsync());
      AgentComputerState? stopped = await runtime.InspectAsync(setup.A, CancellationToken.None);
      Assert.NotNull(stopped);
      Assert.False(stopped.Running);
      await supervisor.DeleteAgentAsync(setup.A, CancellationToken.None);
      Assert.Null(await runtime.InspectAsync(setup.A, CancellationToken.None));
    }
    finally
    {
      runtime.ReleaseStart();
      await starting.WaitAsync(TestTimeout);
      await supervisor.StopAgentAsync(setup.A, CancellationToken.None);
    }
  }

  [DatabaseFact]
  public async Task ResetWaitsForPendingStartAndRefusesToEraseTheRunningHome()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    TestRoomSetup setup = await TestRoomSetup.CreateAsync(database);
    GatedAgentRuntime runtime = new();
    AgentSupervisor supervisor = await CreateSupervisorAsync(database, setup, runtime);
    string home = setup.Paths.Home(setup.Project.Id, setup.A.Id);
    Directory.CreateDirectory(home);
    string installed = Path.Combine(home, "installed-tool");
    File.WriteAllText(installed, "keep this installation");
    Task starting = supervisor.StartAgentAsync(setup.A, CancellationToken.None);
    try
    {
      await runtime.Requested.WaitAsync(TestTimeout);
      Task resetting = supervisor.ResetEnvironmentAsync(setup.A, CancellationToken.None);
      Assert.NotSame(resetting, await Task.WhenAny(resetting, Task.Delay(PendingCheck)));
      Assert.True(File.Exists(installed));

      runtime.ReleaseStart();
      await starting.WaitAsync(TestTimeout);
      await Assert.ThrowsAsync<InvalidOperationException>(() => resetting.WaitAsync(TestTimeout));
      Assert.Equal(AgentStatus.Running, supervisor.Status(setup.A.Id));
      Assert.Equal("keep this installation", File.ReadAllText(installed));
      Assert.Equal(0, runtime.StopCalls);
    }
    finally
    {
      runtime.ReleaseStart();
      await starting.WaitAsync(TestTimeout);
      await supervisor.StopAgentAsync(setup.A, CancellationToken.None);
    }
  }

  [DatabaseFact]
  public async Task ShutdownWaitsForPendingStartAndLeavesTheComputerRunning()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    TestRoomSetup setup = await TestRoomSetup.CreateAsync(database);
    GatedAgentRuntime runtime = new();
    AgentSupervisor supervisor = await CreateSupervisorAsync(database, setup, runtime);
    Task starting = supervisor.StartAgentAsync(setup.A, CancellationToken.None);
    try
    {
      await runtime.Requested.WaitAsync(TestTimeout);
      Task shuttingDown = supervisor.StopAsync(CancellationToken.None);
      Assert.NotSame(shuttingDown, await Task.WhenAny(shuttingDown, Task.Delay(PendingCheck)));
      Assert.Equal(0, runtime.StopCalls);

      runtime.ReleaseStart();
      await Task.WhenAll(starting, shuttingDown).WaitAsync(TestTimeout);
      Assert.Equal(0, runtime.StopCalls);
      AgentComputerState? computer = await runtime.InspectAsync(setup.A, CancellationToken.None);
      Assert.NotNull(computer);
      Assert.True(computer.Running);
      Assert.Equal(setup.A, Assert.Single(await setup.Room.ListShouldRunAsync()));
    }
    finally
    {
      runtime.ReleaseStart();
      await starting.WaitAsync(TestTimeout);
      await supervisor.StopAgentAsync(setup.A, CancellationToken.None);
    }
  }

  [DatabaseFact]
  public async Task CancelledStartClearsTheSessionAndRestoreIntent()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    TestRoomSetup setup = await TestRoomSetup.CreateAsync(database);
    GatedAgentRuntime runtime = new();
    AgentSupervisor supervisor = await CreateSupervisorAsync(database, setup, runtime);
    using CancellationTokenSource cancellation = new();
    Task starting = supervisor.StartAgentAsync(setup.A, cancellation.Token);
    try
    {
      AgentStart requested = await runtime.Requested.WaitAsync(TestTimeout);
      cancellation.Cancel();
      await Assert.ThrowsAnyAsync<OperationCanceledException>(() => starting.WaitAsync(TestTimeout));
      Assert.Equal(AgentStatus.Stopped, supervisor.Status(setup.A.Id));
      Assert.Null(await setup.Room.FindBySessionAsync(AgentKeys.Hash(requested.Key)));
      Assert.Empty(await setup.Room.ListShouldRunAsync());
      Assert.Equal(0, runtime.StopCalls);
    }
    finally
    {
      cancellation.Cancel();
      runtime.ReleaseStart();
      try
      {
        await starting.WaitAsync(TestTimeout);
      }
      catch (OperationCanceledException)
      {
      }

      await supervisor.StopAgentAsync(setup.A, CancellationToken.None);
    }
  }

  private static async Task<AgentSupervisor> CreateSupervisorAsync(TestDatabase database, TestRoomSetup setup, GatedAgentRuntime runtime)
  {
    AgentLibrary library = new(setup.Paths);
    ProjectLibraryStore libraries = new(database.Database, library);
    await libraries.InitializeAsync(CancellationToken.None);
    GrantStore grants = new(database.Database);
    await grants.InitializeAsync(CancellationToken.None);
    return new AgentSupervisor(setup.Room, library, setup.Paths, setup.Signals, runtime, grants, NullLogger<AgentSupervisor>.Instance, libraries);
  }
}
