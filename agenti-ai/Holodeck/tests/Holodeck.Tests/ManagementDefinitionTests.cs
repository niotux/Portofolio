using System.Text.Json;
using Holodeck.Web.Management;
using Holodeck.Web.Rooms;
using Xunit;

namespace Holodeck.Tests;

public sealed class ManagementDefinitionTests
{
  [Fact]
  public void DeclarationsValidatePathsSchemasAndFollowUpsAndHashSourceEdits()
  {
    string root = Path.Combine(Path.GetTempPath(), "holodeck-actions-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
      string directory = TestLibrary.Write(root, "agent", "Worker", ["sleep", "60"]);
      ManagementFixture.WriteDefinition(directory);
      string manifestPath = Path.Combine(directory, "Agent.json");
      PackageAgentManifest agent = JsonSerializer.Deserialize<PackageAgentManifest>(File.ReadAllText(manifestPath))!;
      File.WriteAllText(manifestPath, JsonSerializer.Serialize(agent with { Actions = "actions.json" }));
      string actionsPath = Path.Combine(directory, "actions.json");
      string declaration = File.ReadAllText(actionsPath);
      string library = Path.Combine(root, "libs", TestLibrary.Id);
      ManagementDefinition first = ManagementDefinitions.Read(library, actionsPath);
      Assert.Equal(2, first.Actions.Count);
      Assert.Equal("apply", Assert.Single(first.Actions[0].FollowUpActions));
      Assert.NotNull(TestLibrary.Agent(root, "agent").Actions);
      File.AppendAllText(Path.Combine(directory, "result.schema.json"), "\n");
      Assert.NotEqual(first.DefinitionRevision, ManagementDefinitions.Read(library, actionsPath).DefinitionRevision);
      File.WriteAllText(Path.Combine(directory, "result.schema.json"), ManagementFixture.Schema);
      List<ManagementActionManifest> duplicate =
      [
        new("same", "One", "First.", "input.schema.json", "result.schema.json", null),
        new("same", "Two", "Second.", "input.schema.json", "result.schema.json", null)
      ];
      File.WriteAllText(actionsPath, JsonSerializer.Serialize(new ManagementManifest(1, duplicate)));
      Assert.Throws<ArgumentException>(() => ManagementDefinitions.Read(library, actionsPath));
      File.WriteAllText(actionsPath, declaration.Replace("\"apply\"]", "\"missing\"]", StringComparison.Ordinal));
      Assert.Throws<ArgumentException>(() => ManagementDefinitions.Read(library, actionsPath));
      File.WriteAllText(actionsPath, declaration.Replace("input.schema.json", "../../../../outside.json", StringComparison.Ordinal));
      Assert.Throws<ArgumentException>(() => ManagementDefinitions.Read(library, actionsPath));
      File.WriteAllText(actionsPath, declaration);
      File.WriteAllText(Path.Combine(directory, "input.schema.json"), """{"type":"object","unsupported":true}""");
      Assert.Throws<ArgumentException>(() => ManagementDefinitions.Read(library, actionsPath));
      Assert.Throws<KeyNotFoundException>(() => TestLibrary.Agent(root, "agent"));
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }
}
