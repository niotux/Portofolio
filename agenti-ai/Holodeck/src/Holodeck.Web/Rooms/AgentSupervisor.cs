using System.Text;
using Holodeck.Web.Computers;
using Holodeck.Web.Projects;

namespace Holodeck.Web.Rooms;

// Connects agents to their processes in their libraries' environments. Host shutdown leaves them running; explicit stop
// revokes access.
public sealed class AgentSupervisor(RoomStore room, AgentLibrary library, RoomPaths paths, RoomSignals signals, IAgentRuntime runtime, GrantStore grants, ILogger<AgentSupervisor> logger, ProjectLibraryStore projectLibraries) : IHostedService
{
  private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(2);
  private readonly CancellationTokenSource shutdown = new();
  private Task? monitoring;
  private readonly Dictionary<string, AgentRun> runs = new();
  private readonly Dictionary<string, SemaphoreSlim> lifecycle = new();
  private readonly Lock gate = new();
  private readonly Lock logGate = new();
  private bool shuttingDown;

  public async Task StartAsync(CancellationToken cancellationToken)
  {
    try
    {
      await runtime.PrepareAsync(cancellationToken);
    }
    catch (Exception exception) when (exception is InvalidOperationException || exception is HttpRequestException)
    {
      logger.LogError(exception, "Agent computers are unavailable; agents cannot start.");
      return;
    }

    await RefreshAsync(cancellationToken, true);
    monitoring = MonitorAsync(shutdown.Token);
  }

  // Holodeck disconnects; the agent's process keeps its own lifetime.
  public async Task StopAsync(CancellationToken cancellationToken)
  {
    List<SemaphoreSlim> pending = new();
    lock (gate)
    {
      shuttingDown = true;
      foreach (SemaphoreSlim lifecycleGate in lifecycle.Values)
      {
        pending.Add(lifecycleGate);
      }
    }

    await shutdown.CancelAsync();
    if (monitoring is not null)
    {
      await monitoring;
    }

    foreach (SemaphoreSlim lifecycleGate in pending)
    {
      await lifecycleGate.WaitAsync(cancellationToken);
      lifecycleGate.Release();
    }
  }

  private async Task MonitorAsync(CancellationToken token)
  {
    try
    {
      using PeriodicTimer timer = new(RefreshInterval);
      while (await timer.WaitForNextTickAsync(token))
      {
        await RefreshAsync(token);
      }
    }
    catch (OperationCanceledException) when (token.IsCancellationRequested)
    {
    }
  }

  private async Task RefreshAsync(CancellationToken token, bool provisionMissing = false)
  {
    foreach (RoomMember member in await room.ListAgentsAsync())
    {
      SemaphoreSlim pending = LifecycleFor(member.Id);
      await pending.WaitAsync(token);
      try
      {
        RoomMember current = await room.GetMemberAsync(member.ProjectId, member.Id);
        if (current.IsRemoved)
        {
          continue;
        }

        AgentComputerState? state = await runtime.InspectAsync(member, token);
        if (state is null)
        {
          if (provisionMissing)
          {
            await CreateAgentCoreAsync(member, token);
          }
        }
        else if (state.Running && Status(member.Id) != AgentStatus.Running)
        {
          await StartAgentCoreAsync(member, token);
        }
        else if (!state.Running && Status(member.Id) != AgentStatus.Running)
        {
          await ClearRunAsync(member);
        }
      }
      catch (Exception exception) when (exception is KeyNotFoundException || exception is InvalidOperationException || exception is HttpRequestException || exception is IOException)
      {
        logger.LogWarning(exception, "Could not connect to agent {Name}.", member.Name);
      }
      finally
      {
        pending.Release();
      }
    }
  }

  public async Task CreateAgentAsync(RoomMember member, CancellationToken token)
  {
    SemaphoreSlim pending = LifecycleFor(member.Id);
    await pending.WaitAsync(token);
    try
    {
      await CreateAgentCoreAsync(member, token);
    }
    finally
    {
      pending.Release();
    }
  }

  private async Task CreateAgentCoreAsync(RoomMember member, CancellationToken token)
  {
    member = await room.GetMemberAsync(member.ProjectId, member.Id);
    if (member.IsYou || member.IsRemoved || member.Library is null)
    {
      throw new InvalidOperationException("Only agents in the room can own a computer.");
    }

    LibraryAgent agent = library.Get(member.Library!);
    await projectLibraries.RequireAgentAsync(member.ProjectId, agent);
    AgentGrants granted = await grants.ReadAsync(member.Id);
    await runtime.CreateAsync(new AgentStart(member, agent, AgentKeys.Create(), granted), token);
    await room.SetShouldRunAsync(member.Id, false);
  }

