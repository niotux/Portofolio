using System.Text;
using Holodeck.Web.Computers;
using Holodeck.Web.Rooms;

namespace Holodeck.Web.Projects;

// Project library references own environments. Agent count and window state never own them.
public sealed class ProjectApplications(ProjectLibraryStore references, AgentLibrary libraries, LibraryEnvironments environments, ProjectExecutionStore executions, ILogger<ProjectApplications> logger) : IHostedService
{
  private readonly Lock gatesLock = new();
  private readonly Dictionary<string, SemaphoreSlim> gates = new(StringComparer.Ordinal);

  public async Task<ProjectActivationResult> ActivateAsync(string projectId, CancellationToken token)
  {
    SemaphoreSlim pending = GateFor(projectId);
    await pending.WaitAsync(token);
    try
    {
      await PackagesAsync(projectId);
      await executions.SetRunningAsync(projectId, true, token);
      return await ActivateCoreAsync(projectId, token);
    }
    finally
    {
      pending.Release();
    }
  }

  private async Task<ProjectActivationResult> ActivateCoreAsync(string projectId, CancellationToken token)
  {
    List<LibraryActivationFailure> failures = new();
    foreach (LibraryApplicationPackage package in await PackagesAsync(projectId))
    {
      if (package.Runtime is null)
      {
        continue;
      }

      try
      {
        await environments.EnsureAsync(projectId, package.Key, package.Directory, package.Runtime, token, package.Dependencies);
      }
      catch (Exception exception) when (exception is InvalidOperationException || exception is ArgumentException || exception is HttpRequestException || exception is IOException)
      {
        logger.LogError(exception, "Could not activate library {LibraryKey} in project {ProjectId}", package.Key, projectId);
        failures.Add(new LibraryActivationFailure(package.Id, package.Version, exception.Message));
      }
    }

    return new ProjectActivationResult(failures);
  }

  public async Task AddReferenceAsync(string projectId, string libraryId, string version, CancellationToken token)
  {
    SemaphoreSlim pending = GateFor(projectId);
    await pending.WaitAsync(token);
    try
    {
      await references.AddAsync(projectId, libraryId, version);
      if (await executions.IsRunningAsync(projectId, token))
      {
        await ActivateCoreAsync(projectId, token);
      }
    }
    finally
    {
      pending.Release();
    }
  }

  public async Task RemoveReferenceAsync(string projectId, string libraryId, string version, CancellationToken token)
  {
    SemaphoreSlim pending = GateFor(projectId);
    await pending.WaitAsync(token);
    try
    {
      await references.RemoveAsync(projectId, libraryId, version);
      await environments.StopAsync(projectId, libraryId + "/" + version, token);
      foreach (ProjectLibraryView reference in await references.ListAsync(projectId))
      {
        if (!reference.IsAvailable)
        {
          await environments.StopAsync(projectId, reference.Id + "/" + reference.Version, token);
        }
      }
    }
    finally
    {
      pending.Release();
    }
  }

  public async Task StopAsync(string projectId, CancellationToken token)
  {
    SemaphoreSlim pending = GateFor(projectId);
    await pending.WaitAsync(token);
    try
    {
      foreach (ProjectLibraryView reference in await references.ListAsync(projectId))
      {
        await environments.StopAsync(projectId, reference.Id + "/" + reference.Version, token);
      }

      await executions.SetRunningAsync(projectId, false, token);
    }
    finally
    {
      pending.Release();
    }
  }

  public async Task DeleteAsync(string projectId, Func<Task> removeProject, CancellationToken token)
  {
    SemaphoreSlim pending = GateFor(projectId);
    await pending.WaitAsync(token);
    try
    {
      await references.ListAsync(projectId);
      await environments.DeleteProjectAsync(projectId, token);
      await removeProject();
    }
    finally
    {
      pending.Release();
    }
  }

  private const int MaxServiceLogCharacters = 262144;

  // Every declared service of the project's libraries, with what its environment reports while running.
  public async Task<List<ProjectServiceView>> ServicesAsync(string projectId, CancellationToken token)
  {
    List<ProjectServiceView> services = new();
    List<ProjectLibraryView> libraries = await references.ListAsync(projectId);
    foreach (LibraryApplicationPackage package in await PackagesAsync(projectId))
    {
      if (package.Runtime is null || package.Runtime.Processes.Count == 0)
      {
        continue;
      }

      string name = package.Id;
      foreach (ProjectLibraryView library in libraries)
      {
        if (library.Id == package.Id && library.Version == package.Version)
        {
          name = library.Name;
          break;
        }
      }

      List<string> granted = await environments.SudoAsync(projectId, package.Key, token);
      List<LibraryEnvironmentProcessState> states = await environments.ProcessStatesAsync(projectId, package.Key, token);
      foreach (LibraryEnvironmentProcess process in package.Runtime.Processes)
      {
        LibraryEnvironmentProcessState? state = null;
        foreach (LibraryEnvironmentProcessState candidate in states)
        {
          if (candidate.Id == process.Id)
          {
            state = candidate;
            break;
          }
        }

        bool running = state is not null && state.Running;
        services.Add(new ProjectServiceView(package.Id, package.Version, name, process.Id, process.Port, running, state?.ExitCode, state?.StartedAt ?? "", state?.Uid, granted.Contains(process.Id)));
      }
    }

    return services;
  }

