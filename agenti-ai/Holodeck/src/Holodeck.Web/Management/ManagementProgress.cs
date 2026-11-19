namespace Holodeck.Web.Management;

public sealed record ManagementProgress(string? AttemptToken, int? Progress, string? Message);
