namespace Holodeck.Web.Rooms;

public sealed partial class AgentLibrary
{
  private const int MaximumApplicationProcesses = 64;
  private const int MinimumApplicationPort = 1024;
  private const int MaximumApplicationPort = 65535;
  private const int SupervisorPort = 8079;

  private static void ValidateApplications(LibraryManifest manifest, string directory)
  {
    HashSet<string> services = new(StringComparer.Ordinal);
    HashSet<int> ports = new();
    if (manifest.Runtime is not null)
    {
      if (string.IsNullOrWhiteSpace(manifest.Runtime.Image))
      {
        throw new ArgumentException("Runtime.Image is required.");
      }

      if (manifest.Runtime.Processes is null)
      {
        throw new ArgumentException("Runtime.Processes must be an array.");
      }

      if (manifest.Runtime.Processes.Count > MaximumApplicationProcesses)
      {
        throw new ArgumentException("Too many runtime processes.");
      }

      foreach (Computers.LibraryEnvironmentProcess process in manifest.Runtime.Processes)
      {
        if (process is null)
        {
          throw new ArgumentException("Runtime processes must be objects.");
        }

        LibraryFiles.RequireId(process.Id, "Process Id");
        if (!services.Add(process.Id))
        {
          throw new ArgumentException($"Duplicate runtime process '{process.Id}'.");
        }

        if (process.Command is null)
        {
          throw new ArgumentException($"Process '{process.Id}' requires Command.");
        }

        if (process.Command.Count == 0 || string.IsNullOrWhiteSpace(process.Command[0]))
        {
          throw new ArgumentException($"Process '{process.Id}' requires a command.");
        }

        foreach (string argument in process.Command)
        {
          if (argument is null || argument.Contains('\0'))
          {
            throw new ArgumentException("Process arguments must be valid strings.");
          }
        }

        if (process.Port is not null)
        {
          int port = process.Port.Value;
          if (port < MinimumApplicationPort || port > MaximumApplicationPort || port == SupervisorPort || !ports.Add(port))
          {
            throw new ArgumentException($"Process '{process.Id}' requires a unique unprivileged service port.");
          }
        }

        if (process.HealthPath is not null && (!process.HealthPath.StartsWith('/') || process.HealthPath.StartsWith("//") || process.Port is null))
        {
          throw new ArgumentException($"Process '{process.Id}' health path requires a local path and service port.");
        }
      }
    }

    if (manifest.Applets is null)
    {
      return;
    }

    HashSet<string> applets = new(StringComparer.Ordinal);
    foreach (LibraryApplet applet in manifest.Applets)
    {
      if (applet is null)
      {
        throw new ArgumentException("Applets must contain objects.");
      }

      LibraryFiles.RequireId(applet.Id, "Applet Id");
      AgentNames.Validate(applet.Name ?? "");
      if (!applets.Add(applet.Id))
      {
        throw new ArgumentException($"Duplicate applet '{applet.Id}'.");
      }

      string entry = LibraryFiles.Inside(directory, applet.EntryPoint);
      if (!File.Exists(entry) || !string.Equals(Path.GetExtension(entry), ".html", StringComparison.OrdinalIgnoreCase))
      {
        throw new ArgumentException($"Applet '{applet.Id}' EntryPoint must name an existing HTML file.");
      }

      if (applet.Services is not null)
      {
        foreach (string service in applet.Services)
        {
          if (!services.Contains(service))
          {
            throw new ArgumentException($"Applet '{applet.Id}' references undeclared process '{service}'.");
          }
        }
      }
    }
  }
}
