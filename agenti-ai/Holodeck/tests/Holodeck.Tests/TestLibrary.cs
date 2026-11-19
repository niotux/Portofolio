using System.Text.Json;
using Holodeck.Web.Computers;
using Holodeck.Web.Configuration;
using Holodeck.Web.Rooms;
using Microsoft.Extensions.Options;

namespace Holodeck.Tests;

// Packages test agents into one local library under the data path's libs/, the way an author would.
internal static class TestLibrary
{
  public const string Id = "test";
  public const string Version = "1.0.0";
  public const string RuntimeImage = "holodeck-library:1";

  public static RoomPaths Paths(string dataPath)
  {
    return new RoomPaths(Options.Create(new StudioOptions { DataPath = dataPath }));
  }

  public static string Reference(string agentId)
  {
    return $"{Id}/{Version}/agents/{agentId}";
  }

  // Writes the agent and returns its folder. Start is the command and its arguments; use ${Agent} for files beside Agent.json.
  public static string Write(string dataPath, string agentId, string name, List<string> start, string? settings = null, AgentNeeds? needs = null, string? schema = null)
  {
    string library = Path.Combine(dataPath, "libs", Id);
    string directory = Path.Combine(library, "agents", agentId);
    Directory.CreateDirectory(directory);
    Directory.CreateDirectory(Path.Combine(library, "teams"));
    LibraryManifest manifest = new(1, Id, Version, "Test library", null, new List<LibraryDependency>(), Runtime: new LibraryEnvironmentDefinition(RuntimeImage, new()));
    File.WriteAllText(Path.Combine(library, "Library.json"), JsonSerializer.Serialize(manifest));
    List<string> args = new();
    for (int index = 1; index < start.Count; index++)
    {
      args.Add(start[index]);
    }

    string command = start.Count > 0 ? start[0] : "";
    string? settingsName = null;
    if (settings is not null)
    {
      settingsName = "settings.json";
      File.WriteAllText(Path.Combine(directory, settingsName), settings);
    }

    string? schemaName = null;
    if (schema is not null)
    {
      schemaName = "settings.schema.json";
      File.WriteAllText(Path.Combine(directory, schemaName), schema);
    }

    PackageAgentManifest agent = new(agentId, name, name + " agent", new AgentLaunch(command, args), settingsName, needs is null ? null : new AgentNeedsManifest(needs.Ports), schemaName);
    File.WriteAllText(Path.Combine(directory, "Agent.json"), JsonSerializer.Serialize(agent));
    return directory;
  }

  public static LibraryAgent Agent(string dataPath, string agentId)
  {
    return new AgentLibrary(Paths(dataPath)).Get(Reference(agentId));
  }
}
