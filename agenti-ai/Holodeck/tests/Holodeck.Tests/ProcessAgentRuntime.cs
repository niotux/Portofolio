using System.Collections.Concurrent;
using System.Diagnostics;
using Holodeck.Web.Computers;
using Holodeck.Web.Rooms;
using Microsoft.Extensions.Options;

namespace Holodeck.Tests;

// Test-only runtime: runs agents as plain local processes, so the supervisor and room API can be tested without
// Docker. Production always runs agents in containers.
internal sealed class ProcessAgentRuntime(RoomPaths paths, IOptions<SupervisorOptions> options) : IAgentRuntime, IAsyncDisposable
{
  private static readonly ConcurrentDictionary<string, ProcessComputer> computers = new();

  public Task<AgentComputerState?> InspectAsync(RoomMember member, CancellationToken token)
  {
    AgentComputerState? state = null;
    if (computers.TryGetValue(ComputerId(member), out ProcessComputer? computer))
    {
      bool running = computer.Process is not null && !computer.Process.HasExited;
      state = new AgentComputerState(computer.Start.Key, running);
    }

    return Task.FromResult(state);
  }

  public Task CreateAsync(AgentStart start, CancellationToken token)
  {
    computers.TryAdd(ComputerId(start.Member), new ProcessComputer(start));
    return Task.CompletedTask;
  }

  public async Task StopAsync(RoomMember member, CancellationToken token)
  {
    if (computers.TryGetValue(ComputerId(member), out ProcessComputer? computer))
    {
      if (computer.Running is not null)
      {
        await computer.Running.StopAsync();
      }
    }
  }

  public Task ResetHomeAsync(RoomMember member, CancellationToken token)
  {
    string home = paths.Home(member.ProjectId, member.Id);
    if (Directory.Exists(home))
    {
      Directory.Delete(home, true);
    }

    Directory.CreateDirectory(home);
    return Task.CompletedTask;
  }

  public async Task DeleteAsync(RoomMember member, CancellationToken token)
  {
    if (computers.TryRemove(ComputerId(member), out ProcessComputer? computer))
    {
      if (computer.Running is not null)
      {
        await computer.Running.StopAsync();
      }
    }
  }

  public async ValueTask DisposeAsync()
  {
    foreach (KeyValuePair<string, ProcessComputer> entry in computers)
    {
      if (!entry.Key.StartsWith(paths.Root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
      {
        continue;
      }

      if (computers.TryRemove(entry.Key, out ProcessComputer? computer) && computer.Running is not null)
      {
        await computer.Running.StopAsync();
      }
    }
  }

  public Task PrepareAsync(CancellationToken token)
  {
    return Task.CompletedTask;
  }

  public async Task<RunningAgent> StartAsync(AgentStart start, Action<string> output, CancellationToken token)
  {
    await CreateAsync(start, token);
    ProcessComputer computer = computers[ComputerId(start.Member)];
    computer.Output = output;
    if (computer.Process is not null && !computer.Process.HasExited)
    {
      return computer.Running!;
    }

    start = computer.Start;
    RoomMember member = start.Member;
    LibraryAgent agent = start.Agent;
    string table = paths.Table(member.ProjectId, member.Id);
    Directory.CreateDirectory(table);
    Directory.CreateDirectory(paths.Home(member.ProjectId, member.Id));
    ProcessStartInfo info = new(ResolveProgram(agent))
    {
      WorkingDirectory = table,
      UseShellExecute = false,
      RedirectStandardOutput = true,
      RedirectStandardError = true
    };
    for (int index = 1; index < agent.Start.Count; index++)
    {
      info.ArgumentList.Add(Placeholders.Expand(agent.Start[index], agent.Directory, agent.LibraryDirectory));
    }

    info.Environment["HOLODECK_URL"] = options.Value.HolodeckUrl;
    info.Environment["HOLODECK_KEY"] = start.Key;
    info.Environment["HOLODECK_TABLE"] = table;
    info.Environment["HOLODECK_SETTINGS"] = paths.SettingsFile(member.ProjectId, member.Id);
    info.Environment["HOLODECK_PROGRAM"] = agent.Directory;
    info.Environment["HOLODECK_LIBRARY"] = agent.LibraryDirectory;
    Process process = new() { StartInfo = info, EnableRaisingEvents = true };
    TaskCompletionSource<int> exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    process.OutputDataReceived += (_, line) => Write(computer.Output, line.Data);
    process.ErrorDataReceived += (_, line) => Write(computer.Output, line.Data);
    process.Exited += (_, _) => Task.Run(() =>
    {
      process.WaitForExit();
      exited.TrySetResult(process.ExitCode);
    });
    process.Start();
    process.BeginOutputReadLine();
    process.BeginErrorReadLine();
    computer.Process = process;
    computer.Running = new RunningAgent(exited.Task, () => StopAsync(process, exited.Task));
    return computer.Running;
  }

  private static async Task StopAsync(Process process, Task<int> exited)
  {
    try
    {
      if (!process.HasExited)
      {
        process.Kill(true);
      }
    }
    catch (InvalidOperationException)
    {
    }

    await exited;
  }

  private static void Write(Action<string>? output, string? line)
  {
    if (line is not null && output is not null)
    {
      output(line);
    }
  }

  private string ComputerId(RoomMember member)
  {
    return paths.AgentDirectory(member.ProjectId, member.Id);
  }

  private sealed class ProcessComputer(AgentStart start)
  {
    public AgentStart Start { get; } = start;
    public Process? Process { get; set; }
    public RunningAgent? Running { get; set; }
    public Action<string>? Output { get; set; }
  }

  // A program named with a path is relative to the agent's folder; a bare name is found on PATH.
  private static string ResolveProgram(LibraryAgent agent)
  {
    string program = Placeholders.Expand(agent.Start[0], agent.Directory, agent.LibraryDirectory);
    bool hasSeparator = program.AsSpan().IndexOfAny('/', '\\') >= 0;
    if (hasSeparator && !Path.IsPathRooted(program))
    {
      return Path.Combine(agent.Directory, program);
    }

    return program;
  }
}
