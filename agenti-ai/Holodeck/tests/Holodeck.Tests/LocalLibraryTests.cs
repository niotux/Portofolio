using Holodeck.Web.Computers;
using System.IO.Compression;
using System.Text.Json;
using Holodeck.Web.Rooms;
using Xunit;

namespace Holodeck.Tests;

public sealed class LocalLibraryTests : IDisposable
{
  private readonly string dataPath = Path.Combine(Path.GetTempPath(), "holodeck-packages-" + Guid.NewGuid().ToString("N"));

  [Fact]
  public void ResolvesDeclaredExactLocalDependenciesAndTeamMembers()
  {
    WriteLibrary("utilities", "example.utilities", "1.0.0");
    WriteLibrary("utilities-new", "example.utilities", "2.0.0");
    string writing = WriteLibrary("arbitrary folder name", "example.writing", "1.0.0", [new LibraryDependency("example.utilities", "1.0.0")]);
    WriteTeam(writing, [new TeamMemberManifest("writer", "Writer", "example.writing/agents/probe"), new TeamMemberManifest("researcher", "Researcher", "example.utilities/agents/probe")], [["You", "writer"], ["writer", "researcher"]]);
    AgentLibrary library = CreateLibrary();

    LibraryCatalog catalog = library.ReadCatalog();

    Assert.Empty(catalog.Diagnostics);
    Assert.Equal(3, catalog.Agents.Count);
    LibraryTeam team = Assert.Single(catalog.Teams);
    Assert.Equal("example.writing/1.0.0/teams/sample", team.Folder);
    Assert.Equal("example.utilities/1.0.0/agents/probe", team.Members[1].Agent.Folder);
    Assert.Equal(2, team.Wires.Count);
    LibraryAgent agent = library.Get("example.writing/1.0.0/agents/probe");
    Assert.Equal(writing, agent.LibraryDirectory);
    Assert.Equal("${Agent}/main.py", agent.Start[1]);
    Assert.Equal(team.Folder, library.GetTeam(team.Folder).Folder);
  }

  [Fact]
  public void AgentLaunchUsesOnlyDirectDependenciesAtTheirExactVersions()
  {
    WriteLibrary("base", "example.base", "1.0.0");
    string expected = WriteLibrary("utilities-old", "example.utilities", "1.0.0", [new LibraryDependency("example.base", "1.0.0")]);
    WriteLibrary("utilities-new", "example.utilities", "2.0.0");
    string writing = WriteLibrary("writing", "example.writing", "1.0.0", [new LibraryDependency("example.utilities", "1.0.0")]);
    WriteAgent(writing, launch: new AgentLaunch("${Dependency:example.utilities}/runtime/Person", ["${Dependency:example.utilities}/prompts"]));

    AgentLibrary library = CreateLibrary();
    Assert.Empty(library.ReadCatalog().Diagnostics);
    LibraryAgent agent = library.Get("example.writing/1.0.0/agents/probe");
    Assert.NotNull(agent.Dependencies);
    LibraryMount mount = Assert.Single(agent.Dependencies);
    Assert.Equal(new LibraryMount("example.utilities", "1.0.0", expected), mount);
  }

  [Theory]
  [InlineData("${Dependency:example.undeclared}/run")]
  [InlineData("${Dependency:example.base}/run")]
  [InlineData("${Dependency:}/run")]
  [InlineData("${Dependency}/run")]
  [InlineData("${Dependency:example.utilities/run")]
  public void RejectsUndeclaredTransitiveAndMalformedDependencyLaunchTokens(string argument)
  {
    WriteLibrary("base", "example.base", "1.0.0");
    WriteLibrary("utilities", "example.utilities", "1.0.0", [new LibraryDependency("example.base", "1.0.0")]);
    WriteLibrary("undeclared", "example.undeclared", "1.0.0");
    string writing = WriteLibrary("writing", "example.writing", "1.0.0", [new LibraryDependency("example.utilities", "1.0.0")]);
    WriteAgent(writing, launch: new AgentLaunch("python3", [argument]));

    LibraryCatalog catalog = CreateLibrary().ReadCatalog();

    Assert.Contains("Dependency placeholder", Assert.Single(catalog.Diagnostics).Message);
    Assert.DoesNotContain(catalog.Agents, agent => agent.Folder.StartsWith("example.writing/", StringComparison.Ordinal));
  }

