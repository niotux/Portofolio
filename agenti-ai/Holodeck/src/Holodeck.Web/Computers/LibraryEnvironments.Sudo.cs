using System.Text;
using System.Text.Json;
using Holodeck.Web.Rooms;

namespace Holodeck.Web.Computers;

// Sudo is granted by the user to single processes: a declared service or an agent. Holodeck keeps the grants and sends
// the whole list to the supervisor, which keeps a copy so a restarted environment launches every process with its grant.
public sealed partial class LibraryEnvironments
{
  private const string SudoFile = "sudo.json";
  private const int MaxProcessIdLength = 106;

  public async Task<List<string>> SudoAsync(string projectId, string libraryKey, CancellationToken token)
  {
    LibrarySudoGrants grants = await ReadSudoAsync(projectId, libraryKey, token);
    return grants.Processes;
  }

  // Stores the grant and gives it to a running supervisor. Restarting the process so its launch changes, or recreating
  // a container that predates sudo, is the caller's part.
  public async Task<SudoChange> SetSudoAsync(string projectId, string libraryKey, string processId, bool enabled, CancellationToken token)
  {
    RequireProcessId(processId);
    SemaphoreSlim pending = GateFor(projectId, libraryKey);
    await pending.WaitAsync(token);
    try
    {
      LibrarySudoGrants grants = await ReadSudoAsync(projectId, libraryKey, token);
      grants.Processes.Remove(processId);
      if (enabled)
      {
        grants.Processes.Add(processId);
      }

      await SaveSudoAsync(projectId, libraryKey, grants, token);
      using JsonDocument? inspected = await docker.TryInspectContainerAsync(ContainerName(projectId, libraryKey), token);
      if (inspected is null || !IsRunning(inspected.RootElement))
      {
        return SudoChange.Saved;
      }

      if (PredatesSudo(inspected.RootElement))
      {
        return SudoChange.RecreateEnvironment;
      }

      LibraryEnvironmentConfig config = await ReadConfigAsync(projectId, libraryKey, token);
      string address = await AddressAsync(ContainerName(projectId, libraryKey), token);
      await PushSudoAsync(address, config, grants, token);
      return SudoChange.Applied;
    }
    finally
    {
      pending.Release();
    }
  }

  // Replaces the container. Home folders, tables and supervisor state are bind mounts and remain.
  public async Task<LibraryEnvironmentView> RecreateAsync(string projectId, string libraryKey, string libraryDirectory, LibraryEnvironmentDefinition definition, CancellationToken token, List<LibraryMount>? dependencies = null)
  {
    SemaphoreSlim pending = GateFor(projectId, libraryKey);
    await pending.WaitAsync(token);
    try
    {
      string name = ContainerName(projectId, libraryKey);
      await docker.StopContainerAsync(name, 10, token);
      await docker.RemoveContainerAsync(name, token);
    }
    finally
    {
      pending.Release();
    }

    return await EnsureAsync(projectId, libraryKey, libraryDirectory, definition, token, dependencies);
  }

  // Brings the supervisor's grants up to date. Declared services start before Holodeck can answer, so one whose grant
  // changed restarts; agents start later and pick theirs up then.
  private async Task<LibraryEnvironmentState> SyncSudoAsync(string projectId, string libraryKey, string address, LibraryEnvironmentConfig config, LibraryEnvironmentState state, CancellationToken token)
  {
    LibrarySudoGrants grants = await ReadSudoAsync(projectId, libraryKey, token);
    List<string> changed = new();
    foreach (LibraryEnvironmentProcessState process in state.Processes)
    {
      if (process.Sudo != grants.Processes.Contains(process.Id))
      {
        changed.Add(process.Id);
      }
    }

    if (changed.Count == 0)
    {
      return state;
    }

    await PushSudoAsync(address, config, grants, token);
    bool restarted = false;
    foreach (LibraryEnvironmentProcessConfig process in config.Processes)
    {
      if (changed.Contains(process.Id))
      {
        await ControlAsync(address, config, process.Id, "stop", token);
        await ControlAsync(address, config, process.Id, "start", token);
        restarted = true;
      }
    }

    return restarted ? await WaitUntilReadyAsync(address, config, token) : await ReadStateAsync(address, config, token);
  }

  private async Task PushSudoAsync(string address, LibraryEnvironmentConfig config, LibrarySudoGrants grants, CancellationToken token)
  {
    using HttpRequestMessage request = ControlRequest(HttpMethod.Post, address, "/sudo", config.ControlToken);
    // The supervisor reads a declared length, so the body is not streamed.
    request.Content = new StringContent(JsonSerializer.Serialize(grants, Json), Encoding.UTF8, "application/json");
    using HttpResponseMessage response = await http.SendAsync(request, token);
    if (!response.IsSuccessStatusCode)
    {
      throw new InvalidOperationException("The library supervisor refused the sudo grants.");
    }
  }

  private async Task ControlAsync(string address, LibraryEnvironmentConfig config, string processId, string action, CancellationToken token)
  {
    using HttpRequestMessage request = ControlRequest(HttpMethod.Post, address, $"/processes/{Uri.EscapeDataString(processId)}/{action}", config.ControlToken);
    using HttpResponseMessage response = await http.SendAsync(request, token);
    response.EnsureSuccessStatusCode();
  }

  private async Task<LibrarySudoGrants> ReadSudoAsync(string projectId, string libraryKey, CancellationToken token)
  {
    string file = Path.Combine(EnvironmentDirectory(projectId, libraryKey), "control", SudoFile);
    if (!File.Exists(file))
    {
      return new LibrarySudoGrants(new List<string>());
    }

    string contents = await File.ReadAllTextAsync(file, token);
    return JsonSerializer.Deserialize<LibrarySudoGrants>(contents, Json) ?? throw new InvalidOperationException("The saved sudo grants are invalid.");
  }

  private async Task SaveSudoAsync(string projectId, string libraryKey, LibrarySudoGrants grants, CancellationToken token)
  {
    string control = Path.Combine(EnvironmentDirectory(projectId, libraryKey), "control");
    Directory.CreateDirectory(control);
    if (!OperatingSystem.IsWindows())
    {
      File.SetUnixFileMode(control, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    string file = Path.Combine(control, SudoFile);
    string temporary = file + ".tmp";
    await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(grants, Json), token);
    File.Move(temporary, file, true);
  }

  private static bool IsRunning(JsonElement container)
  {
    return container.GetProperty("State").GetProperty("Running").GetBoolean();
  }

  // Containers created before sudo grants blocked privilege escalation for every process and lack sudo.
  private static bool PredatesSudo(JsonElement container)
  {
    JsonElement options = container.GetProperty("HostConfig").GetProperty("SecurityOpt");
    if (options.ValueKind != JsonValueKind.Array)
    {
      return false;
    }

    foreach (JsonElement option in options.EnumerateArray())
    {
      if (option.GetString() == "no-new-privileges")
      {
        return true;
      }
    }

    return false;
  }

  private static void RequireProcessId(string processId)
  {
    if (processId.Length == 0 || processId.Length > MaxProcessIdLength)
    {
      throw new ArgumentException("Use a valid process identity.");
    }

    foreach (char character in processId)
    {
      if (!char.IsAsciiLetterOrDigit(character) && character != '-' && character != '_')
      {
        throw new ArgumentException("Use a valid process identity.");
      }
    }
  }
}
