namespace Holodeck.Web.Rooms;

// A directly declared dependency, resolved to its exact local package directory.
public sealed record LibraryMount(string Id, string Version, string Directory);