  [Fact]
  public void MissingDependencyDisablesOnlyAffectedLibraries()
  {
    WriteLibrary("broken", "example.broken", "1.0.0", [new LibraryDependency("example.missing", "1.0.0")]);
    WriteLibrary("dependent", "example.dependent", "1.0.0", [new LibraryDependency("example.broken", "1.0.0")]);
    WriteLibrary("good", "example.good", "1.0.0");
    LibraryCatalog catalog = CreateLibrary().ReadCatalog();

    Assert.Equal("example.good/1.0.0/agents/probe", Assert.Single(catalog.Agents).Folder);
    Assert.Equal(2, catalog.Diagnostics.Count);
    Assert.Contains(catalog.Diagnostics, item => item.Message.Contains("Missing local dependency"));
    Assert.Contains(catalog.Diagnostics, item => item.Message.Contains("unavailable"));
  }

  [Fact]
  public void DependencyCyclesAndDuplicateIdentitiesAreReported()
  {
    WriteLibrary("first", "example.first", "1.0.0", [new LibraryDependency("example.second", "1.0.0")]);
    WriteLibrary("second", "example.second", "1.0.0", [new LibraryDependency("example.first", "1.0.0")]);
    WriteLibrary("duplicate-a", "example.duplicate", "1.0.0");
    WriteLibrary("duplicate-b", "example.duplicate", "1.0.0");
    LibraryCatalog catalog = CreateLibrary().ReadCatalog();

    Assert.Empty(catalog.Agents);
    Assert.Contains(catalog.Diagnostics, item => item.Message.Contains("Circular"));
    Assert.Contains(catalog.Diagnostics, item => item.Message.Contains("Duplicate library identity"));
  }

  [Fact]
  public void TeamCannotReferenceUndeclaredInstalledLibrary()
  {
    WriteLibrary("utilities", "example.utilities", "1.0.0");
    string writing = WriteLibrary("writing", "example.writing", "1.0.0");
    WriteTeam(writing, [new TeamMemberManifest("researcher", "Researcher", "example.utilities/agents/probe")], []);
    LibraryCatalog catalog = CreateLibrary().ReadCatalog();

    Assert.Empty(catalog.Teams);
    Assert.Single(catalog.Agents);
    Assert.Contains("declared local library dependency", Assert.Single(catalog.Diagnostics).Message);
  }

  [Theory]
  [InlineData("missing", "writer")]
  [InlineData("writer", "writer")]
  [InlineData("you", "writer")]
  public void RejectsInvalidTeamWires(string first, string second)
  {
    string writing = WriteLibrary("writing", "example.writing", "1.0.0");
    WriteTeam(writing, [new TeamMemberManifest("writer", "Writer", "example.writing/agents/probe")], [[first, second]]);
    LibraryCatalog catalog = CreateLibrary().ReadCatalog();

    Assert.Empty(catalog.Teams);
    Assert.Single(catalog.Diagnostics);
  }

  [Fact]
  public void SettingsCanUseArbitraryLibraryLayoutButCannotEscape()
  {
    string directory = WriteLibrary("writing", "example.writing", "1.0.0");
    Directory.CreateDirectory(Path.Combine(directory, "custom"));
    string settings = Path.Combine(directory, "custom", "defaults.json");
    File.WriteAllText(settings, "{}");
    WriteAgent(directory, "../../custom/defaults.json");
    LibraryAgent agent = Assert.Single(CreateLibrary().List());
    Assert.Equal(settings, agent.SettingsPath);

    WriteAgent(directory, "../../../outside.json");
    LibraryCatalog catalog = CreateLibrary().ReadCatalog();
    Assert.Empty(catalog.Agents);
    Assert.Contains("escapes", Assert.Single(catalog.Diagnostics).Message);
  }

