namespace Holodeck.Web.Rooms;

public sealed record LibraryApplet(string Id, string Name, string EntryPoint, string? Description = null, string? Icon = null, List<string>? Services = null, int Width = 800, int Height = 600)
{
  public const string DefaultIcon = "▣";
}
