using System.Net;
using System.Text.Json;
using Holodeck.Web.Projects;
using Holodeck.Web.Rooms;
using Microsoft.Extensions.Options;

namespace Holodeck.Web.Computers;

// Every agent runs as its own user inside its library's project environment. Identities, credentials and files remain
// private even though the container belongs to the library.
public sealed class LibraryAgentRuntime(LibraryEnvironments environments, DockerClient docker, ComputerHost host, RoomPaths paths, ProjectApplications applications, IOptions<ComputerOptions> computer, IOptions<SupervisorOptions> supervisor, IHostApplicationLifetime lifetime, ILogger<LibraryAgentRuntime> logger) : IAgentRuntime
{
  private const string BindingName = "library-environment.json";
  private const string ProjectLabel = "com.docker.compose.project.working_dir";
  private const int LostExitCode = -1;
  private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

  public bool IsRegistered(RoomMember member) => File.Exists(BindingFile(member));

  // Learns where Holodeck's data and libraries are on the host and which subnets belong to agents.
  public async Task PrepareAsync(CancellationToken token)
  {
    using JsonDocument self = await docker.InspectContainerAsync(Environment.MachineName, token);
    string? data = null;
    string? libraries = null;
    foreach (JsonElement mount in self.RootElement.GetProperty("Mounts").EnumerateArray())
    {
      string destination = mount.GetProperty("Destination").GetString()!;
      string source = mount.GetProperty("Source").GetString()!;
      if (destination == paths.Root)
      {
        data = source;
      }
      else if (destination == paths.Libraries)
      {
        libraries = source;
      }
    }

    if (data is null || libraries is null)
    {
      throw new InvalidOperationException($"Holodeck's container must mount its data at {paths.Root} and its libraries at {paths.Libraries}.");
    }

    string project = Path.GetDirectoryName(data)!;
    JsonElement labels = self.RootElement.GetProperty("Config").GetProperty("Labels");
    if (labels.ValueKind == JsonValueKind.Object && labels.TryGetProperty(ProjectLabel, out JsonElement working))
    {
      project = working.GetString()!;
    }

    List<IPNetwork> subnets = new();
    foreach (string subnet in await docker.NetworkSubnetsAsync(computer.Value.AgentNetwork, token))
    {
      subnets.Add(IPNetwork.Parse(subnet));
    }

    host.Learn(new HostPaths(project, data, libraries), subnets);
  }

  public async Task<AgentComputerState?> InspectAsync(RoomMember member, CancellationToken token)
  {
    if (!IsRegistered(member))
    {
      return null;
    }

    string key = await LibraryKeyAsync(member, token);
    LibraryEnvironmentAgentState? state = await environments.InspectAgentAsync(member.ProjectId, key, ProcessId(member), token);
    return state is null ? null : new AgentComputerState(state.Key, state.Running, state.StartedAt);
  }

  public async Task CreateAsync(AgentStart start, CancellationToken token)
  {
    RequireNoGrants(start.Grants);
    LibraryEnvironmentDefinition definition = start.Agent.Runtime ?? throw new InvalidOperationException("This agent's library declares no Runtime.");
    RequireFreshOrRegistered(start.Member);
    if (IsRegistered(start.Member) && await LibraryKeyAsync(start.Member, token) != start.Agent.LibraryKey)
    {
      throw new InvalidOperationException("This agent belongs to a different library environment.");
    }
    await applications.PrepareAgentAsync(start.Member.ProjectId, start.Agent.LibraryKey, async () =>
    {
      await environments.EnsureAsync(start.Member.ProjectId, start.Agent.LibraryKey, start.Agent.LibraryDirectory, definition, token, start.Agent.Dependencies);
      string processId = ProcessId(start.Member);
      LibraryEnvironmentAgentRegistration registration = await RegistrationAsync(start, processId, token);
      await environments.RegisterAgentAsync(start.Member.ProjectId, start.Agent.LibraryKey, registration, token);
      LibraryEnvironmentAgentPaths storage = environments.AgentPaths(start.Member.ProjectId, start.Agent.LibraryKey, processId);
      LinkDirectory(paths.Home(start.Member.ProjectId, start.Member.Id), storage.Home);
      LinkDirectory(paths.Table(start.Member.ProjectId, start.Member.Id), storage.Work);
      await File.WriteAllTextAsync(BindingFile(start.Member), JsonSerializer.Serialize(new AgentRuntimeBinding(start.Agent.LibraryKey)), token);
    }, token);
  }

