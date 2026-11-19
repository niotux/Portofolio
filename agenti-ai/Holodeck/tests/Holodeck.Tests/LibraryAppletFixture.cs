using System.Text.Json;
using Holodeck.Web.Computers;
using Holodeck.Web.Rooms;

namespace Holodeck.Tests;

internal sealed class LibraryAppletFixture : IDisposable
{
  private readonly bool ownsDirectory;

  public LibraryAppletFixture(string? dataPath = null)
  {
    ownsDirectory = dataPath is null;
    DataPath = dataPath ?? Path.Combine(Path.GetTempPath(), "holodeck-applets-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(DataPath);
  }

  public string DataPath { get; }

  public string Write(string id = "utilities", string version = "1.0.0", LibraryEnvironmentDefinition? runtime = null, LibraryApplet? applet = null)
  {
    string directory = Path.Combine(DataPath, "libs", id + "-" + version);
    Directory.CreateDirectory(Path.Combine(directory, "agents"));
    Directory.CreateDirectory(Path.Combine(directory, "teams"));
    Directory.CreateDirectory(Path.Combine(directory, "applets", "terminal"));
    File.WriteAllText(Path.Combine(directory, "applets", "terminal", "index.html"), "<!doctype html><title>Test terminal</title>");
    File.WriteAllText(Path.Combine(directory, "applets", "terminal", "terminal.js"), "document.body.dataset.loaded = 'yes';");
    File.WriteAllText(Path.Combine(directory, "private.txt"), "library-private-content");
    LibraryApplet definition = applet ?? new LibraryApplet("terminal", "Terminal", "applets/terminal/index.html");
    LibraryManifest manifest = new(1, id, version, "Utility library", "Applications", [], null, runtime, [definition]);
    File.WriteAllText(Path.Combine(directory, "Library.json"), JsonSerializer.Serialize(manifest));
    return directory;
  }

  public LibraryCatalog Read()
  {
    return new AgentLibrary(TestLibrary.Paths(DataPath)).ReadCatalog();
  }

  public void Dispose()
  {
    if (ownsDirectory && Directory.Exists(DataPath))
    {
      Directory.Delete(DataPath, true);
    }
  }
}
