using Holodeck.Web.Computers;

namespace Holodeck.Web.Rooms;

public sealed record LibraryApplicationPackage(string Id, string Version, string Directory, LibraryEnvironmentDefinition? Runtime, List<LibraryApplet> Applets, List<LibraryMount>? Dependencies = null)
{
  public string Key => Id + "/" + Version;
}
