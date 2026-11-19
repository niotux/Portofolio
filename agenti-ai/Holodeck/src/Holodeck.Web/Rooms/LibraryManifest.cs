namespace Holodeck.Web.Rooms;

internal sealed record LibraryManifest(int FormatVersion, string? Id, string? Version, string? Name, string? Description, List<LibraryDependency>? Dependencies, System.Text.Json.JsonElement? Services = null, Holodeck.Web.Computers.LibraryEnvironmentDefinition? Runtime = null, List<LibraryApplet>? Applets = null);