  public async Task DeleteAgentAsync(RoomMember member, CancellationToken token)
  {
    SemaphoreSlim pending = LifecycleFor(member.Id);
    await pending.WaitAsync(token);
    try
    {
      await StopAgentCoreAsync(member, token);
      await runtime.DeleteAsync(member, token);
      await room.RemoveAsync(member.ProjectId, member.Id);
    }
    finally
    {
      pending.Release();
    }
  }

  public async Task RequireUnprovisionedAsync(RoomMember member, CancellationToken token)
  {
    if (await runtime.InspectAsync(member, token) is not null)
    {
      throw new InvalidOperationException("This agent already has its computer in its library environment, which fixes its definition and grants. Place a new agent for a different definition; folder and port grants are not available yet.");
    }
  }

  public async Task StartAgentAsync(RoomMember member, CancellationToken token)
  {
    SemaphoreSlim pending = LifecycleFor(member.Id);
    await pending.WaitAsync(token);
    try
    {
      await StartAgentCoreAsync(member, token);
    }
    finally
    {
      pending.Release();
    }
  }

  private async Task StartAgentCoreAsync(RoomMember member, CancellationToken token)
  {
    member = await room.GetMemberAsync(member.ProjectId, member.Id);
    if (member.IsYou || member.IsRemoved || member.Library is null)
    {
      throw new InvalidOperationException("Only agents in the room can be started.");
    }

    lock (gate)
    {
      if (shuttingDown)
      {
        throw new InvalidOperationException("Holodeck is shutting down; agents cannot start.");
      }

      if (runs.TryGetValue(member.Id, out AgentRun? existing) && existing.Status == AgentStatus.Running)
      {
        return;
      }
    }

    LibraryAgent agent = library.Get(member.Library);
    await projectLibraries.RequireAgentAsync(member.ProjectId, agent);
    AgentGrants granted = await grants.ReadAsync(member.Id);
    AgentComputerState? computer = await runtime.InspectAsync(member, token);
    string key = computer is null ? AgentKeys.Create() : computer.Key;
    await runtime.CreateAsync(new AgentStart(member, agent, key, granted), token);
    if (computer is not null && computer.Running)
    {
      await room.ObserveComputerStartAsync(member.Id, computer.StartedAt);
    }
    else
    {
      await room.EndSessionAsync(member.Id);
    }

    await room.BeginSessionAsync(member.Id, AgentKeys.Hash(key));
    await room.SetShouldRunAsync(member.Id, true);
    AgentRun run = new() { Status = AgentStatus.Running };
    lock (gate)
    {
      runs[member.Id] = run;
    }

    try
    {
      run.Agent = await runtime.StartAsync(new AgentStart(member, agent, key, granted), line => AppendLine(member, line), token);
    }
    catch (OperationCanceledException) when (token.IsCancellationRequested)
    {
      await ClearRunAsync(member);
      SetStatus(member, run, AgentStatus.Stopped);
      throw;
    }
    catch (Exception exception) when (exception is InvalidOperationException || exception is HttpRequestException || exception is System.ComponentModel.Win32Exception || exception is IOException)
    {
      AppendLine(member, $"could not start: {exception.Message}");
      await ClearRunAsync(member);
      SetStatus(member, run, AgentStatus.Crashed);
      return;
    }

    _ = WatchAsync(member, run);
    try
    {
      // A reference may have been removed while the agent was starting.
      await projectLibraries.RequireAgentAsync(member.ProjectId, agent);
    }
    catch
    {
      await StopAgentCoreAsync(member, CancellationToken.None);
      throw;
    }

    signals.Pulse(member.ProjectId, member.Id);
  }

  public async Task StopAgentAsync(RoomMember member, CancellationToken token)
  {
    SemaphoreSlim pending = LifecycleFor(member.Id);
    await pending.WaitAsync(token);
    try
    {
      await StopAgentCoreAsync(member, token);
    }
    finally
    {
      pending.Release();
    }
  }

  private async Task StopAgentCoreAsync(RoomMember member, CancellationToken token)
  {
    await ClearRunAsync(member);
    AgentRun? run;
    lock (gate)
    {
      runs.TryGetValue(member.Id, out run);
      if (run is not null)
      {
        run.Stopping = true;
      }
    }

    if (run?.Agent is not null)
    {
      await run.Agent.StopAsync();
    }
    else
    {
      await runtime.StopAsync(member, token);
    }

    if (run is not null)
    {
      SetStatus(member, run, AgentStatus.Stopped);
    }
    else
    {
      signals.Pulse(member.ProjectId, member.Id);
    }
  }

  // Restarts a running agent so it picks up new grants; a stopped agent stays stopped.
  public async Task RestartIfRunningAsync(RoomMember member, CancellationToken token)
  {
    SemaphoreSlim pending = LifecycleFor(member.Id);
    await pending.WaitAsync(token);
    try
    {
      if (Status(member.Id) != AgentStatus.Running)
      {
        return;
      }

      await StopAgentCoreAsync(member, token);
      await StartAgentCoreAsync(member, token);
    }
    finally
    {
      pending.Release();
    }
  }

