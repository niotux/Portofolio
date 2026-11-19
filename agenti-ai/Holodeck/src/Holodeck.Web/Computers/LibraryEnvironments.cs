using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Holodeck.Web.Rooms;
using Microsoft.Extensions.Options;

namespace Holodeck.Web.Computers;

// A project/library owns its container; declared workload processes own separate non-root identities inside it, and
// reach root only through sudo when the user granted it to them.
public sealed partial class LibraryEnvironments(DockerClient docker, ComputerHost host, RoomPaths paths, IOptions<ComputerOptions> options)
{
  public const int ControlPort = 8079;
  public const string EnvironmentLabel = "holodeck.environment";
  private const string ContainerPrefix = "holodeck-library-";
  private const int EnvironmentIdBytes = 6;
  private const int FirstUid = 10000;
  private const long NanoCpusPerCpu = 1_000_000_000;
  private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
  private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(45);
  private readonly Lock gatesLock = new();
  private readonly Dictionary<string, SemaphoreSlim> gates = new(StringComparer.Ordinal);
  private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(8) };

  public async Task<LibraryEnvironmentView> EnsureAsync(string projectId, string libraryKey, string libraryDirectory, LibraryEnvironmentDefinition definition, CancellationToken token, List<LibraryMount>? dependencies = null)
  {
    SemaphoreSlim pending = GateFor(projectId, libraryKey);
    await pending.WaitAsync(token);
    try
    {
      RequireTrustedImage(definition);
      string name = ContainerName(projectId, libraryKey);
      LibraryEnvironmentConfig config = await ReadOrCreateConfigAsync(projectId, libraryKey, libraryDirectory, definition, dependencies, token);
      await docker.EnsureOpenNetworkAsync(options.Value.OpenNetwork, token);
      await docker.EnsureInternalNetworkAsync(options.Value.AgentNetwork, new(), token);
      using JsonDocument? existing = await docker.TryInspectContainerAsync(name, token);
      // A stopped container from before sudo grants is replaced; its folders and state are bind mounts.
      bool replace = existing is not null && !IsRunning(existing.RootElement) && PredatesSudo(existing.RootElement);
      if (replace)
      {
        await docker.RemoveContainerAsync(name, token);
      }

      string id;
      if (existing is null || replace)
      {
        await docker.EnsureImageAsync(definition.Image, token);
        LibraryEnvironmentCreateRequest request = Build(projectId, libraryKey, libraryDirectory, definition, dependencies);
        id = await docker.CreateLibraryEnvironmentAsync(name, request, token);
      }
      else
      {
        id = existing.RootElement.GetProperty("Id").GetString()!;
      }

      await EnsureAgentNetworkAsync(id, token);
      await docker.StartContainerAsync(id, token);
      string address = await AddressAsync(id, token);
      LibraryEnvironmentState state = await WaitUntilReadyAsync(address, config, token);
      state = await SyncSudoAsync(projectId, libraryKey, address, config, state, token);
      return View(id, address, config, state);
    }
    finally
    {
      pending.Release();
    }
  }

  public async Task StopAsync(string projectId, string libraryKey, CancellationToken token)
  {
    SemaphoreSlim pending = GateFor(projectId, libraryKey);
    await pending.WaitAsync(token);
    try
    {
      if (!File.Exists(Path.Combine(EnvironmentDirectory(projectId, libraryKey), "control", "config.json")))
      {
        return;
      }

      await docker.StopContainerAsync(ContainerName(projectId, libraryKey), 10, token);
    }
    finally
    {
      pending.Release();
    }
  }

  public async Task DeleteAsync(string projectId, string libraryKey, CancellationToken token)
  {
    SemaphoreSlim pending = GateFor(projectId, libraryKey);
    await pending.WaitAsync(token);
    try
    {
      // Removing a container never removes its persistent user directories or settings.
      if (!File.Exists(Path.Combine(EnvironmentDirectory(projectId, libraryKey), "control", "config.json")))
      {
        return;
      }

      await docker.RemoveContainerAsync(ContainerName(projectId, libraryKey), token);
    }
    finally
    {
      pending.Release();
    }
  }

  public async Task DeleteProjectAsync(string projectId, CancellationToken token)
  {
    string directory = Path.Combine(paths.Root, "rooms", projectId, "environments");
    if (!Directory.Exists(directory))
    {
      return;
    }

    foreach (string environment in Directory.EnumerateDirectories(directory))
    {
      string identity = Path.GetFileName(environment);
      SemaphoreSlim pending = GateForId(identity);
      await pending.WaitAsync(token);
      try
      {
        if (File.Exists(Path.Combine(environment, "control", "config.json")))
        {
          await docker.RemoveContainerAsync(ContainerPrefix + identity, token);
        }
      }
      finally
      {
        pending.Release();
      }
    }
  }

  public async Task SetProcessRunningAsync(string projectId, string libraryKey, string processId, bool running, CancellationToken token)
  {
    SemaphoreSlim pending = GateFor(projectId, libraryKey);
    await pending.WaitAsync(token);
    try
    {
      LibraryEnvironmentConfig config = await ReadConfigAsync(projectId, libraryKey, token);
      bool declared = false;
      foreach (LibraryEnvironmentProcessConfig process in config.Processes)
      {
        if (process.Id == processId)
        {
          declared = true;
          break;
        }
      }

      if (!declared)
      {
        throw new ArgumentException("That process is not declared by the library.");
      }

      await RequireRunningAsync(projectId, libraryKey, token);
      string address = await AddressAsync(ContainerName(projectId, libraryKey), token);
      await ControlAsync(address, config, processId, running ? "start" : "stop", token);
    }
    finally
    {
      pending.Release();
    }
  }

  // The supervisor's view of every process, or none while the environment is stopped.
  public async Task<List<LibraryEnvironmentProcessState>> ProcessStatesAsync(string projectId, string libraryKey, CancellationToken token)
  {
    using JsonDocument? inspected = await docker.TryInspectContainerAsync(ContainerName(projectId, libraryKey), token);
    if (inspected is null || !IsRunning(inspected.RootElement))
    {
      return new List<LibraryEnvironmentProcessState>();
    }

    LibraryEnvironmentConfig config = await ReadConfigAsync(projectId, libraryKey, token);
    string address = await AddressAsync(ContainerName(projectId, libraryKey), token);
    LibraryEnvironmentState state = await ReadStateAsync(address, config, token);
    return state.Processes;
  }

  private async Task RequireRunningAsync(string projectId, string libraryKey, CancellationToken token)
  {
    using JsonDocument? inspected = await docker.TryInspectContainerAsync(ContainerName(projectId, libraryKey), token);
    if (inspected is null || !IsRunning(inspected.RootElement))
    {
      throw new InvalidOperationException("This library's environment is stopped. Open the project to start it.");
    }
  }

  public async Task<LibraryEnvironmentLog> ReadProcessLogsAsync(string projectId, string libraryKey, string processId, long offset, CancellationToken token)
  {
    await RequireRunningAsync(projectId, libraryKey, token);
    LibraryEnvironmentConfig config = await ReadConfigAsync(projectId, libraryKey, token);
    string address = await AddressAsync(ContainerName(projectId, libraryKey), token);
    using HttpRequestMessage request = ControlRequest(HttpMethod.Get, address, $"/processes/{Uri.EscapeDataString(processId)}/logs?offset={offset}", config.ControlToken);
    using HttpResponseMessage response = await http.SendAsync(request, token);
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadFromJsonAsync<LibraryEnvironmentLog>(Json, token) ?? new LibraryEnvironmentLog("", offset);
  }

  private SemaphoreSlim GateFor(string projectId, string libraryKey) => GateForId(EnvironmentId(projectId, libraryKey));

  private SemaphoreSlim GateForId(string identity)
  {
    lock (gatesLock)
    {
      if (!gates.TryGetValue(identity, out SemaphoreSlim? pending))
      {
        pending = new SemaphoreSlim(1, 1);
        gates.Add(identity, pending);
      }

      return pending;
    }
  }

  private void RequireTrustedImage(LibraryEnvironmentDefinition definition)
  {
    if (string.IsNullOrWhiteSpace(options.Value.LibraryImage) || definition.Image != options.Value.LibraryImage)
    {
      throw new ArgumentException("The library runtime must use the trusted image configured by Holodeck.");
    }
  }

  private LibraryEnvironmentCreateRequest Build(string projectId, string libraryKey, string libraryDirectory, LibraryEnvironmentDefinition definition, List<LibraryMount>? dependencies)
  {
    string directory = EnvironmentDirectory(projectId, libraryKey);
    List<string> binds = new()
    {
      $"{HostMounts.HostPathOf(libraryDirectory, host.Paths, paths)}:/library:ro",
      $"{HostMounts.HostPathOf(Path.Combine(directory, "data", "home"), host.Paths, paths)}:/home",
      $"{HostMounts.HostPathOf(Path.Combine(directory, "data", "srv"), host.Paths, paths)}:/srv",
      $"{HostMounts.HostPathOf(Path.Combine(directory, "data", "state"), host.Paths, paths)}:/var/lib/holodeck",
      $"{HostMounts.HostPathOf(Path.Combine(directory, "control", "config.json"), host.Paths, paths)}:/etc/holodeck/config.json:ro"
    };
    if (dependencies is not null)
    {
      foreach (LibraryMount dependency in dependencies)
      {
        binds.Add($"{HostMounts.HostPathOf(dependency.Directory, host.Paths, paths)}:/dependencies/{dependency.Id}:ro");
      }
    }

    ComputerOptions computer = options.Value;
    // No container-wide no-new-privileges: the launcher sets it on every process except those the user granted sudo.
    LibraryEnvironmentHostConfig config = new(binds, computer.MemoryBytes, (long)(computer.Cpus * NanoCpusPerCpu), computer.Pids, ["ALL"], ["CHOWN", "DAC_OVERRIDE", "FOWNER", "FSETID", "SETUID", "SETGID", "SETPCAP", "KILL"], new(), true, computer.OpenNetwork, new(), new() { ["/tmp"] = "rw,nosuid,nodev,noexec,mode=1777" });
    Dictionary<string, string> labels = new()
    {
      [EnvironmentLabel] = libraryKey,
      ["holodeck.project"] = projectId
    };
    return new LibraryEnvironmentCreateRequest(definition.Image, new(), "0:0", labels, config);
  }

  private async Task EnsureAgentNetworkAsync(string id, CancellationToken token)
  {
    using JsonDocument inspected = await docker.InspectContainerAsync(id, token);
    JsonElement networks = inspected.RootElement.GetProperty("NetworkSettings").GetProperty("Networks");
    if (!networks.TryGetProperty(options.Value.AgentNetwork, out _))
    {
      await docker.ConnectNetworkAsync(options.Value.AgentNetwork, id, token);
    }
  }

  private async Task<string> AddressAsync(string id, CancellationToken token)
  {
    using JsonDocument inspected = await docker.InspectContainerAsync(id, token);
    JsonElement network = inspected.RootElement.GetProperty("NetworkSettings").GetProperty("Networks").GetProperty(options.Value.AgentNetwork);
    string? address = network.GetProperty("IPAddress").GetString();
    if (string.IsNullOrEmpty(address))
    {
      throw new InvalidOperationException("The library environment has no agent-network address.");
    }

    return address;
  }

  private async Task<LibraryEnvironmentConfig> ReadOrCreateConfigAsync(string projectId, string libraryKey, string libraryDirectory, LibraryEnvironmentDefinition definition, List<LibraryMount>? dependencies, CancellationToken token)
  {
    string fingerprint = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(libraryDirectory + "\n" + JsonSerializer.Serialize(definition, Json) + "\n" + JsonSerializer.Serialize(dependencies ?? new List<LibraryMount>(), Json))));
    string directory = EnvironmentDirectory(projectId, libraryKey);
    string control = Path.Combine(directory, "control");
    string file = Path.Combine(control, "config.json");
    if (File.Exists(file))
    {
      LibraryEnvironmentConfig saved = await ReadConfigAsync(projectId, libraryKey, token);
      if (saved.Fingerprint != fingerprint)
      {
        throw new InvalidOperationException("This library environment was created with a different runtime definition. It must be migrated explicitly.");
      }

      return saved;
    }

    Directory.CreateDirectory(control);
    // Docker would create missing bind sources as root; the supervisor sets their owners and modes.
    Directory.CreateDirectory(Path.Combine(directory, "data", "home"));
    Directory.CreateDirectory(Path.Combine(directory, "data", "srv"));
    Directory.CreateDirectory(Path.Combine(directory, "data", "state"));
    if (!OperatingSystem.IsWindows())
    {
      File.SetUnixFileMode(control, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    List<LibraryEnvironmentProcessConfig> processes = new();
    foreach (LibraryEnvironmentProcess process in definition.Processes)
    {
      processes.Add(new LibraryEnvironmentProcessConfig(process.Id, process.Command, process.Port, process.HealthPath, NewToken()));
    }

    LibraryEnvironmentConfig config = new(fingerprint, NewToken(), processes);
    string temporary = file + ".tmp";
    await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(config, Json), token);
    if (!OperatingSystem.IsWindows())
    {
      File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    File.Move(temporary, file);
    return config;
  }

  private async Task<LibraryEnvironmentConfig> ReadConfigAsync(string projectId, string libraryKey, CancellationToken token)
  {
    string file = Path.Combine(EnvironmentDirectory(projectId, libraryKey), "control", "config.json");
    string contents = await File.ReadAllTextAsync(file, token);
    return JsonSerializer.Deserialize<LibraryEnvironmentConfig>(contents, Json) ?? throw new InvalidOperationException("The library environment configuration is invalid.");
  }

  private async Task<LibraryEnvironmentState> WaitUntilReadyAsync(string address, LibraryEnvironmentConfig config, CancellationToken token)
  {
    DateTimeOffset deadline = DateTimeOffset.UtcNow + ReadyTimeout;
    string detail = "The library supervisor did not answer.";
    while (DateTimeOffset.UtcNow < deadline)
    {
      token.ThrowIfCancellationRequested();
      try
      {
        LibraryEnvironmentState state = await ReadStateAsync(address, config, token);
        if (await ServicesReadyAsync(address, config, state, token))
        {
          return state;
        }

        detail = "A declared library process has not become ready.";
      }
      catch (Exception exception) when (exception is HttpRequestException || exception is TaskCanceledException && !token.IsCancellationRequested)
      {
        detail = "The library supervisor or a declared service did not answer.";
      }

      await Task.Delay(TimeSpan.FromMilliseconds(200), token);
    }

    throw new InvalidOperationException(detail);
  }

  private async Task<LibraryEnvironmentState> ReadStateAsync(string address, LibraryEnvironmentConfig config, CancellationToken token)
  {
    using HttpRequestMessage request = ControlRequest(HttpMethod.Get, address, "/processes", config.ControlToken);
    using HttpResponseMessage response = await http.SendAsync(request, token);
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadFromJsonAsync<LibraryEnvironmentState>(Json, token) ?? throw new InvalidOperationException("The library supervisor returned no process state.");
  }

  private async Task<bool> ServicesReadyAsync(string address, LibraryEnvironmentConfig config, LibraryEnvironmentState state, CancellationToken token)
  {
    foreach (LibraryEnvironmentProcessConfig process in config.Processes)
    {
      bool running = false;
      foreach (LibraryEnvironmentProcessState observed in state.Processes)
      {
        if (observed.Id == process.Id)
        {
          running = observed.Running;
          break;
        }
      }

      if (!running)
      {
        throw new InvalidOperationException($"Library service '{process.Id}' exited. Inspect its private process log before restarting it.");
      }

      if (process.Port is null || string.IsNullOrEmpty(process.HealthPath))
      {
        continue;
      }

      using HttpRequestMessage request = new(HttpMethod.Get, $"http://{address}:{process.Port}{process.HealthPath}");
      request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", process.ServiceToken);
      using HttpResponseMessage response = await http.SendAsync(request, token);
      if (!response.IsSuccessStatusCode)
      {
        return false;
      }
    }

    return true;
  }

  private static LibraryEnvironmentView View(string id, string address, LibraryEnvironmentConfig config, LibraryEnvironmentState state)
  {
    Dictionary<string, LibraryEnvironmentProcessState> states = new(StringComparer.Ordinal);
    foreach (LibraryEnvironmentProcessState process in state.Processes)
    {
      states.Add(process.Id, process);
    }

    List<LibraryEnvironmentProcessView> processes = new();
    for (int index = 0; index < config.Processes.Count; index++)
    {
      LibraryEnvironmentProcessConfig process = config.Processes[index];
      states.TryGetValue(process.Id, out LibraryEnvironmentProcessState? observed);
      bool running = observed is not null && observed.Running;
      processes.Add(new LibraryEnvironmentProcessView(process.Id, FirstUid + index, process.Port, process.ServiceToken, running, observed?.ExitCode, observed?.StartedAt ?? ""));
    }

    return new LibraryEnvironmentView(id, address, processes);
  }

  private string EnvironmentDirectory(string projectId, string libraryKey)
  {
    return Path.Combine(paths.Root, "rooms", projectId, "environments", EnvironmentId(projectId, libraryKey));
  }

  // A short stable name for one project's environment of one exact library version.
  private static string EnvironmentId(string projectId, string libraryKey)
  {
    byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{projectId}\n{libraryKey}"));
    return Convert.ToHexStringLower(hash.AsSpan(0, EnvironmentIdBytes));
  }

  private static string ContainerName(string projectId, string libraryKey)
  {
    return ContainerPrefix + EnvironmentId(projectId, libraryKey);
  }

  private static string NewToken() { return Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)); }

  private static HttpRequestMessage ControlRequest(HttpMethod method, string address, string path, string token)
  {
    HttpRequestMessage request = new(method, $"http://{address}:{ControlPort}{path}");
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    return request;
  }
}