  public async Task<RunningAgent> StartAsync(AgentStart start, Action<string> output, CancellationToken token)
  {
    string libraryKey = await LibraryKeyAsync(start.Member, token);
    await applications.StartAgentAsync(start.Member.ProjectId, libraryKey, ProcessId(start.Member), token);
    TaskCompletionSource stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    Task<int> exited = FollowAsync(start.Member, libraryKey, output, stopped.Task);
    return new RunningAgent(exited, async () =>
    {
      await StopAsync(start.Member, CancellationToken.None);
      stopped.TrySetResult();
    });
  }

  public async Task StopAsync(RoomMember member, CancellationToken token)
  {
    if (!IsRegistered(member))
    {
      return;
    }

    await environments.StopAgentAsync(member.ProjectId, await LibraryKeyAsync(member, token), ProcessId(member), token);
  }

  public async Task DeleteAsync(RoomMember member, CancellationToken token)
  {
    if (!IsRegistered(member))
    {
      return;
    }

    await environments.DeleteAgentAsync(member.ProjectId, await LibraryKeyAsync(member, token), ProcessId(member), token);
    // Preserve the binding and files for inspection; removed members can never start again.
  }

  public async Task<bool> SudoAsync(RoomMember member, CancellationToken token)
  {
    if (!IsRegistered(member))
    {
      return false;
    }

    List<string> granted = await environments.SudoAsync(member.ProjectId, await LibraryKeyAsync(member, token), token);
    return granted.Contains(ProcessId(member));
  }

  public async Task<SudoChange> SetSudoAsync(RoomMember member, bool enabled, CancellationToken token)
  {
    if (!IsRegistered(member))
    {
      throw new InvalidOperationException("This agent has no computer yet.");
    }

    return await environments.SetSudoAsync(member.ProjectId, await LibraryKeyAsync(member, token), ProcessId(member), enabled, token);
  }

  public async Task RecreateEnvironmentAsync(RoomMember member, CancellationToken token)
  {
    await applications.RecreateAsync(member.ProjectId, await LibraryKeyAsync(member, token), token);
  }

  public async Task RefreshSettingsAsync(RoomMember member, string content, CancellationToken token)
  {
    if (IsRegistered(member))
    {
      await environments.UpdateAgentSettingsAsync(member.ProjectId, await LibraryKeyAsync(member, token), ProcessId(member), content, token);
    }
  }

  public async Task ResetHomeAsync(RoomMember member, CancellationToken token)
  {
    if (!IsRegistered(member))
    {
      return;
    }

    await environments.ResetAgentHomeAsync(member.ProjectId, await LibraryKeyAsync(member, token), ProcessId(member), token);
  }

  private async Task<LibraryEnvironmentAgentRegistration> RegistrationAsync(AgentStart start, string processId, CancellationToken token)
  {
    string relativeAgent = Path.GetRelativePath(start.Agent.LibraryDirectory, start.Agent.Directory).Replace('\\', '/');
    string agentPath = "/library/" + relativeAgent;
    Dictionary<string, string> dependencies = new(StringComparer.Ordinal);
    if (start.Agent.Dependencies is not null)
    {
      foreach (LibraryMount dependency in start.Agent.Dependencies)
      {
        dependencies.Add(dependency.Id, "/dependencies/" + dependency.Id);
      }
    }

    List<string> command = new();
    foreach (string argument in start.Agent.Start)
    {
      command.Add(Placeholders.Expand(argument, agentPath, "/library", dependencies));
    }

    if (!Path.IsPathRooted(command[0]) && command[0].Contains('/'))
    {
      command[0] = agentPath + "/" + command[0];
    }

    // The supervisor adds HOME, HOLODECK_TABLE and HOLODECK_SETTINGS, which belong to its layout.
    Dictionary<string, string> environment = new(StringComparer.Ordinal)
    {
      ["HOLODECK_URL"] = supervisor.Value.HolodeckUrl,
      ["HOLODECK_KEY"] = start.Key,
      ["HOLODECK_PROGRAM"] = agentPath,
      ["HOLODECK_LIBRARY"] = "/library"
    };
    string settings = await File.ReadAllTextAsync(paths.SettingsFile(start.Member.ProjectId, start.Member.Id), token);
    return new LibraryEnvironmentAgentRegistration(processId, command, environment, PlatformGroup(), settings);
  }

  private int PlatformGroup()
  {
    string[] identity = computer.Value.User.Split(':');
    string group = identity.Length > 1 ? identity[1] : identity[0];
    if (!int.TryParse(group, out int value) || value <= 0)
    {
      throw new InvalidOperationException("The shared agent runtime requires a numeric non-root platform group.");
    }

    return value;
  }

