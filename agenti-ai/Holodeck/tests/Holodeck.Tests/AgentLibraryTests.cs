using Holodeck.Web.Rooms;
using Xunit;

namespace Holodeck.Tests;

public sealed class AgentLibraryTests : IDisposable
{
  private readonly string dataPath = Path.Combine(Path.GetTempPath(), "holodeck-library-" + Guid.NewGuid().ToString("N"));

  [Fact]
  public void ListsOnlyPackagedAgents()
  {
    TestLibrary.Write(dataPath, "talker", "Talker", ["python3", "${Agent}/agent.py"]);
    // A folder in the old data/library layout is not an agent any more.
    string old = Path.Combine(dataPath, "library", "echo");
    Directory.CreateDirectory(old);
    File.WriteAllText(Path.Combine(old, "agent.json"), """{"name":"Echo","start":["python3","agent.py"]}""");
    AgentLibrary library = new(TestLibrary.Paths(dataPath));
    Assert.Empty(library.ReadCatalog().Diagnostics);
    LibraryAgent agent = Assert.Single(library.List());
    Assert.Equal(TestLibrary.Reference("talker"), agent.Folder);
    Assert.Equal("Talker", agent.Name);
    Assert.Equal(["python3", "${Agent}/agent.py"], agent.Start);
    Assert.EndsWith(Path.Combine("agents", "talker"), agent.Directory);
  }

  [Fact]
  public void ListIsEmptyWithoutALibrary()
  {
    AgentLibrary library = new(TestLibrary.Paths(dataPath));
    Assert.Empty(library.List());
  }

  [Fact]
  public void GetRejectsUnknownAndEscapingReferences()
  {
    TestLibrary.Write(dataPath, "talker", "Talker", ["python3", "${Agent}/agent.py"]);
    AgentLibrary library = new(TestLibrary.Paths(dataPath));
    Assert.Equal("Talker", library.Get(TestLibrary.Reference("talker")).Name);
    Assert.Throws<KeyNotFoundException>(() => library.Get("talker"));
    Assert.Throws<KeyNotFoundException>(() => library.Get(TestLibrary.Reference("missing")));
    Assert.Throws<ArgumentException>(() => library.Get("../x"));
    Assert.Throws<ArgumentException>(() => library.Get(".hidden"));
  }

  public void Dispose()
  {
    if (Directory.Exists(dataPath))
    {
      Directory.Delete(dataPath, true);
    }
  }
}
