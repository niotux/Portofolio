using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Holodeck.Web.Computers;

public sealed partial class LibraryEnvironments
{
  public LibraryEnvironmentAgentPaths AgentPaths(string projectId, string libraryKey, string processId)
  {
    // The supervisor's /home/<agent> and /srv/<agent>: its home and its table.
    string data = Path.Combine(EnvironmentDirectory(projectId, libraryKey), "data");
    return new LibraryEnvironmentAgentPaths(Path.Combine(data, "home", processId), Path.Combine(data, "srv", processId));
  }

  public async Task RegisterAgentAsync(string projectId, string libraryKey, LibraryEnvironmentAgentRegistration registration, CancellationToken token)
  {
    SemaphoreSlim pending = GateFor(projectId, libraryKey);
    await pending.WaitAsync(token);
    try
    {
      RequireAgentId(registration.Id);
      LibraryEnvironmentConfig config = await ReadConfigAsync(projectId, libraryKey, token);
      string address = await AddressAsync(ContainerName(projectId, libraryKey), token);
      string registrationFile = AgentRegistrationFile(projectId, libraryKey, registration.Id);
      bool firstRegistration = !File.Exists(registrationFile);
      if (firstRegistration)
      {
        // Persist the key before the supervisor can accept it, so a lost response is safe to retry.
        await SaveAgentRegistrationAsync(projectId, libraryKey, registration, token);
      }

      using HttpRequestMessage request = ControlRequest(HttpMethod.Post, address, "/processes/register", config.ControlToken);
      request.Content = new StringContent(JsonSerializer.Serialize(registration, Json), Encoding.UTF8, "application/json");
      using HttpResponseMessage response = await http.SendAsync(request, token);
      if (!response.IsSuccessStatusCode)
      {
        if (firstRegistration && response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
          File.Delete(registrationFile);
        }

        throw new InvalidOperationException("The library supervisor refused the agent registration. An existing agent launch cannot be changed implicitly.");
      }

      await SaveAgentRegistrationAsync(projectId, libraryKey, registration, token);
    }
    finally
    {
      pending.Release();
    }
  }

  public async Task<LibraryEnvironmentAgentState?> InspectAgentAsync(string projectId, string libraryKey, string processId, CancellationToken token)
  {
    RequireAgentId(processId);
    string file = AgentRegistrationFile(projectId, libraryKey, processId);
    if (!File.Exists(file))
    {
      return null;
    }

    string contents = await File.ReadAllTextAsync(file, token);
    LibraryEnvironmentAgentRegistration registration = JsonSerializer.Deserialize<LibraryEnvironmentAgentRegistration>(contents, Json) ?? throw new InvalidOperationException("The saved agent registration is invalid.");
    if (!registration.Environment.TryGetValue("HOLODECK_KEY", out string? key))
    {
      throw new InvalidOperationException("The saved agent registration has no key.");
    }

    using JsonDocument? inspected = await docker.TryInspectContainerAsync(ContainerName(projectId, libraryKey), token);
    if (inspected is null || !inspected.RootElement.GetProperty("State").GetProperty("Running").GetBoolean())
    {
      return new LibraryEnvironmentAgentState(key, false, "", null);
    }

    LibraryEnvironmentConfig config = await ReadConfigAsync(projectId, libraryKey, token);
    string address = await AddressAsync(ContainerName(projectId, libraryKey), token);
    LibraryEnvironmentState state = await ReadStateAsync(address, config, token);
    foreach (LibraryEnvironmentProcessState process in state.Processes)
    {
      if (process.Id == processId)
      {
        return new LibraryEnvironmentAgentState(key, process.Running, process.StartedAt, process.ExitCode);
      }
    }

    return new LibraryEnvironmentAgentState(key, false, "", null);
  }

  public Task StartAgentAsync(string projectId, string libraryKey, string processId, CancellationToken token)
  {
    RequireAgentId(processId);
    return AgentOperationAsync(projectId, libraryKey, processId, "start", token);
  }

  public Task StopAgentAsync(string projectId, string libraryKey, string processId, CancellationToken token)
  {
    RequireAgentId(processId);
    return AgentOperationAsync(projectId, libraryKey, processId, "stop", token);
  }

  public async Task DeleteAgentAsync(string projectId, string libraryKey, string processId, CancellationToken token)
  {
    RequireAgentId(processId);
    SemaphoreSlim pending = GateFor(projectId, libraryKey);
    await pending.WaitAsync(token);
    try
    {
      await AgentOperationCoreAsync(projectId, libraryKey, processId, "delete", token);
      File.Delete(AgentRegistrationFile(projectId, libraryKey, processId));
    }
    finally
    {
      pending.Release();
    }
  }

