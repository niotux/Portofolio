namespace Holodeck.Web.Management;

internal sealed record ManagementManifest(int FormatVersion, List<ManagementActionManifest>? Actions);
