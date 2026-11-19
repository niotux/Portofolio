using Holodeck.Web.Computers;
using Holodeck.Web.Projects;
using Holodeck.Web.Rooms;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Holodeck.Tests;

public sealed class AgentSupervisorTests
{
  private const string DumpScript = "env | grep HOLODECK_ > env.txt\necho $$ > pid.txt\necho ready\nexec sleep 30\n";
  private static readonly CancellationToken Token = CancellationToken.None;

  [DatabaseFact]
  public async Task StartsWithEnvironmentAndStops()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    SupervisorSetup setup = await SupervisorSetup.CreateAsync(database, ["sh", "${Agent}/run.sh"], DumpScript);
    await setup.Supervisor.StartAgentAsync(setup.Member, Token);
    string table = setup.Paths.Table(setup.Member.ProjectId, setup.Member.Id);
    string env = Path.Combine(table, "env.txt");
    Assert.True(await Eventually.TrueAsync(() => EnvironmentDump.Read(env, "HOLODECK_SETTINGS") is not null));
    Assert.Equal("http://holodeck.test", EnvironmentDump.Read(env, "HOLODECK_URL"));
    Assert.Equal(table, EnvironmentDump.Read(env, "HOLODECK_TABLE"));
    Assert.Equal(setup.Paths.SettingsFile(setup.Member.ProjectId, setup.Member.Id), EnvironmentDump.Read(env, "HOLODECK_SETTINGS"));
    string key = EnvironmentDump.Read(env, "HOLODECK_KEY")!;
    Assert.Equal(setup.Member, await setup.Room.FindBySessionAsync(AgentKeys.Hash(key)));
    Assert.Equal(AgentStatus.Running, setup.Supervisor.Status(setup.Member.Id));
    Assert.True(await Eventually.TrueAsync(() => setup.Supervisor.ReadLog(setup.Member).AsSpan().Contains("ready", StringComparison.Ordinal)));
    string pid = File.ReadAllText(Path.Combine(table, "pid.txt")).AsSpan().Trim().ToString();
    await setup.Supervisor.StopAgentAsync(setup.Member, Token);
    Assert.Equal(AgentStatus.Stopped, setup.Supervisor.Status(setup.Member.Id));
    Assert.Null(await setup.Room.FindBySessionAsync(AgentKeys.Hash(key)));
    Assert.Empty(await setup.Room.ListShouldRunAsync());
    Assert.True(await Eventually.TrueAsync(() => !Directory.Exists("/proc/" + pid)));
  }

  [DatabaseFact]
  public async Task PackagedArgumentsUseExplicitPathsAndKeepLiteralArguments()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    SupervisorSetup setup = await SupervisorSetup.CreatePackagedAsync(database, "sh", true);
    await setup.Supervisor.StartAgentAsync(setup.Member, Token);
    try
    {
      string table = setup.Paths.Table(setup.Member.ProjectId, setup.Member.Id);
      string arguments = Path.Combine(table, "args.txt");
      string env = Path.Combine(table, "env.txt");
      Assert.True(await Eventually.TrueAsync(() => EnvironmentDump.Read(env, "HOLODECK_PROGRAM") is not null));
      string libraryDirectory = Path.Combine(setup.Paths.Libraries, "library with spaces");
      string agentDirectory = Path.Combine(libraryDirectory, "agents", "probe");
      Assert.Equal(["literal.txt", "a value with spaces", libraryDirectory, agentDirectory, "$(touch shell-ran)", ""], File.ReadAllLines(arguments));
      Assert.False(File.Exists(Path.Combine(table, "shell-ran")));
      Assert.Equal(libraryDirectory, EnvironmentDump.Read(env, "HOLODECK_LIBRARY"));
      Assert.Equal(agentDirectory, EnvironmentDump.Read(env, "HOLODECK_PROGRAM"));
      Assert.Equal(table, EnvironmentDump.Read(env, "HOLODECK_TABLE"));
    }
    finally
    {
      await setup.Supervisor.StopAgentAsync(setup.Member, Token);
    }
  }

  [DatabaseFact]
  public async Task PackagedProgramsResolveExplicitAndRelativeCommands()
  {
    string[] commands = ["${Agent}/run.sh", "./run.sh"];
    foreach (string command in commands)
    {
      await using TestDatabase database = await TestDatabase.CreateAsync();
      SupervisorSetup setup = await SupervisorSetup.CreatePackagedAsync(database, command, false);
      await setup.Supervisor.StartAgentAsync(setup.Member, Token);
      try
      {
        Assert.True(await Eventually.TrueAsync(() => setup.Supervisor.ReadLog(setup.Member).Contains("ready", StringComparison.Ordinal)));
        Assert.Equal(AgentStatus.Running, setup.Supervisor.Status(setup.Member.Id));
      }
      finally
      {
        await setup.Supervisor.StopAgentAsync(setup.Member, Token);
      }
    }
  }

  [DatabaseFact]
  public async Task FailingProgramsShowAsCrashedWithoutRestarting()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    SupervisorSetup setup = await SupervisorSetup.CreateAsync(database, ["sh", "-c", "echo boom >&2; exit 3"], null);
    await setup.Supervisor.StartAgentAsync(setup.Member, Token);
    Assert.True(await Eventually.TrueAsync(() => setup.Supervisor.Status(setup.Member.Id) == AgentStatus.Crashed));
    Assert.True(await Eventually.TrueAsync(() => setup.Supervisor.ReadLog(setup.Member).AsSpan().Contains("exited with code 3", StringComparison.Ordinal)));
    Assert.Contains("boom", setup.Supervisor.ReadLog(setup.Member));
    Assert.Empty(await setup.Room.ListShouldRunAsync());
    await Task.Delay(1000);
    Assert.Equal(AgentStatus.Crashed, setup.Supervisor.Status(setup.Member.Id));
  }

  [DatabaseFact]
  public async Task MissingProgramsShowAsCrashed()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    SupervisorSetup setup = await SupervisorSetup.CreateAsync(database, ["no-such-binary-holodeck"], null);
    await setup.Supervisor.StartAgentAsync(setup.Member, Token);
    Assert.Equal(AgentStatus.Crashed, setup.Supervisor.Status(setup.Member.Id));
    Assert.Contains("could not start", setup.Supervisor.ReadLog(setup.Member));
    Assert.Empty(await setup.Room.ListShouldRunAsync());
  }

  [DatabaseFact]
  public async Task AgentsThatNeverStartedCanStillBeStopped()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    SupervisorSetup setup = await SupervisorSetup.CreateAsync(database, ["no-such-binary-holodeck"], null);
    await setup.Supervisor.StartAgentAsync(setup.Member, Token);
    await setup.Supervisor.StopAgentAsync(setup.Member, Token);
    Assert.Equal(AgentStatus.Stopped, setup.Supervisor.Status(setup.Member.Id));
  }

  [DatabaseFact]
  public async Task AdoptsRunningAgentsOnStartupWithoutRestartingThem()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    SupervisorSetup setup = await SupervisorSetup.CreateAsync(database, ["sh", "${Agent}/run.sh"], DumpScript);
    string env = Path.Combine(setup.Paths.Table(setup.Member.ProjectId, setup.Member.Id), "env.txt");
    await setup.Supervisor.StartAgentAsync(setup.Member, Token);
    Assert.True(await Eventually.TrueAsync(() => EnvironmentDump.Read(env, "HOLODECK_SETTINGS") is not null));
    string firstKey = EnvironmentDump.Read(env, "HOLODECK_KEY")!;
    string pidFile = Path.Combine(setup.Paths.Table(setup.Member.ProjectId, setup.Member.Id), "pid.txt");
    Assert.True(await Eventually.TrueAsync(() =>
    {
      if (!File.Exists(pidFile))
      {
        return false;
      }

      return File.ReadAllText(pidFile).Trim().Length > 0;
    }));
    string firstPid = File.ReadAllText(pidFile);
    await setup.Supervisor.StopAsync(Token);
    Assert.Equal(setup.Member, Assert.Single(await setup.Room.ListShouldRunAsync()));
    Assert.Equal(firstPid, File.ReadAllText(pidFile));
    Assert.True(Directory.Exists("/proc/" + firstPid.Trim()));
    AgentSupervisor restarted = setup.NewSupervisor();
    await restarted.StartAsync(Token);
    Assert.Equal(AgentStatus.Running, restarted.Status(setup.Member.Id));
    Assert.True(await Eventually.TrueAsync(() => EnvironmentDump.Read(env, "HOLODECK_SETTINGS") is not null));
    string secondKey = EnvironmentDump.Read(env, "HOLODECK_KEY")!;
    Assert.Equal(firstKey, secondKey);
    Assert.Equal(firstPid, File.ReadAllText(pidFile));
    Assert.Equal(setup.Member, await setup.Room.FindBySessionAsync(AgentKeys.Hash(secondKey)));
    await restarted.StopAgentAsync(setup.Member, Token);
    Assert.Null(await setup.Room.FindBySessionAsync(AgentKeys.Hash(firstKey)));
    File.Delete(env);
    await restarted.StartAgentAsync(setup.Member, Token);
    try
    {
      Assert.True(await Eventually.TrueAsync(() => EnvironmentDump.Read(env, "HOLODECK_KEY") is not null));
      Assert.Equal(firstKey, EnvironmentDump.Read(env, "HOLODECK_KEY"));
      Assert.Equal(setup.Member, await setup.Room.FindBySessionAsync(AgentKeys.Hash(firstKey)));
    }
    finally
    {
      await restarted.StopAgentAsync(setup.Member, Token);
    }
  }

  [DatabaseFact]
  public async Task StartupCreatesStoppedComputersAndDoesNotRestoreSavedRunIntent()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    SupervisorSetup setup = await SupervisorSetup.CreateAsync(database, ["sh", "${Agent}/run.sh"], DumpScript);
    await setup.Room.SetShouldRunAsync(setup.Member.Id, true);
    await setup.Supervisor.StartAsync(Token);
    ProcessAgentRuntime runtime = new(setup.Paths, Options.Create(new SupervisorOptions { HolodeckUrl = "http://holodeck.test" }));
    AgentComputerState? computer = await runtime.InspectAsync(setup.Member, Token);
    Assert.NotNull(computer);
    Assert.False(computer.Running);
    Assert.Equal(AgentStatus.Stopped, setup.Supervisor.Status(setup.Member.Id));
    Assert.False(File.Exists(Path.Combine(setup.Paths.Table(setup.Member.ProjectId, setup.Member.Id), "pid.txt")));
    Assert.Null(await setup.Room.FindBySessionAsync(AgentKeys.Hash(computer.Key)));
    await setup.Supervisor.StopAsync(Token);
    await runtime.DeleteAsync(setup.Member, Token);
  }

  [DatabaseFact]
  public async Task RefusesYouAndRemovedMembers()
  {
    await using TestDatabase database = await TestDatabase.CreateAsync();
    SupervisorSetup setup = await SupervisorSetup.CreateAsync(database, ["sh", "${Agent}/run.sh"], DumpScript);
    RoomMember you = await setup.Room.EnsureYouAsync(setup.Member.ProjectId);
    await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Supervisor.StartAgentAsync(you, Token));
    await setup.Room.RemoveAsync(setup.Member.ProjectId, setup.Member.Id);
    await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Supervisor.StartAgentAsync(setup.Member, Token));
    await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Supervisor.CreateAgentAsync(setup.Member, Token));
    RoomMember removed = await setup.Room.GetMemberAsync(setup.Member.ProjectId, setup.Member.Id);
    await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Supervisor.StartAgentAsync(removed, Token));
  }

  [Fact]
  public void KeysAreRandomAndHashedConsistently()
  {
    string first = AgentKeys.Create();
    string second = AgentKeys.Create();
    Assert.NotEqual(first, second);
    Assert.Equal(AgentKeys.Hash(first), AgentKeys.Hash(first));
    Assert.Equal(64, AgentKeys.Hash(first).Length);
  }

  private sealed class SupervisorSetup
  {
    private readonly AgentLibrary library;
    private readonly ProjectLibraryStore projectLibraries;

    private SupervisorSetup(RoomStore room, AgentLibrary library, RoomPaths paths, RoomMember member, ProjectLibraryStore projectLibraries, GrantStore grants)
    {
      Room = room;
      Grants = grants;
      this.library = library;
      this.projectLibraries = projectLibraries;
      Paths = paths;
      Member = member;
      Supervisor = NewSupervisor();
    }

    public RoomStore Room { get; }
    public GrantStore Grants { get; }
    public RoomPaths Paths { get; }
    public RoomMember Member { get; }
    public AgentSupervisor Supervisor { get; }

    public AgentSupervisor NewSupervisor()
    {
      IOptions<SupervisorOptions> options = Options.Create(new SupervisorOptions { HolodeckUrl = "http://holodeck.test" });
      return new AgentSupervisor(Room, library, Paths, new RoomSignals(), new ProcessAgentRuntime(Paths, options), Grants, NullLogger<AgentSupervisor>.Instance, projectLibraries);
    }

    public static async Task<SupervisorSetup> CreateAsync(TestDatabase database, List<string> start, string? script)
    {
      ProjectStore projects = new(database.Database);
      await projects.InitializeAsync(Token);
      RoomPaths paths = TestLibrary.Paths(database.DataPath);
      RoomStore room = new(database.Database, paths);
      await room.InitializeAsync(Token);
      string directory = TestLibrary.Write(database.DataPath, "probe", "Probe", start);
      if (script is not null)
      {
        File.WriteAllText(Path.Combine(directory, "run.sh"), script);
      }

      AgentLibrary library = new(paths);
      Project project = await projects.CreateAsync(new ProjectInput("Supervised", null, null), Token);
      RoomMember member = await room.PlaceAsync(project.Id, library.Get(TestLibrary.Reference("probe")));
      ProjectLibraryStore projectLibraries = new(database.Database, library);
      await projectLibraries.InitializeAsync(Token);
      GrantStore grants = new(database.Database);
      await grants.InitializeAsync(Token);
      return new SupervisorSetup(room, library, paths, member, projectLibraries, grants);
    }

    public static async Task<SupervisorSetup> CreatePackagedAsync(TestDatabase database, string command, bool passScriptArgument)
    {
      ProjectStore projects = new(database.Database);
      await projects.InitializeAsync(Token);
      RoomPaths paths = TestLibrary.Paths(database.DataPath);
      RoomStore room = new(database.Database, paths);
      await room.InitializeAsync(Token);
      string libraryDirectory = Path.Combine(paths.Libraries, "library with spaces");
      string agentDirectory = Path.Combine(libraryDirectory, "agents", "probe");
      Directory.CreateDirectory(agentDirectory);
      Directory.CreateDirectory(Path.Combine(libraryDirectory, "teams"));
      File.WriteAllText(Path.Combine(libraryDirectory, "Library.json"), """
        { "FormatVersion": 1, "Id": "example.probes", "Version": "1.0.0", "Name": "Probes", "Dependencies": [], "Runtime": { "Image": "holodeck-library:1", "Processes": [] } }
        """);
      string scriptArgument = passScriptArgument ? "\"${Agent}/run.sh\", " : "";
      string manifest = """
        {
          "Id": "probe",
          "Name": "Probe",
          "Launch": {
            "Command": COMMAND,
            "Args": [SCRIPT"literal.txt", "a value with spaces", "${Library}", "${Agent}", "$(touch shell-ran)", ""]
          }
        }
        """;
      manifest = manifest.Replace("COMMAND", System.Text.Json.JsonSerializer.Serialize(command), StringComparison.Ordinal).Replace("SCRIPT", scriptArgument, StringComparison.Ordinal);
      File.WriteAllText(Path.Combine(agentDirectory, "Agent.json"), manifest);
      File.WriteAllText(Path.Combine(agentDirectory, "literal.txt"), "Arguments naming existing files must remain literal.");
      string script = "#!/bin/sh\nprintf '%s\\n' \"$@\" > args.txt\n" + DumpScript;
      string scriptPath = Path.Combine(agentDirectory, "run.sh");
      File.WriteAllText(scriptPath, script);
      if (!OperatingSystem.IsWindows())
      {
        File.SetUnixFileMode(scriptPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
      }

      AgentLibrary library = new(paths);
      Project project = await projects.CreateAsync(new ProjectInput("Packaged supervisor", null, null), Token);
      RoomMember member = await room.PlaceAsync(project.Id, library.Get("example.probes/1.0.0/agents/probe"));
      ProjectLibraryStore projectLibraries = new(database.Database, library);
      await projectLibraries.InitializeAsync(Token);
      GrantStore grants = new(database.Database);
      await grants.InitializeAsync(Token);
      return new SupervisorSetup(room, library, paths, member, projectLibraries, grants);
    }
  }
}
