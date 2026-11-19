namespace Holodeck.Web.Computers;

// Where Holodeck's own folders are on the Docker host, as Docker needs them for bind mounts.
public sealed record HostPaths(string ProjectDirectory, string Data, string Libraries);
