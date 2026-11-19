namespace Holodeck.Web.Rooms;

public sealed partial class AgentLibrary
{
  private Dictionary<string, LibraryPackage> Discover(List<LibraryDiagnostic> diagnostics)
  {
    Dictionary<string, LibraryPackage> packages = new(StringComparer.Ordinal);
    if (!Directory.Exists(paths.Libraries))
    {
      return packages;
    }

    foreach (string source in Directory.GetFileSystemEntries(paths.Libraries))
    {
      string name = Path.GetFileName(source);
      if (name.StartsWith('.'))
      {
        continue;
      }

      try
      {
        string directory;
        if (Directory.Exists(source))
        {
          directory = source;
        }
        else if (string.Equals(Path.GetExtension(source), ".zip", StringComparison.OrdinalIgnoreCase))
        {
          directory = LibraryArchive.Extract(source, paths.LibraryCache);
        }
        else
        {
          continue;
        }

        LibraryManifest manifest = LibraryFiles.Read<LibraryManifest>(Path.Combine(directory, "Library.json"));
        ValidateManifest(manifest);
        ValidateApplications(manifest, directory);
        LibraryPackage package = new(name, directory, manifest);
        if (packages.TryGetValue(package.Key, out LibraryPackage? existing))
        {
          existing.Invalid = true;
          diagnostics.Add(new LibraryDiagnostic(name, $"Duplicate library identity '{package.Key}', also provided by '{existing.Source}'."));
          continue;
        }

        packages.Add(package.Key, package);
      }
      catch (Exception exception) when (LibraryFiles.IsManifestError(exception))
      {
        diagnostics.Add(new LibraryDiagnostic(name, exception.Message));
      }
    }

    return packages;
  }

  private static void ValidateManifest(LibraryManifest manifest)
  {
    if (manifest.FormatVersion != 1)
    {
      throw new ArgumentException("Library FormatVersion must be 1.");
    }

    LibraryFiles.RequireId(manifest.Id, "Library Id");
    LibraryFiles.RequireId(manifest.Version, "Library Version");
    AgentNames.Validate(manifest.Name ?? "");
    if (manifest.Services is not null && manifest.Services.Value.ValueKind != System.Text.Json.JsonValueKind.Null)
    {
      throw new ArgumentException("Library Services are no longer supported; declare service processes under Runtime.Processes.");
    }

    if (manifest.Dependencies is null)
    {
      return;
    }

    HashSet<string> ids = new(StringComparer.Ordinal);
    foreach (LibraryDependency dependency in manifest.Dependencies)
    {
      if (dependency is null)
      {
        throw new ArgumentException("Dependencies must contain library objects.");
      }

      string id = LibraryFiles.RequireId(dependency.Id, "Dependency Id");
      LibraryFiles.RequireId(dependency.Version, "Dependency Version");
      if (!ids.Add(id))
      {
        throw new ArgumentException($"Dependency '{id}' is declared more than once.");
      }
    }
  }

  private static bool ValidateDependencies(LibraryPackage package, Dictionary<string, LibraryPackage> packages, HashSet<string> visiting, List<LibraryDiagnostic> diagnostics)
  {
    if (package.Invalid)
    {
      return false;
    }

    if (!visiting.Add(package.Key))
    {
      Reject(package, "Circular library dependency.", diagnostics);
      return false;
    }

    if (package.Manifest.Dependencies is not null)
    {
      foreach (LibraryDependency dependency in package.Manifest.Dependencies)
      {
        string key = $"{dependency.Id}/{dependency.Version}";
        if (!packages.TryGetValue(key, out LibraryPackage? target))
        {
          Reject(package, $"Missing local dependency '{key}'.", diagnostics);
        }
        else if (!ValidateDependencies(target, packages, visiting, diagnostics))
        {
          Reject(package, $"Local dependency '{key}' is unavailable.", diagnostics);
        }
      }
    }

    visiting.Remove(package.Key);
    return !package.Invalid;
  }
}
