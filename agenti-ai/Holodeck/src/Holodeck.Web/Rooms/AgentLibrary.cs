namespace Holodeck.Web.Rooms;

// Agents come only from local library packages.
public sealed partial class AgentLibrary(RoomPaths paths)
{
  private readonly Lock gate = new();

  public List<LibraryAgent> List() => ReadCatalog().Agents;

  public LibraryCatalog ReadCatalog()
  {
    lock (gate)
    {
      List<LibraryDiagnostic> diagnostics = new();
      Dictionary<string, LibraryPackage> packages = Discover(diagnostics);
      foreach (LibraryPackage package in packages.Values)
      {
        ValidateDependencies(package, packages, new HashSet<string>(StringComparer.Ordinal), diagnostics);
      }

      foreach (LibraryPackage package in packages.Values)
      {
        if (!package.Invalid)
        {
          ReadAgents(package, packages, diagnostics);
        }
      }

      // Invalid agent manifests also make dependent teams unavailable.
      foreach (LibraryPackage package in packages.Values)
      {
        ValidateDependencies(package, packages, new HashSet<string>(StringComparer.Ordinal), diagnostics);
      }

      foreach (LibraryPackage package in packages.Values)
      {
        if (!package.Invalid)
        {
          ReadTeams(package, packages, diagnostics);
        }
      }

      foreach (LibraryPackage package in packages.Values)
      {
        ValidateDependencies(package, packages, new HashSet<string>(StringComparer.Ordinal), diagnostics);
      }

      List<LibraryAgent> agents = new();
      List<LibraryTeam> teams = new();
      List<LibrarySummary> libraries = new();
      List<LibraryApplicationPackage> applications = new();
      foreach (LibraryPackage package in packages.Values)
      {
        if (!package.Invalid)
        {
          libraries.Add(Summarize(package.Manifest));
          applications.Add(new LibraryApplicationPackage(package.Manifest.Id!, package.Manifest.Version!, package.Directory, package.Manifest.Runtime, package.Manifest.Applets ?? new(), ResolveDependencyMounts(package, packages)));
          agents.AddRange(package.Agents);
          teams.AddRange(package.Teams);
        }
      }

      agents.Sort((first, second) => string.Compare(first.Name, second.Name, StringComparison.OrdinalIgnoreCase));
      teams.Sort((first, second) => string.Compare(first.Name, second.Name, StringComparison.OrdinalIgnoreCase));
      libraries.Sort((first, second) => string.Compare(first.Name, second.Name, StringComparison.OrdinalIgnoreCase));
      return new LibraryCatalog(agents, teams, diagnostics, libraries) { Applications = applications };
    }
  }

  private static LibrarySummary Summarize(LibraryManifest manifest)
  {
    List<LibraryReference>? dependencies = null;
    if (manifest.Dependencies is not null)
    {
      if (manifest.Dependencies.Count > 0)
      {
        dependencies = new();
        foreach (LibraryDependency dependency in manifest.Dependencies)
        {
          dependencies.Add(new LibraryReference(dependency.Id!, dependency.Version!));
        }
      }
    }

    return new LibrarySummary(manifest.Id!, manifest.Version!, manifest.Name!, manifest.Description ?? "", dependencies);
  }

  public LibraryAgent Get(string reference)
  {
    ValidateReference(reference);
    LibraryCatalog catalog = ReadCatalog();
    foreach (LibraryAgent agent in catalog.Agents)
    {
      if (agent.Folder == reference)
      {
        return agent;
      }
    }

    throw new KeyNotFoundException("That agent is not in an available local library. Check library diagnostics.");
  }

  public LibraryTeam GetTeam(string reference)
  {
    ValidateReference(reference);
    foreach (LibraryTeam team in ReadCatalog().Teams)
    {
      if (team.Folder == reference)
      {
        return team;
      }
    }

    throw new KeyNotFoundException("That team is not in an available local library. Check library diagnostics.");
  }

  private static void Reject(LibraryPackage package, string message, List<LibraryDiagnostic> diagnostics)
  {
    if (!package.Invalid)
    {
      diagnostics.Add(new LibraryDiagnostic(package.Source, message));
    }

    package.Invalid = true;
  }

  private static void ValidateReference(string reference)
  {
    if (string.IsNullOrWhiteSpace(reference) || reference.Contains('\\'))
    {
      throw new ArgumentException("Use a library reference.");
    }

    foreach (string part in reference.Split('/'))
    {
      LibraryFiles.RequireId(part, "Library reference segment");
      if (part.StartsWith('.'))
      {
        throw new ArgumentException("Use a library reference.");
      }
    }
  }
}