  public async Task SetServiceRunningAsync(string projectId, string id, string version, string serviceId, bool running, CancellationToken token)
  {
    SemaphoreSlim pending = GateFor(projectId);
    await pending.WaitAsync(token);
    try
    {
      LibraryApplicationPackage package = await PackageAsync(projectId, id, version);
      RequireService(package, serviceId);
      await environments.SetProcessRunningAsync(projectId, package.Key, serviceId, running, token);
    }
    finally
    {
      pending.Release();
    }
  }

  // The end of a service's private log.
  public async Task<string> ServiceLogAsync(string projectId, string id, string version, string serviceId, CancellationToken token)
  {
    LibraryApplicationPackage package = await PackageAsync(projectId, id, version);
    RequireService(package, serviceId);
    StringBuilder text = new();
    long offset = 0;
    while (true)
    {
      LibraryEnvironmentLog chunk = await environments.ReadProcessLogsAsync(projectId, package.Key, serviceId, offset, token);
      if (chunk.Text.Length == 0 || chunk.Offset <= offset)
      {
        break;
      }

      text.Append(chunk.Text);
      if (text.Length > MaxServiceLogCharacters)
      {
        text.Remove(0, text.Length - MaxServiceLogCharacters);
      }

      offset = chunk.Offset;
    }

    return text.ToString();
  }

  // Grants or revokes sudo for one declared service and restarts it so its launch changes. Its open sessions end.
  public async Task<SudoChange> SetServiceSudoAsync(string projectId, string id, string version, string serviceId, bool enabled, CancellationToken token)
  {
    SemaphoreSlim pending = GateFor(projectId);
    await pending.WaitAsync(token);
    try
    {
      LibraryApplicationPackage package = await PackageAsync(projectId, id, version);
      RequireService(package, serviceId);

      SudoChange change = await environments.SetSudoAsync(projectId, package.Key, serviceId, enabled, token);
      if (change == SudoChange.Applied)
      {
        await environments.SetProcessRunningAsync(projectId, package.Key, serviceId, false, token);
        await environments.SetProcessRunningAsync(projectId, package.Key, serviceId, true, token);
      }

      return change;
    }
    finally
    {
      pending.Release();
    }
  }

  public async Task RecreateAsync(string projectId, string libraryKey, CancellationToken token)
  {
    SemaphoreSlim pending = GateFor(projectId);
    await pending.WaitAsync(token);
    try
    {
      foreach (LibraryApplicationPackage package in await PackagesAsync(projectId))
      {
        if (package.Key == libraryKey && package.Runtime is not null)
        {
          await environments.RecreateAsync(projectId, package.Key, package.Directory, package.Runtime, token, package.Dependencies);
          return;
        }
      }

      throw new KeyNotFoundException("This project's shared library environment is unavailable.");
    }
    finally
    {
      pending.Release();
    }
  }

  private static void RequireService(LibraryApplicationPackage package, string serviceId)
  {
    if (package.Runtime is not null)
    {
      foreach (LibraryEnvironmentProcess process in package.Runtime.Processes)
      {
        if (process.Id == serviceId)
        {
          return;
        }
      }
    }

    throw new KeyNotFoundException("This library declares no such service.");
  }

  public async Task PrepareAgentAsync(string projectId, string libraryKey, Func<Task> prepare, CancellationToken token)
  {
    SemaphoreSlim pending = GateFor(projectId);
    await pending.WaitAsync(token);
    try
    {
      await RequireLibraryAsync(projectId, libraryKey);
      await executions.SetRunningAsync(projectId, true, token);
      await ActivateCoreAsync(projectId, token);
      await prepare();
    }
    finally
    {
      pending.Release();
    }
  }

  public async Task StartAgentAsync(string projectId, string libraryKey, string processId, CancellationToken token)
  {
    SemaphoreSlim pending = GateFor(projectId);
    await pending.WaitAsync(token);
    try
    {
      await RequireLibraryAsync(projectId, libraryKey);
      if (!await executions.IsRunningAsync(projectId, token))
      {
        throw new InvalidOperationException("Start the project before starting its agents.");
      }

      await environments.StartAgentAsync(projectId, libraryKey, processId, token);
    }
    finally
    {
      pending.Release();
    }
  }

