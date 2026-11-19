using Holodeck.Web.Computers;
using Holodeck.Web.Rooms;
using Xunit;

namespace Holodeck.Tests;

public sealed class LibraryAppletTests
{
  [Fact]
  public void AnAppletOnlyLibraryHasNoAgentOrServiceRequirement()
  {
    using LibraryAppletFixture fixture = new();
    fixture.Write();

    LibraryCatalog catalog = fixture.Read();
    Assert.Empty(catalog.Diagnostics);
    Assert.Empty(catalog.Agents);
    Assert.Empty(catalog.Teams);
    LibraryApplicationPackage package = Assert.Single(catalog.Applications);
    Assert.Equal("utilities/1.0.0", package.Key);
    Assert.Null(package.Runtime);
    Assert.Equal("terminal", Assert.Single(package.Applets).Id);
  }

  [Fact]
  public void AnAppletCanDeclareItsRuntimeProcess()
  {
    using LibraryAppletFixture fixture = new();
    LibraryEnvironmentDefinition runtime = new("holodeck-library:1", [new LibraryEnvironmentProcess("bash", ["python3", "/library/server.py"], 8090, "/health")]);
    fixture.Write(runtime: runtime, applet: new LibraryApplet("terminal", "Terminal", "applets/terminal/index.html", Services: ["bash"]));

    LibraryCatalog catalog = fixture.Read();
    Assert.Empty(catalog.Diagnostics);
    LibraryApplicationPackage package = Assert.Single(catalog.Applications);
    Assert.NotNull(package.Runtime);
    Assert.Equal("bash", Assert.Single(package.Runtime.Processes).Id);
    Assert.Equal("bash", Assert.Single(Assert.Single(package.Applets).Services!));
  }

  [Fact]
  public void UndeclaredAppletServicesRejectTheLibrary()
  {
    using LibraryAppletFixture fixture = new();
    fixture.Write(applet: new LibraryApplet("terminal", "Terminal", "applets/terminal/index.html", Services: ["missing"]));

    LibraryCatalog catalog = fixture.Read();
    Assert.Empty(catalog.Applications);
    Assert.Contains("undeclared process", Assert.Single(catalog.Diagnostics).Message);
  }

  [Fact]
  public void DuplicateProcessIdentitiesRejectTheLibrary()
  {
    using LibraryAppletFixture fixture = new();
    LibraryEnvironmentDefinition runtime = new("holodeck-library:1", [new LibraryEnvironmentProcess("server", ["sleep", "60"]), new LibraryEnvironmentProcess("server", ["sleep", "60"])]);
    fixture.Write(runtime: runtime);

    LibraryCatalog catalog = fixture.Read();
    Assert.Empty(catalog.Applications);
    Assert.Contains("Duplicate runtime process", Assert.Single(catalog.Diagnostics).Message);
  }

  [Theory]
  [InlineData(8079)]
  [InlineData(1023)]
  [InlineData(65536)]
  public void ReservedAndInvalidPortsRejectTheLibrary(int port)
  {
    using LibraryAppletFixture fixture = new();
    fixture.Write(runtime: new LibraryEnvironmentDefinition("holodeck-library:1", [new LibraryEnvironmentProcess("server", ["sleep", "60"], port)]));

    LibraryCatalog catalog = fixture.Read();
    Assert.Empty(catalog.Applications);
    Assert.Contains("unique unprivileged service port", Assert.Single(catalog.Diagnostics).Message);
  }

  [Fact]
  public void DuplicateServicePortsRejectTheLibrary()
  {
    using LibraryAppletFixture fixture = new();
    LibraryEnvironmentDefinition runtime = new("holodeck-library:1", [new LibraryEnvironmentProcess("first", ["sleep", "60"], 8090), new LibraryEnvironmentProcess("second", ["sleep", "60"], 8090)]);
    fixture.Write(runtime: runtime);

    LibraryCatalog catalog = fixture.Read();
    Assert.Empty(catalog.Applications);
    Assert.Contains("unique unprivileged service port", Assert.Single(catalog.Diagnostics).Message);
  }

  [Theory]
  [InlineData("applets/terminal/missing.html")]
  [InlineData("applets/terminal/terminal.js")]
  [InlineData("../../outside.html")]
  public void MissingNonHtmlAndEscapingEntryPointsRejectTheLibrary(string entryPoint)
  {
    using LibraryAppletFixture fixture = new();
    fixture.Write(applet: new LibraryApplet("terminal", "Terminal", entryPoint));

    LibraryCatalog catalog = fixture.Read();
    Assert.Empty(catalog.Applications);
    Assert.Single(catalog.Diagnostics);
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public void EntryPointsCannotPassThroughFileOrDirectorySymlinks(bool directoryLink)
  {
    using LibraryAppletFixture fixture = new();
    string directory = fixture.Write();
    string entryPoint;
    if (directoryLink)
    {
      Directory.CreateSymbolicLink(Path.Combine(directory, "linked"), Path.Combine(directory, "applets", "terminal"));
      entryPoint = "linked/index.html";
    }
    else
    {
      File.CreateSymbolicLink(Path.Combine(directory, "linked.html"), Path.Combine(directory, "applets", "terminal", "index.html"));
      entryPoint = "linked.html";
    }

    LibraryManifest manifest = new(1, "utilities", "1.0.0", "Utilities", null, [], null, null, [new LibraryApplet("terminal", "Terminal", entryPoint)]);
    File.WriteAllText(Path.Combine(directory, "Library.json"), System.Text.Json.JsonSerializer.Serialize(manifest));
    LibraryCatalog catalog = fixture.Read();
    Assert.Empty(catalog.Applications);
    Assert.Contains("symbolic link", Assert.Single(catalog.Diagnostics).Message);
  }
}
