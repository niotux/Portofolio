namespace Holodeck.Web.Rooms;

internal sealed class LibraryPackage(string source, string directory, LibraryManifest manifest)
{
  public string Source { get; } = source;
  public string Directory { get; } = directory;
  public LibraryManifest Manifest { get; } = manifest;
  public string Key => $"{Manifest.Id}/{Manifest.Version}";
  public bool Invalid { get; set; }
  public List<LibraryAgent> Agents { get; } = new();
  public List<LibraryTeam> Teams { get; } = new();
}
