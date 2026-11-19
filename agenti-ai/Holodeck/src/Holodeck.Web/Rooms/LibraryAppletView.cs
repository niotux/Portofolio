namespace Holodeck.Web.Rooms;

public sealed record LibraryAppletView(string Id, string Title, string Description, string Icon, string LibraryId, string LibraryVersion, string EntryPoint, int Width, int Height, List<string> Services);
