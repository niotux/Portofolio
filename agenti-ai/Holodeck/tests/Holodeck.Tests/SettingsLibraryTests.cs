using Holodeck.Web.Computers;
using System.Text.Json;
using Holodeck.Web.Rooms;
using Xunit;

namespace Holodeck.Tests;

// How the catalog resolves SettingsSchema and the reserved Actions path inside one library.
public sealed class SettingsLibraryTests : IDisposable
{
  private readonly string dataPath = Path.Combine(Path.GetTempPath(), "holodeck-settings-library-" + Guid.NewGuid().ToString("N"));

  [Fact]
  public void ASchemaElsewhereInTheSameLibraryIsUsed()
  {
    string library = WriteLibrary("shared", "{}", "../../shared/settings.schema.json", null);
    Directory.CreateDirectory(Path.Combine(library, "shared"));
    File.WriteAllText(Path.Combine(library, "shared", "settings.schema.json"), """{ "type": "object", "additionalProperties": true }""");

    LibraryCatalog catalog = Catalog();
    Assert.Empty(catalog.Diagnostics);
    Assert.NotNull(Assert.Single(catalog.Agents).SettingsSchema);
  }

  [Theory]
  [InlineData("../../../outside.schema.json", null)]
  [InlineData("/etc/hostname", null)]
  [InlineData("missing.schema.json", null)]
  [InlineData("settings.schema.json", "../../../outside-actions.json")]
  [InlineData("settings.schema.json", "missing-actions.json")]
  public void PathsOutsideTheLibraryOrMissingFilesMakeTheLibraryUnavailable(string schema, string? actions)
  {
    string library = WriteLibrary("escape", "{}", schema, actions);
    File.WriteAllText(Path.Combine(library, "agents", "probe", "settings.schema.json"), """{ "type": "object", "additionalProperties": true }""");
    File.WriteAllText(Path.Combine(dataPath, "libs", "outside.schema.json"), """{ "type": "object", "additionalProperties": true }""");
    File.WriteAllText(Path.Combine(dataPath, "libs", "outside-actions.json"), "{}");

    LibraryCatalog catalog = Catalog();
    Assert.Empty(catalog.Agents);
    Assert.Single(catalog.Diagnostics);
  }

  [Fact]
  public void ASchemaLinkedOutOfTheLibraryIsRefused()
  {
    string library = WriteLibrary("linked", "{}", "settings.schema.json", null);
    string outside = Path.Combine(dataPath, "outside.schema.json");
    File.WriteAllText(outside, """{ "type": "object", "additionalProperties": true }""");
    File.CreateSymbolicLink(Path.Combine(library, "agents", "probe", "settings.schema.json"), outside);

    LibraryCatalog catalog = Catalog();
    Assert.Empty(catalog.Agents);
    Assert.Contains("symbolic link", Assert.Single(catalog.Diagnostics).Message);
  }

  [Theory]
  [InlineData("""{ "type": "object", "additionalProperties": true, "properties": { "a": { "type": "string", "pattern": "x" } } }""", "{}", "pattern")]
  [InlineData("""{ "type": "object", "additionalProperties": false, "properties": {} }""", """{ "extra": 1 }""", "do not satisfy")]
  [InlineData("""{ "type": "object", "additionalProperties": true, "required": ["a"], "properties": { "a": { "type": "string" } } }""", "{}", "do not satisfy")]
  public void AnInvalidSchemaOrInitialSettingsIsADiagnostic(string schema, string settings, string expected)
  {
    string library = WriteLibrary("invalid", settings, "settings.schema.json", null);
    File.WriteAllText(Path.Combine(library, "agents", "probe", "settings.schema.json"), schema);

    LibraryCatalog catalog = Catalog();
    Assert.Empty(catalog.Agents);
    Assert.Contains(expected, Assert.Single(catalog.Diagnostics).Message);
  }

  [Fact]
  public void ADefinitionWithoutASchemaIsUnchanged()
  {
    WriteLibrary("plain", "{\"any\": \"thing\"}", null, null);
    LibraryAgent agent = Assert.Single(Catalog().Agents);
    Assert.Null(agent.SettingsSchema);
    Assert.NotNull(agent.SettingsPath);
  }

  public void Dispose()
  {
    if (Directory.Exists(dataPath))
    {
      Directory.Delete(dataPath, true);
    }
  }

  private LibraryCatalog Catalog()
  {
    return new AgentLibrary(TestLibrary.Paths(dataPath)).ReadCatalog();
  }

  private string WriteLibrary(string id, string settings, string? schema, string? actions)
  {
    string library = Path.Combine(dataPath, "libs", id);
    string agent = Path.Combine(library, "agents", "probe");
    Directory.CreateDirectory(agent);
    Directory.CreateDirectory(Path.Combine(library, "teams"));
    File.WriteAllText(Path.Combine(library, "Library.json"), JsonSerializer.Serialize(new LibraryManifest(1, id, "1.0.0", id, "", null, Runtime: new LibraryEnvironmentDefinition(TestLibrary.RuntimeImage, new()))));
    File.WriteAllText(Path.Combine(agent, "settings.json"), settings);
    PackageAgentManifest manifest = new("probe", "Probe", "", new AgentLaunch("sh", ["${Agent}/run.sh"]), "settings.json", null, schema, actions);
    File.WriteAllText(Path.Combine(agent, "Agent.json"), JsonSerializer.Serialize(manifest));
    return library;
  }
}
