namespace Holodeck.Web.Management;

internal sealed record ManagementActionManifest(string? Id, string? Title, string? Description, string? InputSchema, string? ResultSchema, List<string>? FollowUpActions);