  // Grants or revokes sudo; a running agent restarts so its launch changes.
  public async Task SetSudoAsync(RoomMember member, bool enabled, CancellationToken token)
  {
    SudoChange change = await runtime.SetSudoAsync(member, enabled, token);
    if (change == SudoChange.RecreateEnvironment)
    {
      await WhileAgentsStoppedAsync(member.ProjectId, () => runtime.RecreateEnvironmentAsync(member, token), token);
    }
    else if (change == SudoChange.Applied)
    {
      await RestartIfRunningAsync(member, token);
    }

    signals.Pulse(member.ProjectId, member.Id);
  }

  // Replacing a library environment ends every process in it, so the project's running agents stop first and start again
  // afterwards.
  public async Task WhileAgentsStoppedAsync(string projectId, Func<Task> work, CancellationToken token)
  {
    List<RoomMember> running = new();
    foreach (RoomMember member in await room.ListAgentsAsync())
    {
      if (member.ProjectId == projectId && Status(member.Id) == AgentStatus.Running)
      {
        running.Add(member);
      }
    }

    foreach (RoomMember member in running)
    {
      await StopAgentAsync(member, token);
    }

    try
    {
      await work();
    }
    finally
    {
      foreach (RoomMember member in running)
      {
        await StartAgentAsync(member, token);
      }
    }
  }

  public async Task ResetEnvironmentAsync(RoomMember member, CancellationToken token)
  {
    SemaphoreSlim pending = LifecycleFor(member.Id);
    await pending.WaitAsync(token);
    try
    {
      if (Status(member.Id) == AgentStatus.Running)
      {
        throw new InvalidOperationException("Stop the agent before resetting its environment.");
      }

      await runtime.ResetHomeAsync(member, token);
      signals.Pulse(member.ProjectId, member.Id);
    }
    finally
    {
      pending.Release();
    }
  }

  private SemaphoreSlim LifecycleFor(string memberId)
  {
    lock (gate)
    {
      if (!lifecycle.TryGetValue(memberId, out SemaphoreSlim? pending))
      {
        pending = new SemaphoreSlim(1, 1);
        lifecycle.Add(memberId, pending);
      }

      return pending;
    }
  }

  public AgentStatus Status(string memberId)
  {
    lock (gate)
    {
      if (runs.TryGetValue(memberId, out AgentRun? run))
      {
        return run.Status;
      }

      return AgentStatus.Stopped;
    }
  }

  public string ReadLog(RoomMember member)
  {
    string file = paths.LogFile(member.ProjectId, member.Id);
    lock (logGate)
    {
      if (!File.Exists(file))
      {
        return "";
      }

      return File.ReadAllText(file);
    }
  }

  private async Task WatchAsync(RoomMember member, AgentRun run)
  {
    int code = await run.Agent!.Exited;
    SemaphoreSlim pending = LifecycleFor(member.Id);
    await pending.WaitAsync();
    try
    {
      lock (gate)
      {
        if (shuttingDown || run.Stopping || !runs.TryGetValue(member.Id, out AgentRun? current) || !ReferenceEquals(current, run))
        {
          return;
        }
      }

      AppendLine(member, $"exited with code {code}");
      await ClearRunAsync(member);
      SetStatus(member, run, AgentStatus.Crashed);
    }
    finally
    {
      pending.Release();
    }
  }

  private async Task ClearRunAsync(RoomMember member)
  {
    await room.SetShouldRunAsync(member.Id, false);
    await room.EndSessionAsync(member.Id);
  }

  private void SetStatus(RoomMember member, AgentRun run, AgentStatus status)
  {
    lock (gate)
    {
      run.Status = status;
      runs[member.Id] = run;
    }

    signals.Pulse(member.ProjectId, member.Id);
  }

  private void AppendLine(RoomMember member, string? line)
  {
    if (line is null)
    {
      return;
    }

    string file = paths.LogFile(member.ProjectId, member.Id);
    string entry = $"[{DateTimeOffset.Now:HH:mm:ss}] {line}\n";
    lock (logGate)
    {
      Directory.CreateDirectory(Path.GetDirectoryName(file)!);
      File.AppendAllText(file, entry, Encoding.UTF8);
      FileInfo info = new(file);
      if (info.Length > RoomLimits.MaxLogBytes)
      {
        byte[] bytes = File.ReadAllBytes(file);
        int keep = bytes.Length / 2;
        File.WriteAllBytes(file, bytes.AsSpan(bytes.Length - keep).ToArray());
      }
    }

    signals.Pulse(member.ProjectId, member.Id);
  }
}
