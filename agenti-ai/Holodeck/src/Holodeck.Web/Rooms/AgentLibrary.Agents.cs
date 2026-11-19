using Holodeck.Web.Computers;
using Holodeck.Web.Settings;
using Holodeck.Web.Management;

namespace Holodeck.Web.Rooms;

public sealed partial class AgentLibrary
{
  private static void ReadAgents(LibraryPackage package, Dictionary<string, LibraryPackage> packages, List<LibraryDiagnostic> diagnostics)
  {
    try
    {
      string directory = LibraryFiles.Inside(package.Directory, "agents");
      if (!Directory.Exists(directory))
      {
        throw new ArgumentException("A library must contain an agents directory.");
      }

      List<LibraryMount> dependencies = ResolveDependencyMounts(package, packages);
      Dictionary<string, string> dependencyPaths = new(StringComparer.Ordinal);
      foreach (LibraryMount dependency in dependencies)
      {
        dependencyPaths.Add(dependency.Id, dependency.Directory);
      }

      HashSet<string> ids = new(StringComparer.Ordinal);
      foreach (string agentDirectory in Directory.GetDirectories(directory))
      {
        string relative = Path.GetRelativePath(package.Directory, agentDirectory);
        string manifestPath = LibraryFiles.Inside(package.Directory, relative + "/Agent.json");
        PackageAgentManifest manifest = LibraryFiles.Read<PackageAgentManifest>(manifestPath);
        string id = LibraryFiles.RequireId(manifest.Id, "Agent Id");
        if (!ids.Add(id))
        {
          throw new ArgumentException($"Duplicate agent Id '{id}'.");
        }

        string name = AgentNames.Validate(manifest.Name ?? "");
        if (manifest.Launch is null || string.IsNullOrWhiteSpace(manifest.Launch.Command))
        {
          throw new ArgumentException($"Agent '{id}' requires Launch.Command.");
        }

        List<string> start = new() { manifest.Launch.Command };
        if (manifest.Launch.Args is not null)
        {
          foreach (string argument in manifest.Launch.Args)
          {
            if (argument is null)
            {
              throw new ArgumentException($"Agent '{id}' launch arguments must be strings.");
            }

            start.Add(argument);
          }
        }

        foreach (string argument in start)
        {
          Placeholders.Expand(argument, agentDirectory, package.Directory, dependencyPaths);
        }

        string? settings = FindFile(package, relative, manifest.Settings, id, "settings");
        string? schemaPath = FindFile(package, relative, manifest.SettingsSchema, id, "settings schema");
        string? actionsPath = FindFile(package, relative, manifest.Actions, id, "actions");
        ManagementDefinition? actions = actionsPath is null ? null : ManagementDefinitions.Read(package.Directory, actionsPath);
        SettingsSchema? schema = null;
        if (schemaPath is not null)
        {
          schema = ReadSchema(schemaPath, id);
          RequireValidInitialSettings(settings, schema, id);
        }

        if (package.Manifest.Runtime is null)
        {
          throw new ArgumentException($"Agent '{id}' requires its library to declare Runtime.");
        }

        package.Agents.Add(new LibraryAgent($"{package.Key}/agents/{id}", name, manifest.Description ?? "", start, agentDirectory, package.Directory, ReadNeeds(manifest.Needs), settings, schema, actions, package.Key, dependencies, package.Manifest.Runtime));
      }
    }
    catch (Exception exception) when (LibraryFiles.IsManifestError(exception))
    {
      Reject(package, exception.Message, diagnostics);
    }
  }

  private static List<LibraryMount> ResolveDependencyMounts(LibraryPackage package, Dictionary<string, LibraryPackage> packages)
  {
    List<LibraryMount> mounts = new();
    if (package.Manifest.Dependencies is null)
    {
      return mounts;
    }

    foreach (LibraryDependency dependency in package.Manifest.Dependencies)
    {
      string key = $"{dependency.Id}/{dependency.Version}";
      if (!packages.TryGetValue(key, out LibraryPackage? resolved) || resolved.Invalid)
      {
        throw new ArgumentException($"Missing or unavailable local dependency '{key}'.");
      }

      mounts.Add(new LibraryMount(dependency.Id!, dependency.Version!, resolved.Directory));
    }

    return mounts;
  }

  // Definition files are named relative to the agent folder and must stay inside the same library.
  private static string? FindFile(LibraryPackage package, string agentFolder, string? declared, string id, string label)
  {
    if (declared is null)
    {
      return null;
    }

    string path = LibraryFiles.Inside(package.Directory, agentFolder + "/" + declared);
    if (!File.Exists(path))
    {
      throw new ArgumentException($"Agent '{id}' {label} file does not exist.");
    }

    return path;
  }

  private static SettingsSchema ReadSchema(string path, string id)
  {
    try
    {
      return SettingsSchemaReader.Read(path);
    }
    catch (Exception exception) when (LibraryFiles.IsManifestError(exception))
    {
      throw new ArgumentException($"Agent '{id}' settings schema: {exception.Message}");
    }
  }

  // Every new instance starts from these settings, so they must already satisfy the schema.
  private static void RequireValidInitialSettings(string? settingsPath, SettingsSchema schema, string id)
  {
    string text = settingsPath is null ? SettingsDocument.Empty : File.ReadAllText(settingsPath);
    List<SettingsFieldError> errors = SettingsValidator.Validate(schema.Root, SettingsDocument.Parse(text));
    if (errors.Count > 0)
    {
      throw new ArgumentException($"Agent '{id}' settings do not satisfy its schema at '{errors[0].Path}': {errors[0].Message}");
    }
  }

  private static AgentNeeds ReadNeeds(AgentNeedsManifest? declared)
  {
    List<int> ports = new();
    if (declared is null || declared.Ports is null)
    {
      return new AgentNeeds(ports);
    }

    foreach (int port in declared.Ports)
    {
      if (port < 1 || port > 65535)
      {
        throw new ArgumentException($"Needs.Ports entry {port} is not a valid port.");
      }

      ports.Add(port);
    }

    return new AgentNeeds(ports);
  }
}