  private async Task<int> FollowAsync(RoomMember member, string libraryKey, Action<string> output, Task stopped)
  {
    CancellationToken token = lifetime.ApplicationStopping;
    string offsetFile = Path.Combine(paths.AgentDirectory(member.ProjectId, member.Id), "library-log-offset");
    long offset = 0;
    if (File.Exists(offsetFile))
    {
      long.TryParse(await File.ReadAllTextAsync(offsetFile), out offset);
    }

    while (!token.IsCancellationRequested && !stopped.IsCompleted)
    {
      try
      {
        // Inspect before reading, so the output written just before an exit is still read once more.
        LibraryEnvironmentAgentState? state = await environments.InspectAgentAsync(member.ProjectId, libraryKey, ProcessId(member), token);
        if (state is not null)
        {
          offset = await ReadLogsAsync(member, libraryKey, offset, offsetFile, output, state.Running, token);
        }

        if (state is null || !state.Running)
        {
          return state?.ExitCode ?? LostExitCode;
        }
      }
      catch (Exception exception) when (exception is HttpRequestException || exception is IOException || exception is InvalidOperationException)
      {
        logger.LogWarning(exception, "Could not inspect shared agent {AgentId}", member.Id);
      }

      try
      {
        await Task.WhenAny(Task.Delay(PollInterval, token), stopped);
      }
      catch (OperationCanceledException) when (token.IsCancellationRequested)
      {
        break;
      }
    }

    if (stopped.IsCompleted)
    {
      return 0;
    }

    // Platform shutdown only disconnects observation; it never terminates the workload.
    try
    {
      await Task.Delay(Timeout.InfiniteTimeSpan, lifetime.ApplicationStopped);
    }
    catch (OperationCanceledException)
    {
    }

    return LostExitCode;
  }

  // A stopped environment cannot be read; its exit is still reported.
  private async Task<long> ReadLogsAsync(RoomMember member, string libraryKey, long offset, string offsetFile, Action<string> output, bool running, CancellationToken token)
  {
    LibraryEnvironmentLog logs;
    try
    {
      logs = await environments.ReadProcessLogsAsync(member.ProjectId, libraryKey, ProcessId(member), offset, token);
    }
    catch (Exception exception) when (!running && (exception is HttpRequestException || exception is IOException || exception is InvalidOperationException))
    {
      return offset;
    }

    if (logs.Text.Length > 0)
    {
      output(logs.Text.TrimEnd('\r', '\n'));
    }

    await File.WriteAllTextAsync(offsetFile, logs.Offset.ToString(), token);
    return logs.Offset;
  }

  private void RequireFreshOrRegistered(RoomMember member)
  {
    if (IsRegistered(member))
    {
      return;
    }

    RequireEmptyDirectory(paths.Table(member.ProjectId, member.Id));
    RequireEmptyDirectory(paths.Home(member.ProjectId, member.Id));
  }

  private static void RequireEmptyDirectory(string directory)
  {
    if (!Directory.Exists(directory))
    {
      return;
    }

    if (new DirectoryInfo(directory).LinkTarget is not null)
    {
      throw new InvalidOperationException("This agent's storage is linked elsewhere.");
    }

    foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
    {
      throw new InvalidOperationException("This agent already has files outside its library environment.");
    }
  }

  private static void LinkDirectory(string destination, string target)
  {
    DirectoryInfo directory = new(destination);
    if (directory.LinkTarget is not null)
    {
      if (directory.LinkTarget != target)
      {
        throw new InvalidOperationException("This agent's existing storage binding does not match its environment.");
      }

      return;
    }

    RequireEmptyDirectory(destination);
    if (Directory.Exists(destination))
    {
      Directory.Delete(destination);
    }

    Directory.CreateSymbolicLink(destination, target);
  }

  private static void RequireNoGrants(AgentGrants grants)
  {
    if (grants.Folders.Count > 0 || grants.Ports.Count > 0)
    {
      throw new InvalidOperationException("Agents do not yet support external folder or published-port grants.");
    }
  }

  private string BindingFile(RoomMember member) => Path.Combine(paths.AgentDirectory(member.ProjectId, member.Id), BindingName);

  private static string ProcessId(RoomMember member) => "agent-" + member.Id;

  private async Task<string> LibraryKeyAsync(RoomMember member, CancellationToken token)
  {
    string contents = await File.ReadAllTextAsync(BindingFile(member), token);
    AgentRuntimeBinding binding = JsonSerializer.Deserialize<AgentRuntimeBinding>(contents) ?? throw new InvalidOperationException("The shared agent environment binding is invalid.");
    return binding.LibraryKey;
  }
}