  private async Task RequireLibraryAsync(string projectId, string libraryKey)
  {
    foreach (LibraryApplicationPackage package in await PackagesAsync(projectId))
    {
      if (package.Key == libraryKey && package.Runtime is not null)
      {
        return;
      }
    }

    throw new KeyNotFoundException("This project's shared library environment is unavailable.");
  }

  public async Task<LibraryServiceConnection> ServiceAsync(string projectId, string libraryId, string version, string appletId, string service, CancellationToken token)
  {
    SemaphoreSlim pending = GateFor(projectId);
    await pending.WaitAsync(token);
    try
    {
      LibraryApplicationPackage package = await PackageAsync(projectId, libraryId, version);
      LibraryApplet applet = Applet(package, appletId);
      if (applet.Services is null || !applet.Services.Contains(service))
      {
        throw new UnauthorizedAccessException("This applet cannot access that service.");
      }

      if (package.Runtime is null || !await executions.IsRunningAsync(projectId, token))
      {
        throw new InvalidOperationException("This project's library services are stopped. Open the project to start them.");
      }

      LibraryEnvironmentView environment = await environments.EnsureAsync(projectId, package.Key, package.Directory, package.Runtime, token, package.Dependencies);
      foreach (LibraryEnvironmentProcessView process in environment.Processes)
      {
        if (process.Id == service && process.Port is not null && process.Running)
        {
          return new LibraryServiceConnection(environment.Address, process.Port.Value, process.ServiceToken);
        }
      }

      throw new InvalidOperationException("The library service is not running or has no endpoint.");
    }
    finally
    {
      pending.Release();
    }
  }

  public async Task StartAsync(CancellationToken token)
  {
    foreach (string projectId in await executions.ListRunningAsync(token))
    {
      try
      {
        await ActivateAsync(projectId, token);
      }
      catch (Exception exception) when (exception is InvalidOperationException || exception is HttpRequestException || exception is ArgumentException || exception is IOException)
      {
        logger.LogError(exception, "Could not reconnect project {ProjectId} library services", projectId);
      }
    }
  }

  Task IHostedService.StopAsync(CancellationToken token)
  {
    // Holodeck shutdown disconnects; it does not stop project execution or application sessions.
    return Task.CompletedTask;
  }

  public async Task<List<LibraryAppletView>> AppletsAsync(string projectId)
  {
    List<LibraryAppletView> applets = new();
    foreach (LibraryApplicationPackage package in await PackagesAsync(projectId))
    {
      foreach (LibraryApplet applet in package.Applets)
      {
        applets.Add(new LibraryAppletView(applet.Id, applet.Name, applet.Description ?? "", applet.Icon ?? LibraryApplet.DefaultIcon, package.Id, package.Version, applet.EntryPoint, applet.Width, applet.Height, applet.Services ?? new()));
      }
    }

    return applets;
  }

  public async Task<LibraryApplicationPackage> PackageAsync(string projectId, string id, string version)
  {
    LibraryFiles.RequireId(id, "Library Id");
    LibraryFiles.RequireId(version, "Library Version");
    foreach (LibraryApplicationPackage package in await PackagesAsync(projectId))
    {
      if (package.Id == id && package.Version == version)
      {
        return package;
      }
    }

    throw new KeyNotFoundException("That library is not available in this project.");
  }

  public static LibraryApplet Applet(LibraryApplicationPackage package, string id)
  {
    foreach (LibraryApplet applet in package.Applets)
    {
      if (applet.Id == id)
      {
        return applet;
      }
    }

    throw new KeyNotFoundException("That applet is not available in this library.");
  }

  private SemaphoreSlim GateFor(string projectId)
  {
    lock (gatesLock)
    {
      if (!gates.TryGetValue(projectId, out SemaphoreSlim? pending))
      {
        pending = new SemaphoreSlim(1, 1);
        gates.Add(projectId, pending);
      }

      return pending;
    }
  }

  private async Task<List<LibraryApplicationPackage>> PackagesAsync(string projectId)
  {
    List<ProjectLibraryView> attached = await references.ListAsync(projectId);
    List<LibraryApplicationPackage> packages = new();
    foreach (LibraryApplicationPackage package in libraries.ReadCatalog().Applications)
    {
      foreach (ProjectLibraryView reference in attached)
      {
        if (reference.IsAvailable && reference.Id == package.Id && reference.Version == package.Version)
        {
          packages.Add(package);
          break;
        }
      }
    }

    return packages;
  }
}