  [Fact]
  public void DiscoversZipAndRetainsExecutablePermissionsWithoutTreatingCacheAsSource()
  {
    string directory = WriteLibrary("native", "example.native", "1.0.0");
    string program = Path.Combine(directory, "agents", "probe", "run");
    File.WriteAllText(program, "#!/bin/sh\nexit 0\n");
    if (!OperatingSystem.IsWindows())
    {
      File.SetUnixFileMode(program, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    string zip = Path.Combine(TestLibrary.Paths(dataPath).Libraries, "native.zip");
    ZipFile.CreateFromDirectory(directory, zip);
    Directory.Delete(directory, true);
    AgentLibrary library = CreateLibrary();
    LibraryAgent agent = Assert.Single(library.List());
    Assert.StartsWith(TestLibrary.Paths(dataPath).LibraryCache, agent.Directory);
    if (!OperatingSystem.IsWindows())
    {
      Assert.NotEqual(0, (int)(File.GetUnixFileMode(Path.Combine(agent.Directory, "run")) & UnixFileMode.UserExecute));
    }

    Assert.Single(library.List());
    File.Delete(zip);
    Assert.Empty(library.List());
  }

  [Theory]
  [InlineData("../escape")]
  [InlineData("/absolute")]
  [InlineData("agents/probe/../../../../escape")]
  public void RejectsZipTraversal(string entryName)
  {
    string libs = TestLibrary.Paths(dataPath).Libraries;
    Directory.CreateDirectory(libs);
    using (ZipArchive archive = ZipFile.Open(Path.Combine(libs, "bad.zip"), ZipArchiveMode.Create))
    {
      archive.CreateEntry(entryName);
    }

    LibraryCatalog catalog = CreateLibrary().ReadCatalog();
    Assert.Empty(catalog.Agents);
    Assert.Single(catalog.Diagnostics);
    Assert.False(File.Exists(Path.Combine(dataPath, "escape")));
  }

  [Fact]
  public void CorruptZipProducesDiagnostic()
  {
    string libs = TestLibrary.Paths(dataPath).Libraries;
    Directory.CreateDirectory(libs);
    File.WriteAllText(Path.Combine(libs, "broken.zip"), "not an archive");

    LibraryCatalog catalog = CreateLibrary().ReadCatalog();

    Assert.Empty(catalog.Agents);
    Assert.Single(catalog.Diagnostics);
  }

  [Fact]
  public void RejectsLowercaseManifestProperties()
  {
    string directory = WriteLibrary("writing", "example.writing", "1.0.0");
    File.WriteAllText(Path.Combine(directory, "Library.json"), """{"formatVersion":1,"id":"example.writing","version":"1.0.0","name":"Writing"}""");
    LibraryCatalog catalog = CreateLibrary().ReadCatalog();
    Assert.Empty(catalog.Agents);
    Assert.Contains("FormatVersion", Assert.Single(catalog.Diagnostics).Message);
  }

  [Fact]
  public void AgentsRequireTheirLibraryToDeclareRuntime()
  {
    string directory = WriteLibrary("writing", "example.writing", "1.0.0");
    File.WriteAllText(Path.Combine(directory, "Library.json"), """{"FormatVersion":1,"Id":"example.writing","Version":"1.0.0","Name":"Writing"}""");
    LibraryCatalog catalog = CreateLibrary().ReadCatalog();
    Assert.Empty(catalog.Agents);
    Assert.Contains("Runtime", Assert.Single(catalog.Diagnostics).Message);
  }

  [Fact]
  public void RefusesContainerServices()
  {
    string directory = WriteLibrary("writing", "example.writing", "1.0.0");
    File.WriteAllText(Path.Combine(directory, "Library.json"), """{"FormatVersion":1,"Id":"example.writing","Version":"1.0.0","Name":"Writing","Services":[{"Name":"embed","Image":"embed:1"}],"Runtime":{"Image":"holodeck-library:1","Processes":[]}}""");
    LibraryCatalog catalog = CreateLibrary().ReadCatalog();
    Assert.Empty(catalog.Agents);
    Assert.Contains("Runtime.Processes", Assert.Single(catalog.Diagnostics).Message);
  }

  private AgentLibrary CreateLibrary() => new(TestLibrary.Paths(dataPath));

  private string WriteLibrary(string folder, string id, string version, List<LibraryDependency>? dependencies = null)
  {
    string directory = Path.Combine(TestLibrary.Paths(dataPath).Libraries, folder);
    Directory.CreateDirectory(Path.Combine(directory, "agents", "probe"));
    Directory.CreateDirectory(Path.Combine(directory, "teams"));
    File.WriteAllText(Path.Combine(directory, "Library.json"), JsonSerializer.Serialize(new LibraryManifest(1, id, version, "Test library", null, dependencies, Runtime: new LibraryEnvironmentDefinition(TestLibrary.RuntimeImage, new()))));
    WriteAgent(directory);
    return directory;
  }

  private static void WriteAgent(string directory, string? settings = null, AgentLaunch? launch = null)
  {
    PackageAgentManifest manifest = new("probe", "Probe", null, launch ?? new AgentLaunch("python3", ["${Agent}/main.py"]), settings);
    File.WriteAllText(Path.Combine(directory, "agents", "probe", "Agent.json"), JsonSerializer.Serialize(manifest));
  }

  private static void WriteTeam(string directory, List<TeamMemberManifest> members, List<List<string>> wires)
  {
    string teamDirectory = Path.Combine(directory, "teams", "sample");
    Directory.CreateDirectory(teamDirectory);
    TeamManifest manifest = new("sample", "Sample", null, members, wires);
    File.WriteAllText(Path.Combine(teamDirectory, "Team.json"), JsonSerializer.Serialize(manifest));
  }

  public void Dispose()
  {
    if (Directory.Exists(dataPath))
    {
      Directory.Delete(dataPath, true);
    }
  }
}
