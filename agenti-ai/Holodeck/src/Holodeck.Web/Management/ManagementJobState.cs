namespace Holodeck.Web.Management;

internal sealed record ManagementJobState(ManagementJob Job, string SettingsContent, string? SessionHash, string? AttemptToken, DateTimeOffset? LeaseExpiresAt);