  private async Task AgentOperationAsync(string projectId, string libraryKey, string processId, string action, CancellationToken token)
  {
    SemaphoreSlim pending = GateFor(projectId, libraryKey);
    await pending.WaitAsync(token);
    try
    {
      await AgentOperationCoreAsync(projectId, libraryKey, processId, action, token);
    }
    finally
    {
      pending.Release();
    }
  }

  private async Task AgentOperationCoreAsync(string projectId, string libraryKey, string processId, string action, CancellationToken token)
  {
    if (!File.Exists(AgentRegistrationFile(projectId, libraryKey, processId)))
    {
      return;
    }

    using JsonDocument? inspected = await docker.TryInspectContainerAsync(ContainerName(projectId, libraryKey), token);
    bool containerRunning = inspected is not null && inspected.RootElement.GetProperty("State").GetProperty("Running").GetBoolean();
    if (!containerRunning)
    {
      if (action == "stop" || action == "delete")
      {
        return;
      }

      throw new InvalidOperationException("Activate the project before controlling this agent environment.");
    }

    LibraryEnvironmentConfig config = await ReadConfigAsync(projectId, libraryKey, token);
    string address = await AddressAsync(ContainerName(projectId, libraryKey), token);
    using HttpRequestMessage request = ControlRequest(HttpMethod.Post, address, $"/processes/{Uri.EscapeDataString(processId)}/{action}", config.ControlToken);
    using HttpResponseMessage response = await http.SendAsync(request, token);
    if (!response.IsSuccessStatusCode)
    {
      throw new InvalidOperationException("The library supervisor could not control the agent process.");
    }
  }

  public async Task UpdateAgentSettingsAsync(string projectId, string libraryKey, string processId, string settingsJson, CancellationToken token)
  {
    RequireAgentId(processId);
    SemaphoreSlim pending = GateFor(projectId, libraryKey);
    await pending.WaitAsync(token);
    try
    {
      string file = AgentRegistrationFile(projectId, libraryKey, processId);
      if (!File.Exists(file))
      {
        return;
      }

      string contents = await File.ReadAllTextAsync(file, token);
      LibraryEnvironmentAgentRegistration registration = JsonSerializer.Deserialize<LibraryEnvironmentAgentRegistration>(contents, Json) ?? throw new InvalidOperationException("The saved agent registration is invalid.");
      registration = registration with { SettingsJson = settingsJson };
      using JsonDocument? inspected = await docker.TryInspectContainerAsync(ContainerName(projectId, libraryKey), token);
      if (inspected is not null && inspected.RootElement.GetProperty("State").GetProperty("Running").GetBoolean())
      {
        LibraryEnvironmentConfig config = await ReadConfigAsync(projectId, libraryKey, token);
        string address = await AddressAsync(ContainerName(projectId, libraryKey), token);
        using HttpRequestMessage request = ControlRequest(HttpMethod.Post, address, "/processes/register", config.ControlToken);
        request.Content = new StringContent(JsonSerializer.Serialize(registration, Json), Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await http.SendAsync(request, token);
        response.EnsureSuccessStatusCode();
      }

      await SaveAgentRegistrationAsync(projectId, libraryKey, registration, token);
    }
    finally
    {
      pending.Release();
    }
  }

  public Task ResetAgentHomeAsync(string projectId, string libraryKey, string processId, CancellationToken token)
  {
    RequireAgentId(processId);
    return AgentOperationAsync(projectId, libraryKey, processId, "reset-home", token);
  }

  private async Task SaveAgentRegistrationAsync(string projectId, string libraryKey, LibraryEnvironmentAgentRegistration registration, CancellationToken token)
  {
    string file = AgentRegistrationFile(projectId, libraryKey, registration.Id);
    string directory = Path.GetDirectoryName(file)!;
    Directory.CreateDirectory(directory);
    if (!OperatingSystem.IsWindows())
    {
      File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    string temporary = file + ".tmp";
    await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(registration, Json), token);
    if (!OperatingSystem.IsWindows())
    {
      File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    File.Move(temporary, file, true);
  }

  private string AgentRegistrationFile(string projectId, string libraryKey, string processId)
  {
    return Path.Combine(EnvironmentDirectory(projectId, libraryKey), "control", "agents", processId + ".json");
  }

  private static void RequireAgentId(string processId)
  {
    if (!processId.StartsWith("agent-", StringComparison.Ordinal) || processId.Length > 106)
    {
      throw new ArgumentException("Use a valid agent process identity.");
    }

    foreach (char character in processId)
    {
      if (!char.IsAsciiLetterOrDigit(character) && character != '-' && character != '_')
      {
        throw new ArgumentException("Use a valid agent process identity.");
      }
    }
  }
}
